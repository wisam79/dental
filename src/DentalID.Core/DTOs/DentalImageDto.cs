using DentalID.Core.Enums;

namespace DentalID.Core.DTOs;

/// <summary>
/// Data Transfer Object for DentalImage entity
/// </summary>
public class DentalImageDto
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public ImageType ImageType { get; set; } = ImageType.Panoramic;
    public JawType? JawType { get; set; }
    public string? Quadrant { get; set; }
    public DateTime? CaptureDate { get; set; }
    public double? QualityScore { get; set; }
    public string? AnalysisResults { get; set; }
    public string? FingerprintCode { get; set; }
    public DateTime UploadedAt { get; set; }
    public double UniquenessScore { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? DigitalSeal { get; set; }
}

/// <summary>
/// DTO for creating a new DentalImage
/// </summary>
public class CreateDentalImageDto
{
    public int SubjectId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public ImageType ImageType { get; set; } = ImageType.Panoramic;
    public JawType? JawType { get; set; }
    public string? Quadrant { get; set; }
    public DateTime? CaptureDate { get; set; }
    public double? QualityScore { get; set; }
    public string? AnalysisResults { get; set; }
    public string? FingerprintCode { get; set; }
    public double UniquenessScore { get; set; }
    public bool IsProcessed { get; set; }
}

/// <summary>
/// DTO for updating an existing DentalImage
/// </summary>
public class UpdateDentalImageDto
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public string? FileHash { get; set; }
    public ImageType ImageType { get; set; } = ImageType.Panoramic;
    public JawType? JawType { get; set; }
    public string? Quadrant { get; set; }
    public DateTime? CaptureDate { get; set; }
    public double? QualityScore { get; set; }
    public string? AnalysisResults { get; set; }
    public string? FingerprintCode { get; set; }
    public double UniquenessScore { get; set; }
    public bool IsProcessed { get; set; }
}

/// <summary>
/// DTO for dental image search results
/// </summary>
public class DentalImageSearchDto
{
    public int Id { get; set; }
    public int SubjectId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    public ImageType ImageType { get; set; } = ImageType.Panoramic;
    public JawType? JawType { get; set; }
    public DateTime? CaptureDate { get; set; }
    public double? QualityScore { get; set; }
    public bool IsProcessed { get; set; }
    public DateTime UploadedAt { get; set; }
}
