namespace DentalID.Core.DTOs;

/// <summary>
/// Data Transfer Object for Match entity
/// </summary>
public class MatchDto
{
    public int Id { get; set; }
    public int? CaseId { get; set; }
    public int QueryImageId { get; set; }
    public int MatchedSubjectId { get; set; }
    public int? MatchedImageId { get; set; }
    public double ConfidenceScore { get; set; }
    public string? ResultType { get; set; }
    public string? Notes { get; set; }
    public bool IsConfirmed { get; set; }
    public int? ConfirmedById { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// DTO for creating a new Match
/// </summary>
public class CreateMatchDto
{
    public int? CaseId { get; set; }
    public int QueryImageId { get; set; }
    public int MatchedSubjectId { get; set; }
    public int? MatchedImageId { get; set; }
    public double ConfidenceScore { get; set; }
    public string? ResultType { get; set; }
    public string? Notes { get; set; }
    public bool IsConfirmed { get; set; }
    public int? ConfirmedById { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}

/// <summary>
/// DTO for updating an existing Match
/// </summary>
public class UpdateMatchDto
{
    public int Id { get; set; }
    public int? CaseId { get; set; }
    public int QueryImageId { get; set; }
    public int MatchedSubjectId { get; set; }
    public int? MatchedImageId { get; set; }
    public double ConfidenceScore { get; set; }
    public string? ResultType { get; set; }
    public string? Notes { get; set; }
    public bool IsConfirmed { get; set; }
    public int? ConfirmedById { get; set; }
    public DateTime? ConfirmedAt { get; set; }
}

/// <summary>
/// DTO for match search results
/// </summary>
public class MatchSearchDto
{
    public int Id { get; set; }
    public int? CaseId { get; set; }
    public int QueryImageId { get; set; }
    public int MatchedSubjectId { get; set; }
    public double ConfidenceScore { get; set; }
    public string? ResultType { get; set; }
    public bool IsConfirmed { get; set; }
    public DateTime CreatedAt { get; set; }
}
