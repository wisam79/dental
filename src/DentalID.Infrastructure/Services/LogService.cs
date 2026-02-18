using System.Text.Json;
using DentalID.Core.Interfaces;

namespace DentalID.Infrastructure.Services;

public class LogService : ILoggerService
{
    private readonly string _logDirectory;
    private readonly string _auditFile;
    private readonly string _appLogFile;
    private readonly object _auditLock = new();
    private readonly object _logLock = new();

    public LogService()
    {
        _logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(_logDirectory);
        _auditFile = Path.Combine(_logDirectory, "audit_trail.jsonl");
        _appLogFile = Path.Combine(_logDirectory, "application.log");
    }

    public void LogInformation(string message)
    {
        WriteLog("INFO", message);
    }

    public void LogWarning(string message)
    {
        WriteLog("WARN", message);
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"[WARN] {message}");
        Console.ResetColor();
    }

    public void LogError(Exception ex, string message)
    {
        // Do not log full stack trace to avoid exposing sensitive information
        WriteLog("ERROR", $"{message} | Ex: {ex.Message}");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"[ERROR] {message}");
        Console.ResetColor();
    }

    public void LogAudit(string action, string user, string details, string hash = "N/A")
    {
        var entry = new
        {
            Timestamp = DateTime.UtcNow,
            Type = "AUDIT",
            Action = action,
            User = user,
            Details = details,
            IntegrityHash = hash
        };

        var json = JsonSerializer.Serialize(entry);
        
        lock (_auditLock)
        {
            File.AppendAllLines(_auditFile, new[] { json });
        }
    }

    private void WriteLog(string level, string message)
    {
        var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {message}";
        
        lock (_logLock)
        {
            File.AppendAllLines(_appLogFile, new[] { line });
        }
    }
}

