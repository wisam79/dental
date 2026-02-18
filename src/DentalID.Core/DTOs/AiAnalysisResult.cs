using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DentalID.Core.DTOs;

/// <summary>
/// Analysis result from the AI pipeline for a single dental image.
/// </summary>
public class AnalysisResult
{
    private List<DetectedTooth> _teeth = new();
    public List<DetectedTooth> Teeth 
    { 
        get => _teeth; 
        set 
        {
            _teeth = value ?? new List<DetectedTooth>();
            if (RawTeeth.Count == 0)
                RawTeeth = new List<DetectedTooth>(_teeth);
        }
    }
    
    private List<DetectedPathology> _pathologies = new();
    public List<DetectedPathology> Pathologies 
    { 
        get => _pathologies; 
        set 
        {
            _pathologies = value ?? new List<DetectedPathology>();
            if (RawPathologies.Count == 0)
                RawPathologies = new List<DetectedPathology>(_pathologies);
        }
    }
    
    /// <summary>Original detections before sensitivity filtering.</summary>
    [JsonIgnore] public List<DetectedTooth> RawTeeth { get; set; } = new();
    /// <summary>Original pathologies before sensitivity filtering.</summary>
    [JsonIgnore] public List<DetectedPathology> RawPathologies { get; set; } = new();

    public int? EstimatedAge { get; set; }
    public string? EstimatedGender { get; set; }
    public float[]? FeatureVector { get; set; }
    public DentalFingerprint? Fingerprint { get; set; }
    public double ProcessingTimeMs { get; set; }
    public string? Error { get; set; }
    public bool IsSuccess => Error == null;

    /// <summary>
    /// Forensic flags indicating potential anomalies, conflicts, or deepfake suspicions.
    /// </summary>
    public List<string> Flags { get; set; } = new();

    /// <summary>
    /// AI-derived insights (e.g., Dentition Type, Occlusion, Symmetry).
    /// </summary>
    public List<string> SmartInsights { get; set; } = new();
}

/// <summary>
/// Represents a detected tooth with FDI numbering.
/// </summary>
public class DetectedTooth
{
    public int FdiNumber { get; set; }
    public float Confidence { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}

/// <summary>
/// Represents a detected dental pathology.
/// </summary>
public class DetectedPathology
{
    public string ClassName { get; set; } = string.Empty;
    public float Confidence { get; set; }
    public int? ToothNumber { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}
