using System.Windows;

namespace GWTP_Windows_POC;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) => DiagnosticLog.Write("DispatcherUnhandledException", args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => DiagnosticLog.Write("UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => DiagnosticLog.Write("UnobservedTaskException", args.Exception);
        DiagnosticLog.Write("RuntimeStartup");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        DiagnosticLog.Write("RuntimeExit");
        base.OnExit(e);
    }
}

internal static class DiagnosticLog
{
    private static readonly object Sync = new();
    private static readonly string PathName = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GWTP", "Logs", "windows-runtime.log");

    public static void Write(string operation, Exception? exception = null)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(PathName);
            if (directory is not null) Directory.CreateDirectory(directory);
            var detail = exception is null ? "" : $" | {exception}";
            var line = $"{DateTimeOffset.Now:O} | pid={Environment.ProcessId} | tid={Environment.CurrentManagedThreadId} | {operation}{detail}";
            lock (Sync) File.AppendAllText(PathName, line + Environment.NewLine);
        }
        catch
        {
            // Diagnostics must never interrupt the runtime.
        }
    }
}
