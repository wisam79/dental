namespace DentalID.Core.Entities;

/// <summary>
/// Represents a person (subject) whose dental records are stored in the system.
/// </summary>
public class Subject : AuditableEntity
{
    // Id, CreatedAt, UpdatedAt inherited
    public string SubjectId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public string? ContactInfo { get; set; }
    public string? Notes { get; set; }
    public byte[]? FeatureVector { get; set; }
    public string? ThumbnailPath { get; set; }
    public int? CreatedById { get; set; }
    
    // Navigation properties
    public User? CreatedBy { get; set; }
    public ICollection<DentalImage> DentalImages { get; set; } = new List<DentalImage>();
    public ICollection<Match> Matches { get; set; } = new List<Match>();

    // Bug #2 fix: DentalImages is always initialized to new List<>(), remove erroneous ?. operator
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string LatestDentalCode => DentalImages.OrderByDescending(x => x.UploadedAt).FirstOrDefault()?.FingerprintCode ?? "N/A";
}
