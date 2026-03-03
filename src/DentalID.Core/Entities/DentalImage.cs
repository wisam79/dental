using DentalID.Core.Enums;

namespace DentalID.Core.Entities;

/// <summary>
/// Represents a dental X-ray or photograph associated with a subject.
/// </summary>
public class DentalImage : AuditableEntity
{
    // Id, CreatedAt inherited
    public int SubjectId { get; set; }
    public string ImagePath { get; set; } = string.Empty;
    /// <summary>SHA-256 hash of the original image file for integrity verification.</summary>
    public string? FileHash { get; set; }
    public ImageType ImageType { get; set; } = ImageType.Panoramic;
    public JawType? JawType { get; set; }
    public string? Quadrant { get; set; }
    public DateTime? CaptureDate { get; set; }
    public double? QualityScore { get; set; }
    private string? _analysisResults;
    /// <summary>JSON-serialized analysis results from AI models.</summary>
    public string? AnalysisResults 
    { 
        get => _analysisResults; 
        set 
        { 
            if (_analysisResults != value) 
            {
                _analysisResults = value;
                _parsedAnalysisResults = null; // Invalidate cache when raw JSON string is updated
            }
        } 
    }
    private DentalID.Core.DTOs.AnalysisResult? _parsedAnalysisResults;

    /// <summary>
    /// Strongly-typed wrapper for AnalysisResults JSON.
    /// Not mapped to database directly; uses AnalysisResults backing field.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public DentalID.Core.DTOs.AnalysisResult? ParsedAnalysisResults
    {
        get
        {
            if (_parsedAnalysisResults != null) return _parsedAnalysisResults;
            if (string.IsNullOrEmpty(AnalysisResults)) return null;
            try 
            {
                _parsedAnalysisResults = System.Text.Json.JsonSerializer.Deserialize<DentalID.Core.DTOs.AnalysisResult>(AnalysisResults);
                return _parsedAnalysisResults;
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException("Evidence corruption detected: Analysis results JSON is malformed.", ex);
            }
        }
        set
        {
            _parsedAnalysisResults = value;
            if (value == null) AnalysisResults = null;
            else AnalysisResults = System.Text.Json.JsonSerializer.Serialize(value);
        }
    }
    
    // Bug #4 fix: Remove duplicate <summary> XML comment
    /// <summary>Generated Dental DNA code (Biometric Fingerprint).</summary>
    public string? FingerprintCode { get; set; }
    
    // Bug #6 fix: Default to UtcNow at construction time (nearest to actual upload);
    // callers in SaveEvidenceAsync should set this explicitly via UploadedAt = DateTime.UtcNow
    // Bug #7 fix: UploadedAt is distinct from CreatedAt (inherited); it records when image was submitted
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>Biometric uniqueness score of the dental map.</summary>
    public double UniquenessScore { get; set; }

    public bool IsProcessed { get; set; }
    // CreatedAt inherited
    /// <summary>Digital seal for evidence integrity verification</summary>
    public string? DigitalSeal { get; set; }

    // Navigation properties
    public Subject Subject { get; set; } = null!;
    public ICollection<Match> QueryMatches { get; set; } = new List<Match>();

    public byte[]? FeatureVectorBlob { get; set; }

    /// <summary>
    /// In-memory feature vector. Populated from AnalysisResults or external storage.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public float[]? FeatureVector { get; set; }
}
