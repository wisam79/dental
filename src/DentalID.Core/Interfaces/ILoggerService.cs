namespace DentalID.Core.Interfaces;

public interface ILoggerService
{
    void LogInformation(string message);
    void LogWarning(string message);
    void LogError(Exception ex, string message);
    void LogAudit(string action, string user, string details, string hash = "N/A");
}
