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

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Services.CrashLog.Write("Unhandled exception", ex);
        };

        try
        {
            Views.Motion.MatchDisplayRefresh(); // before any animation is created, the app's resources included
            var app = new App();
            app.DispatcherUnhandledException += (_, e) => Services.CrashLog.Write("Error on the interface thread", e.Exception); // still ends the app, as before
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            // The window failed to open (a broken resource, for instance): say why instead of vanishing.
            Services.CrashLog.Fatal("Startup failed", ex);
            Environment.ExitCode = 1;
        }
    }
}
