using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Sonora.Appearance.Features;
using Sonora.Core;
using Velopack;
using Velopack.Sources;
using Sonora.Appearance.Localization;

namespace Sonora.ViewModels;

/// <summary>
/// Looks for a new version of Sonora on GitHub and installs it. Sonora is installed by Velopack's Setup.exe, which
/// also provides the updater: the new version is downloaded from the GitHub release, then Sonora closes, is
/// replaced and starts again. A copy that was not installed that way (a zip, a build from source) can still look
/// for a version but sends you to the releases page instead of replacing itself.
/// </summary>
public sealed class UpdatesViewModel : ObservableObject
{
    public const string RepositoryUrl = "https://github.com/Tonoplas909/sonora-app";
    const string ReleasesUrl = RepositoryUrl + "/releases/latest";

    readonly MainViewModel _main;
    readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    UpdateInfo? _pending;

    public UpdatesViewModel(MainViewModel main)
    {
        _main = main;
        CheckCommand = new RelayCommand(() => _ = CheckAsync(manual: true), () => !IsBusy);
        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => HasUpdate && !IsBusy);
        OpenReleasesCommand = new RelayCommand(() => Process.Start(new ProcessStartInfo(ReleasesUrl) { UseShellExecute = true }));
        DismissPromptCommand = new RelayCommand(() => { _promptDismissed = true; OnPropertyChanged(nameof(ShowPrompt)); });
    }

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand OpenReleasesCommand { get; }
    public ICommand DismissPromptCommand { get; }

    /// <summary>True when Sonora was installed by its installer, the only case where it can replace itself.</summary>
    public bool IsInstalled => _manager.IsInstalled;
    public bool IsNotInstalled => !IsInstalled;

    /// <summary>The version running: the installed package's, or the assembly's for a copy that was not installed.</summary>
    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "?";

    public string VersionLine => $"Sonora {CurrentVersion}";

    // The status is kept as a key and its arguments, so it is written again when the language changes.
    string? _statusKey;
    object[] _statusArgs = [];

    /// <summary>What happened last, in a sentence (empty until something has).</summary>
    public string Status => _statusKey != null ? Loc.T(_statusKey, _statusArgs) : "";
    public bool HasStatus => _statusKey != null;

    void SetStatus(string key, params object[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(HasStatus));
        OnPropertyChanged(nameof(PromptText));
    }

    /// <summary>Called when the language changes: what is shown is written again.</summary>
    public void RefreshLanguage()
    {
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(InstallLabel));
        OnPropertyChanged(nameof(PromptText));
    }

    bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsPromptIdle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    bool _hasUpdate;
    public bool HasUpdate
    {
        get => _hasUpdate;
        private set
        {
            if (!Set(ref _hasUpdate, value)) return;
            OnPropertyChanged(nameof(ShowPrompt));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    // ---------------- the offer shown over the window ----------------

    bool _promptDismissed;
    bool _installStarted;

    /// <summary>A new version is known: a card offers to install it, wherever the user is. "Later" hides it until Sonora is opened again.</summary>
    public bool ShowPrompt => HasUpdate && !_promptDismissed;

    /// <summary>The card's buttons are shown unless the update is being downloaded or installed.</summary>
    public bool IsPromptIdle => !IsBusy;

    /// <summary>"Sonora 1.2.0 is available", then what the install is doing (or why it failed).</summary>
    public string PromptText => _installStarted ? Status : Loc.T("update.prompt", UpdateVersion);

    string _updateVersion = "";
    public string UpdateVersion
    {
        get => _updateVersion;
        private set
        {
            if (!Set(ref _updateVersion, value)) return;
            OnPropertyChanged(nameof(InstallLabel));
            OnPropertyChanged(nameof(PromptText));
        }
    }

    public string InstallLabel => Loc.T("update.install_version", UpdateVersion);

    bool _isDownloading;
    public bool IsDownloading { get => _isDownloading; private set => Set(ref _isDownloading, value); }

    int _progress;
    public int Progress { get => _progress; private set => Set(ref _progress, value); }

    // ---------------- checking ----------------

    /// <summary>The check done when Sonora opens, if it is switched on and the last one is not too recent. Never reports a failure.</summary>
    public async Task CheckAtStartupAsync()
    {
        var features = _main.State.Features;
        if (!UpdateSchedule.ShouldCheckAtStartup(features.UpdateCheck, features.LastUpdateCheckUtc, DateTime.UtcNow)) return;
        if (!IsInstalled) return; // a copy from source or a zip would only be told to go and look: no need to ask GitHub unprompted
        await CheckAsync(manual: false);
    }

    /// <summary>Asks GitHub for the latest release. <paramref name="manual"/> is the button: it always says what it found.</summary>
    async Task CheckAsync(bool manual)
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            if (!IsInstalled)
            {
                SetStatus("update.not_installed");
                return;
            }

            if (manual) SetStatus("update.checking");
            var info = await _manager.CheckForUpdatesAsync();
            _main.State.Features.LastUpdateCheckUtc = DateTime.UtcNow;
            _main.ScheduleSave();

            if (info == null)
            {
                _pending = null;
                HasUpdate = false;
                SetStatus("update.up_to_date", CurrentVersion);
                return;
            }

            _pending = info;
            UpdateVersion = info.TargetFullRelease.Version.ToString();
            HasUpdate = true;
            SetStatus("update.available", UpdateVersion);
        }
        catch (Exception ex)
        {
            // Offline, GitHub unreachable, a release without packages: nothing to nag about on opening.
            if (manual) SetStatus("update.check_failed", ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------- installing ----------------

    async Task InstallAsync()
    {
        if (_pending == null || IsBusy) return;
        IsBusy = true;
        _installStarted = true; // the card now reports the install instead of the offer
        try
        {
            IsDownloading = true;
            Progress = 0;
            SetStatus("update.downloading", UpdateVersion);
            await _manager.DownloadUpdatesAsync(_pending, p => Application.Current.Dispatcher.Invoke(() => Progress = p));

            SetStatus("update.installing");
            // The updater replaces the files once this process has ended, then starts the new version.
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: false, restart: true);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            SetStatus("update.failed", ex.Message);
            IsDownloading = false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
