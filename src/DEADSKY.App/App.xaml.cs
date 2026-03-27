using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using DEADSKY.Core.Logging;

namespace DEADSKY.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    private static readonly string CrashLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DEADSKY",
        "logs",
        "app-crash.log");

    public App()
    {
        // Initialize comprehensive game logging first
        GameLogger.Initialize();
        GameLogger.Info("APP", "DEADSKY application starting...");

        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        GameLogger.Info("APP", "Exception handlers registered");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        GameLogger.Info("APP", $"Application exiting with code {e.ApplicationExitCode}");
        GameLogger.Shutdown();
        base.OnExit(e);
    }

    private static void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        GameLogger.Critical("APP", $"Unhandled AppDomain exception (IsTerminating={e.IsTerminating})", ex);
        LogCrash("APPDOMAIN", ex, $"IsTerminating={e.IsTerminating}");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        GameLogger.Critical("APP", "Unhandled Dispatcher exception", e.Exception);
        LogCrash("DISPATCHER", e.Exception);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        GameLogger.Critical("APP", "Unobserved Task exception", e.Exception);
        LogCrash("TASK", e.Exception);
        e.SetObserved();
    }

    private static void LogCrash(string source, Exception? exception, string? extra = null)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CrashLogPath)!);
            var builder = new StringBuilder()
                .AppendLine("========================================")
                .AppendLine(DateTime.UtcNow.ToString("O"))
                .AppendLine($"SOURCE: {source}");

            if (!string.IsNullOrWhiteSpace(extra))
                builder.AppendLine(extra);

            if (exception != null)
            {
                builder.AppendLine(exception.GetType().FullName ?? "UNKNOWN");
                builder.AppendLine(exception.Message);
                builder.AppendLine(exception.StackTrace ?? "NO STACK");
                if (exception.InnerException != null)
                {
                    builder.AppendLine("-- INNER --");
                    builder.AppendLine(exception.InnerException.ToString());
                }
            }
            else
            {
                builder.AppendLine("NO EXCEPTION PAYLOAD");
            }

            File.AppendAllText(CrashLogPath, builder.ToString());
        }
        catch
        {
            // Avoid secondary failures during crash reporting.
        }
    }
}
