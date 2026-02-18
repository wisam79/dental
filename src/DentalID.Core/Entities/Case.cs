using System.ComponentModel.DataAnnotations;
using DentalID.Core.Enums;

namespace DentalID.Core.Entities;

/// <summary>
/// Represents a forensic case for dental identification.
/// </summary>
public class Case : AuditableEntity
{
    // Id, CreatedAt, UpdatedAt inherited
    
    [Required]
    public string CaseNumber { get; set; } = string.Empty;

    [Required]
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CaseType { get; set; }
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public CasePriority Priority { get; set; } = CasePriority.Normal;
    public int? AssignedToId { get; set; }
    public string? ReportedBy { get; set; }
    public DateTime? IncidentDate { get; set; }
    public string? Location { get; set; }
    public int EvidenceCount { get; set; }
    public string? Result { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int? CreatedById { get; set; }

    // Navigation properties
    public User? AssignedTo { get; set; }
    public User? CreatedBy { get; set; }
    public ICollection<Match> Matches { get; set; } = new List<Match>();
}
