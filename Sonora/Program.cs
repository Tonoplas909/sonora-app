using Velopack;

namespace Sonora;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // Velopack's installer, updater and uninstaller start Sonora with special arguments (--veloapp-install and
        // so on) and expect it to do its small job and exit at once. This has to run before anything else; for a
        // normal start it does nothing and returns.
        VelopackApp.Build().Run();

        Views.Motion.MatchDisplayRefresh(); // before any animation is created, the app's resources included
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
