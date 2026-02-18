namespace DentalID.Core.DTOs;

/// <summary>
/// Data Transfer Object for Subject entity
/// </summary>
public class SubjectDto
{
    public int Id { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public string? ContactInfo { get; set; }
    public string? Notes { get; set; }
    public byte[]? FeatureVector { get; set; }
    public string? ThumbnailPath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int? CreatedById { get; set; }
    public List<DentalImageDto> DentalImages { get; set; } = new();
}

/// <summary>
/// DTO for creating a new Subject
/// </summary>
public class CreateSubjectDto
{
    public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public string? ContactInfo { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for updating an existing Subject
/// </summary>
public class UpdateSubjectDto
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public string? ContactInfo { get; set; }
    public string? Notes { get; set; }
}

/// <summary>
/// DTO for subject search results
/// </summary>
public class SubjectSearchDto
{
    public int Id { get; set; }
    public string SubjectId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? NationalId { get; set; }
    public int DentalImageCount { get; set; }
    public DateTime CreatedAt { get; set; }
}
