namespace DentalID.Core.Entities;

/// <summary>
/// Represents an immutable audit log entry for tracking all system actions.
/// </summary>
public class AuditLogEntry : BaseEntity
{
    // Id inherited
    public int? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string? EntityType { get; set; }
    public int? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IPAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "INFO";
    
    // Integrity
    public string? Hash { get; set; }
    public string? PreviousHash { get; set; }

    // Navigation
    public User? User { get; set; }
}
