using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Sonora.Models;
using Sonora.Services;
using static Sonora.Spotify.SpotifyCatalog;
using Sonora.Appearance.Localization;

namespace Sonora.Spotify;

public sealed record SpotifyState(
    Track? Track, bool IsPlaying, double PositionSec, double DurationSec,
    bool Shuffle, RepeatMode Repeat, DateTime AtUtc, string DeviceName, bool IsSonoraDevice,
    string? ContextUri = null); // the playlist, album or artist playing from; null for a bare list of tracks

/// <summary>
/// Spotify playback through official channels only:
/// - Spotify's Web Playback SDK, hosted in an invisible WebView2, registers Sonora as a Spotify Connect
///   device (Premium required; protected audio is handled by the SDK and the browser's CDM).
/// - Web API player endpoints drive whichever device is active (Sonora, the official app, a phone…).
/// </summary>
public sealed class SpotifyEngine(SpotifyApi api, SpotifyAuth auth, SpotifyCatalog catalog, Dispatcher ui) : IDisposable
{
    const string Host = "sonora.player";

    CoreWebView2Controller? _controller;
    CoreWebView2? _web;
    DispatcherTimer? _poll;
    bool _sdkHasState;

    public string? DeviceId { get; private set; }
    public string? TargetDeviceId { get; private set; }
    public string TargetDeviceName { get; private set; } = "Sonora";
    public bool IsTargetSonora => TargetDeviceId == null || TargetDeviceId == DeviceId;
    public bool? DrmSupported { get; private set; }
    public string? DrmError { get; private set; }
    public bool SdkLoaded { get; private set; }

    public event Action<SpotifyState>? StateChanged;
    public event Action<string>? Error;
    public event Action? Ready;

    // ---------------- WebView2 host ----------------

    public async Task StartAsync(IntPtr hwnd)
    {
        if (_controller != null) return;
        var webDir = Path.Combine(StateStore.Dir, "web");
        Directory.CreateDirectory(webDir);
        await File.WriteAllTextAsync(Path.Combine(webDir, "player.html"), PlayerHtml);

        // Chromium registers its own Windows media session for the SDK's audio element, and Windows hands the
        // media keys to whichever session is playing audio: that one, which does nothing. Turning Chromium's
        // media integration off leaves Sonora's own session (see PlayerService) as the only one.
        var options = new CoreWebView2EnvironmentOptions(
            "--autoplay-policy=no-user-gesture-required --disable-features=HardwareMediaKeyHandling,MediaSessionService,GlobalMediaControls");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(StateStore.Dir, "webview"), options);
        _controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        _controller.Bounds = new System.Drawing.Rectangle(0, 0, 0, 0);
        _controller.IsVisible = true; // invisible (0×0) but not suspended, so audio keeps running
        _web = _controller.CoreWebView2;
        _web.Settings.AreDefaultContextMenusEnabled = false;
        _web.Settings.AreDevToolsEnabled = false;
        _web.Settings.IsStatusBarEnabled = false;
        _web.SetVirtualHostNameToFolderMapping(Host, webDir, CoreWebView2HostResourceAccessKind.Allow);
        _web.WebMessageReceived += OnMessage;
        _web.Navigate($"https://{Host}/player.html");

