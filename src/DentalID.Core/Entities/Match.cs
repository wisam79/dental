namespace DentalID.Core.Entities;

/// <summary>
/// Represents a match result between a query dental image and a stored subject.
/// </summary>
public class Match : AuditableEntity
{
    // Id, CreatedAt, UpdatedAt inherited
    public int? CaseId { get; set; }
    public int QueryImageId { get; set; }
    public int MatchedSubjectId { get; set; }
    public int? MatchedImageId { get; set; }
    public double ConfidenceScore { get; set; }
    public string? MatchMethod { get; set; }
    public string? ResultType { get; set; }
    public string? AlgorithmVersion { get; set; }
    public double? FeatureSimilarity { get; set; }
    public bool IsConfirmed { get; set; }
    public int? ConfirmedById { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    public string? Notes { get; set; }

    // Navigation properties
    public Case? Case { get; set; }
    public DentalImage QueryImage { get; set; } = null!;
    public Subject MatchedSubject { get; set; } = null!;
    public DentalImage? MatchedImage { get; set; }
    public User? ConfirmedBy { get; set; }
}
