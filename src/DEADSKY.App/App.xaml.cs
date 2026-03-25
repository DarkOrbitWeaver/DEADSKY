using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;

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
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        LogCrash("APPDOMAIN", e.ExceptionObject as Exception, $"IsTerminating={e.IsTerminating}");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash("DISPATCHER", e.Exception);
        e.Handled = true;
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
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