        _poll = new DispatcherTimer(DispatcherPriority.Background, ui) { Interval = TimeSpan.FromSeconds(2) };
        _poll.Tick += async (_, _) => await PollRemoteAsync();
        _poll.Start();
    }

    void Post(object message)
    {
        try { _web?.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
        catch { /* WebView torn down */ }
    }

    void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonElement m;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            m = doc.RootElement.Clone();
        }
        catch { return; }

        switch (Str(m, "type"))
        {
            case "token":
                _ = SendTokenAsync();
                break;
            case "eme":
                DrmSupported = m.GetProperty("ok").GetBoolean();
                DrmError = Str(m, "message");
                if (DrmSupported == false)
                    Error?.Invoke(Loc.T("spotify.engine_no_widevine"));
                break;
            case "sdk":
                SdkLoaded = true;
                break;
            case "ready":
                DeviceId = Str(m, "device_id");
                Ready?.Invoke();
                break;
            case "not_ready":
                if (Str(m, "device_id") == DeviceId) DeviceId = null;
                break;
            case "state":
                OnSdkState(m.TryGetProperty("state", out var s) ? s : default);
                break;
            case "error":
                Error?.Invoke(Translate(Str(m, "kind"), Str(m, "message")));
                break;
        }
    }

    async Task SendTokenAsync()
    {
        try
        {
            if (!auth.IsSignedIn) return;
            Post(new { cmd = "token", token = await auth.GetAccessTokenAsync() });
        }
        catch (Exception ex) { Error?.Invoke(ex.Message); }
    }

    static string Translate(string? kind, string? message) => kind switch
    {
        "account_error" => Loc.T("spotify.engine_account_error"),
        "authentication_error" => Loc.T("spotify.engine_auth_error"),
        "initialization_error" => Loc.T("spotify.engine_init_error", message),
        _ => Loc.T("spotify.engine_playback_error", message),
    };

    void OnSdkState(JsonElement st)
    {
        if (st.ValueKind != JsonValueKind.Object)
        {
            _sdkHasState = false; // playback moved to another device
            return;
        }
        _sdkHasState = true;
        if (TargetDeviceId != DeviceId) { TargetDeviceId = DeviceId; TargetDeviceName = "Sonora"; }

        Track? track = null;
        if (st.TryGetProperty("track_window", out var tw) && tw.TryGetProperty("current_track", out var cur))
            track = catalog.MapTrack(cur);
        var repeat = (int)Num(st, "repeat_mode") switch { 1 => RepeatMode.All, 2 => RepeatMode.One, _ => RepeatMode.Off };
        StateChanged?.Invoke(new SpotifyState(
            track,
            !(st.TryGetProperty("paused", out var p) && p.GetBoolean()),
            Num(st, "position") / 1000.0,
            Num(st, "duration") / 1000.0,
            st.TryGetProperty("shuffle", out var sh) && sh.GetBoolean(),
            repeat, DateTime.UtcNow, "Sonora", true, ContextOf(st)));
    }

    /// <summary>The URI of what is playing from: both the SDK's state and the Web API's carry it as <c>context.uri</c>.</summary>
    static string? ContextOf(JsonElement st) =>
        st.TryGetProperty("context", out var c) && c.ValueKind == JsonValueKind.Object && Str(c, "uri") is { Length: > 0 } uri ? uri : null;

    /// <summary>When another device plays, mirror its state from the Web API.</summary>
    async Task PollRemoteAsync()
    {
        if (!auth.IsSignedIn || (_sdkHasState && IsTargetSonora)) return;
        try
        {
            var st = await api.GetAsync("me/player?additional_types=episode");
            if (st.ValueKind != JsonValueKind.Object) return;
            var device = st.TryGetProperty("device", out var d) ? d : default;
            var id = Str(device, "id");
            if (id != null && id == DeviceId) return; // the SDK reports this one itself
            TargetDeviceId = id;
            TargetDeviceName = Str(device, "name") ?? Loc.T("spotify.other_device");
            var track = st.TryGetProperty("item", out var item) ? catalog.MapTrack(item) : null;
            var repeat = Str(st, "repeat_state") switch { "context" => RepeatMode.All, "track" => RepeatMode.One, _ => RepeatMode.Off };
            StateChanged?.Invoke(new SpotifyState(
                track,
                st.TryGetProperty("is_playing", out var pl) && pl.GetBoolean(),
                Num(st, "progress_ms") / 1000.0,
                track?.DurationSec ?? 0,
                st.TryGetProperty("shuffle_state", out var sh) && sh.GetBoolean(),
                repeat, DateTime.UtcNow, TargetDeviceName, false, ContextOf(st)));
        }
        catch
        {
            // polling is best effort
        }
    }

    // ---------------- playback commands ----------------

    string DeviceQuery(char sep)
    {
        var id = TargetDeviceId ?? DeviceId;
        return id != null ? $"{sep}device_id={Uri.EscapeDataString(id)}" : "";
    }

    public async Task PlayAsync(string? contextUri, IReadOnlyList<string> uris, int index, long positionMs = 0)
    {
        if (IsTargetSonora) Post(new { cmd = "activate" });
        var body = new Dictionary<string, object>();
        if (contextUri != null)
        {
            body["context_uri"] = contextUri;
            if (index >= 0 && index < uris.Count) body["offset"] = new { uri = uris[index] };
        }
        else
        {
            // The API accepts a bounded list of URIs: send a window around the selected track.
            int start = Math.Max(0, Math.Min(index, uris.Count - 1) - 20);
            var window = uris.Skip(start).Take(100).ToList();
            body["uris"] = window;
            body["offset"] = new { position = Math.Max(0, index - start) };
        }
        if (positionMs > 0) body["position_ms"] = positionMs;

        try
        {
            await api.SendAsync(HttpMethod.Put, "me/player/play" + DeviceQuery('?'), body);
        }
        catch (SpotifyException ex) when (ex.Status == 404 && DeviceId != null)
        {
            // Sonora's device may not be known by the backend yet: transfer to it, then retry once.
            await TransferAsync(DeviceId, play: false);
            await Task.Delay(400);
            await api.SendAsync(HttpMethod.Put, "me/player/play" + DeviceQuery('?'), body);
        }
    }

    public Task ResumeAsync()
    {
        if (IsTargetSonora && _sdkHasState) { Post(new { cmd = "resume" }); return Task.CompletedTask; }
        return api.SendAsync(HttpMethod.Put, "me/player/play" + DeviceQuery('?'));
    }

    public Task PauseAsync()
    {
        if (IsTargetSonora && _sdkHasState) { Post(new { cmd = "pause" }); return Task.CompletedTask; }
        return api.SendAsync(HttpMethod.Put, "me/player/pause" + DeviceQuery('?'));
    }

    public Task NextAsync()
    {
        if (IsTargetSonora && _sdkHasState) { Post(new { cmd = "next" }); return Task.CompletedTask; }
        return api.SendAsync(HttpMethod.Post, "me/player/next" + DeviceQuery('?'));
    }

    public Task PreviousAsync()
    {
        if (IsTargetSonora && _sdkHasState) { Post(new { cmd = "prev" }); return Task.CompletedTask; }
        return api.SendAsync(HttpMethod.Post, "me/player/previous" + DeviceQuery('?'));
    }

    public Task SeekAsync(double seconds)
    {
        var ms = (long)(seconds * 1000);
        if (IsTargetSonora && _sdkHasState) { Post(new { cmd = "seek", ms }); return Task.CompletedTask; }
        return api.SendAsync(HttpMethod.Put, $"me/player/seek?position_ms={ms}" + DeviceQuery('&'));
    }

    public Task SetVolumeAsync(double percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        if (IsTargetSonora) { Post(new { cmd = "volume", v = percent / 100.0 }); return Task.CompletedTask; }
        return api.SendAsync(HttpMethod.Put, $"me/player/volume?volume_percent={(int)percent}" + DeviceQuery('&'));
    }

    public Task SetShuffleAsync(bool on) =>
        api.SendAsync(HttpMethod.Put, $"me/player/shuffle?state={(on ? "true" : "false")}" + DeviceQuery('&'));

    public Task SetRepeatAsync(RepeatMode mode) =>
        api.SendAsync(HttpMethod.Put, $"me/player/repeat?state={mode switch { RepeatMode.All => "context", RepeatMode.One => "track", _ => "off" }}" + DeviceQuery('&'));

    public Task EnqueueAsync(string uri) =>
        api.SendAsync(HttpMethod.Post, $"me/player/queue?uri={Uri.EscapeDataString(uri)}" + DeviceQuery('&'));

    public async Task<List<Track>> GetQueueAsync()
    {
        var q = await api.GetAsync("me/player/queue");
        var result = new List<Track>();
        if (q.ValueKind == JsonValueKind.Object && q.TryGetProperty("queue", out var arr))
            foreach (var t in arr.EnumerateArray())
                if (catalog.MapTrack(t) is { } track) result.Add(track);
        return result;
    }

    public async Task<List<SpotifyDevice>> GetDevicesAsync()
    {
        var d = await api.GetAsync("me/player/devices");
        var list = new List<SpotifyDevice>();
        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("devices", out var arr))
            foreach (var x in arr.EnumerateArray())
                if (Str(x, "id") is { } id)
                    list.Add(new SpotifyDevice(id, Str(x, "name") ?? id, Str(x, "type") ?? "",
                        x.TryGetProperty("is_active", out var a) && a.GetBoolean()));
        return list;
    }

    public async Task TransferAsync(string deviceId, bool play)
    {
        if (deviceId == DeviceId) Post(new { cmd = "activate" });
        await api.SendAsync(HttpMethod.Put, "me/player", new { device_ids = new[] { deviceId }, play });
        TargetDeviceId = deviceId;
        if (deviceId == DeviceId) TargetDeviceName = "Sonora";
    }

    public void Dispose()
    {
        _poll?.Stop();
        Post(new { cmd = "disconnect" });
        _controller?.Close();
        _controller = null;
        _web = null;
    }

    // ---------------- page hosting the official SDK ----------------

    const string PlayerHtml = """
        <!doctype html>
        <html><head><meta charset="utf-8"><title>Sonora player</title></head>
        <body>
        <script>
        const post = m => window.chrome.webview.postMessage(m);
        const tokenWaiters = [];
        window.chrome.webview.addEventListener('message', e => {
          const m = e.data;
          if (m.cmd === 'token') { while (tokenWaiters.length) tokenWaiters.shift()(m.token); return; }
          const p = window.sonoraPlayer;
          if (!p) return;
          switch (m.cmd) {
            case 'activate': p.activateElement && p.activateElement(); break;
            case 'pause': p.pause(); break;
            case 'resume': p.resume(); break;
            case 'toggle': p.togglePlay(); break;
            case 'next': p.nextTrack(); break;
            case 'prev': p.previousTrack(); break;
            case 'seek': p.seek(m.ms); break;
            case 'volume': p.setVolume(m.v); break;
            case 'disconnect': p.disconnect(); break;
          }
        });

        (async () => {
          try {
            await navigator.requestMediaKeySystemAccess('com.widevine.alpha', [{
              initDataTypes: ['cenc'],
              audioCapabilities: [{ contentType: 'audio/mp4; codecs="mp4a.40.2"' }]
            }]);
            post({ type: 'eme', ok: true });
          } catch (err) {
            post({ type: 'eme', ok: false, message: String(err) });
          }
        })();

        window.onSpotifyWebPlaybackSDKReady = () => {
          post({ type: 'sdk' });
          const player = window.sonoraPlayer = new Spotify.Player({
            name: 'Sonora',
            volume: 0.7,
            getOAuthToken: cb => { tokenWaiters.push(cb); post({ type: 'token' }); }
          });
          player.addListener('ready', ({ device_id }) => post({ type: 'ready', device_id }));
          player.addListener('not_ready', ({ device_id }) => post({ type: 'not_ready', device_id }));
          player.addListener('player_state_changed', state => post({ type: 'state', state }));
          ['initialization_error', 'authentication_error', 'account_error', 'playback_error']
            .forEach(kind => player.addListener(kind, ({ message }) => post({ type: 'error', kind, message })));
          player.connect();
        };
        </script>
        <script src="https://sdk.scdn.co/spotify-player.js"></script>
        </body></html>
        """;
}
