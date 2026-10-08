using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Bluzic;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            LogCrash(args.ExceptionObject as Exception);
        };

        DispatcherUnhandledException += (s, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = true;
        };

        base.OnStartup(e);
    }

    private void LogCrash(Exception? ex)
    {
        if (ex == null) return;
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logPath = Path.Combine(appData, "Bluzic", "crash.log");
            File.WriteAllText(logPath, $"{DateTime.Now}: {ex}\nInner: {ex.InnerException}");
        }
        catch
        {
        }
    }
}
