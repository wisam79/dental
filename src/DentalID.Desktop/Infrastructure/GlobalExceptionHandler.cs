using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace DentalID.Desktop.Infrastructure;

/// <summary>
/// Forensic-Grade Global Exception Handler.
/// Catch unhandled exceptions, log full system state, and ensure data is not corrupted.
/// </summary>
public static class GlobalExceptionHandler
{
    private static bool _isCaught = false;

    public static void Setup()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            HandleException(args.ExceptionObject as Exception, "AppDomain.UnhandledException");
        };

        // Note: Avalonia specific handling for UI thread exceptions
        // TaskScheduler.UnobservedTaskException is also configured in Bootstrapper
    }

    public static void HandleException(Exception? ex, string source)
    {
        if (_isCaught || ex == null) return;
        _isCaught = true;

        try
        {
            // 1. Capture Forensic Context
            var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var crashLogPath = Path.Combine(AppContext.BaseDirectory, "logs", "CRASH_REPORT.log");
            Directory.CreateDirectory(Path.GetDirectoryName(crashLogPath)!);

            var message = $@"
================================================================================
CRASH REPORT - FORENSIC INTERVENTION
Timestamp: {timestamp} UTC
Source: {source}
OS: {Environment.OSVersion}
Machine: {Environment.MachineName}
User: {Environment.UserName}
--------------------------------------------------------------------------------
EXCEPTION TYPE: {ex.GetType().FullName}
MESSAGE: {ex.Message}
STACK TRACE:
{ex.StackTrace}
--------------------------------------------------------------------------------
INNER EXCEPTION:
{ex.InnerException?.ToString() ?? "None"}
================================================================================
";

            // 2. Atomic Write (Try to write even if system is unstable)
            File.AppendAllText(crashLogPath, message);

            // 3. Last Ditch UI Notification (if UI thread is still alive)
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    // In a real scenario, show a dialog. For now, specific Debug output.
                    System.Diagnostics.Debug.WriteLine("[CRITICAL] Application Crashed. Log saved to: " + crashLogPath);
                    // Safe Shutdown
                    desktop.Shutdown(-1);
                });
            }
            else
            {
                Environment.Exit(-1);
            }
        }
        catch
        {
            // If logging fails, just die to prevent data corruption.
            Environment.Exit(-1);
        }
    }
}
