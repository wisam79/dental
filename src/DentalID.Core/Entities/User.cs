using DentalID.Core.Enums;

namespace DentalID.Core.Entities;

/// <summary>
/// Represents a system user.
/// NOTE: Authentication is intentionally disabled during development.
/// This entity exists for future auth implementation.
/// </summary>
public class User : AuditableEntity
{
    // Id, CreatedAt, UpdatedAt inherited
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Viewer;
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Department { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastLogin { get; set; }
    public bool MustChangePassword { get; set; } = false;

    // Navigation properties
    public ICollection<Subject> CreatedSubjects { get; set; } = new List<Subject>();
    public ICollection<Case> AssignedCases { get; set; } = new List<Case>();
    public ICollection<AuditLogEntry> AuditLogs { get; set; } = new List<AuditLogEntry>();
}

