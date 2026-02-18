using DentalID.Core.Enums;

namespace DentalID.Core.DTOs;

/// <summary>
/// Data Transfer Object for Case entity
/// </summary>
public class CaseDto
{
    public int Id { get; set; }
    public string CaseNumber { get; set; } = string.Empty;
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
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int? CreatedById { get; set; }
    public List<MatchDto> Matches { get; set; } = new();
}

/// <summary>
/// DTO for creating a new Case
/// </summary>
public class CreateCaseDto
{
    public string CaseNumber { get; set; } = string.Empty;
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
}

/// <summary>
/// DTO for updating an existing Case
/// </summary>
public class UpdateCaseDto
{
    public int Id { get; set; }
    public string CaseNumber { get; set; } = string.Empty;
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
}

/// <summary>
/// DTO for case search results
/// </summary>
public class CaseSearchDto
{
    public int Id { get; set; }
    public string CaseNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public CaseStatus Status { get; set; } = CaseStatus.Open;
    public CasePriority Priority { get; set; } = CasePriority.Normal;
    public int EvidenceCount { get; set; }
    public int MatchCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
