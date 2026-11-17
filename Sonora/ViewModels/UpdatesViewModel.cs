using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using Sonora.Appearance.Features;
using Sonora.Core;
using Velopack;
using Velopack.Sources;

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
    }

    public ICommand CheckCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand OpenReleasesCommand { get; }

    /// <summary>True when Sonora was installed by its installer, the only case where it can replace itself.</summary>
    public bool IsInstalled => _manager.IsInstalled;
    public bool IsNotInstalled => !IsInstalled;

    /// <summary>The version running: the installed package's, or the assembly's for a copy that was not installed.</summary>
    public string CurrentVersion =>
        _manager.CurrentVersion?.ToString()
        ?? Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? "?";

    public string VersionLine => $"Sonora {CurrentVersion}";

    string _status = "";
    /// <summary>What happened last, in a sentence (empty until something has).</summary>
    public string Status { get => _status; private set { if (Set(ref _status, value)) OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => _status.Length > 0;

    bool _isBusy;
    public bool IsBusy { get => _isBusy; private set { if (Set(ref _isBusy, value)) CommandManager.InvalidateRequerySuggested(); } }

    bool _hasUpdate;
    public bool HasUpdate { get => _hasUpdate; private set { if (Set(ref _hasUpdate, value)) CommandManager.InvalidateRequerySuggested(); } }

    string _updateVersion = "";
    public string UpdateVersion { get => _updateVersion; private set { if (Set(ref _updateVersion, value)) OnPropertyChanged(nameof(InstallLabel)); } }

    public string InstallLabel => $"Installer la version {UpdateVersion}";

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
                Status = "Cette copie n'a pas été installée avec l'installeur de Sonora, elle ne peut pas se mettre à jour toute seule. "
                         + "Téléchargez la dernière version sur la page des versions.";
                return;
            }

            if (manual) Status = "Recherche d'une mise à jour…";
            var info = await _manager.CheckForUpdatesAsync();
            _main.State.Features.LastUpdateCheckUtc = DateTime.UtcNow;
            _main.ScheduleSave();

            if (info == null)
            {
                _pending = null;
                HasUpdate = false;
                Status = $"Sonora est à jour ({CurrentVersion}).";
                return;
            }

            _pending = info;
            UpdateVersion = info.TargetFullRelease.Version.ToString();
            HasUpdate = true;
            Status = $"La version {UpdateVersion} est disponible.";
            // On opening, a card says so once; the install itself is always a deliberate click in the settings.
            if (!manual)
                _main.ReportError($"Sonora {UpdateVersion} est disponible. Vous pouvez l'installer depuis Paramètres › Mises à jour.");
        }
        catch (Exception ex)
        {
            // Offline, GitHub unreachable, a release without packages: nothing to nag about on opening.
            if (manual) Status = "Impossible de vérifier les mises à jour : " + ex.Message;
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
        try
        {
            IsDownloading = true;
            Progress = 0;
            Status = $"Téléchargement de la version {UpdateVersion}…";
            await _manager.DownloadUpdatesAsync(_pending, p => Application.Current.Dispatcher.Invoke(() => Progress = p));

            Status = "Installation… Sonora va redémarrer (la lecture s'interrompt le temps du redémarrage).";
            // The updater replaces the files once this process has ended, then starts the new version.
            _manager.WaitExitThenApplyUpdates(_pending.TargetFullRelease, silent: false, restart: true);
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            Status = "La mise à jour a échoué : " + ex.Message;
            IsDownloading = false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
