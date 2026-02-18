using System.Collections.Generic;
using DentalID.Application.Services;

namespace DentalID.Application.Configuration;

/// <summary>
/// Configuration settings for AI models and inference parameters.
/// Loaded from appsettings.json section "AI".
/// </summary>
public class AiConfiguration : IAiConfiguration
{
    public ModelSettings Model { get; set; } = new();
    public ThresholdSettings Thresholds { get; set; } = new();
    public FdiMappingSettings FdiMapping { get; set; } = new();
    
    // LLM/AI Chat settings
    public string? LlmProvider { get; set; }
    public string? LlmApiKey { get; set; }
    public string? LlmModel { get; set; }
    public string? LocalLlmEndpoint { get; set; }
    public bool EnableGpu { get; set; } = false;
    public bool EnableTTA { get; set; } = true; // Test Time Augmentation default ON
    public bool EnableRulesBasedFallback { get; set; } = true;
}

/// <summary>
/// Model input size configurations.
/// </summary>
public class ModelSettings
{
    /// <summary>
    /// Input size for detection models (teeth and pathology detection).
    /// Default: 640x640 pixels (YOLOv8 standard)
    /// </summary>
    public int DetectionInputSize { get; set; } = 640;

    /// <summary>
    /// Input size for the encoder model (feature extraction).
    /// Default: 1024x1024 pixels (SAM standard)
    /// </summary>
    public int EncoderInputSize { get; set; } = 1024;

    /// <summary>
    /// Input size for the age/gender estimation model.
    /// Default: 96x96 pixels
    /// </summary>
    public int GenderAgeInputSize { get; set; } = 96;

    /// <summary>
    /// Ordered list of pathology classes as expected by the pathology detection model.
    /// </summary>
    public string[] PathologyClasses { get; set; } = { "Caries", "Crown", "Filling", "Implant", "Missing teeth", "Periapical lesion", "Root Piece", "Root canal obturation" };
}

/// <summary>
/// Detection threshold configurations.
/// </summary>
public class ThresholdSettings
{
    /// <summary>
    /// Default confidence threshold for detections.
    /// </summary>
    public float DefaultThreshold { get; set; } = 0.35f;

    /// <summary>
    /// Confidence threshold for teeth detection.
    /// Reduced to 0.35 to improve recall on low-contrast images.
    /// </summary>
    public float TeethThreshold { get; set; } = 0.25f;

    /// <summary>
    /// Minimum cosine similarity score to consider a subject a match.
    /// </summary>
    public float MatchSimilarityThreshold { get; set; } = 0.50f;

    /// <summary>
    /// IoU threshold for Non-Maximum Suppression (NMS).
    /// </summary>
    public float NmsIoUThreshold { get; set; } = 0.45f;

    /// <summary>
    /// Proximity threshold for mapping pathologies to teeth.
    /// </summary>
    public float ProximityThreshold { get; set; } = 0.15f;

    /// <summary>
    /// Class-specific thresholds for pathology detection.
    /// </summary>
    public Dictionary<string, float> PathologyThresholds { get; set; } = new()
    {
        { "Caries", 0.25f },
        { "Crown", 0.45f },
        { "Filling", 0.40f },
        { "Implant", 0.55f },
        { "Missing teeth", 0.45f },
        { "Periapical lesion", 0.25f },
        { "Root Piece", 0.35f },
        { "Root canal obturation", 0.45f }
    };

    /// <summary>
    /// Bias values to adjust sensitivity for specific pathologies.
    /// Added to the base threshold calculated from user sensitivity.
    /// </summary>
    public Dictionary<string, double> PathologyBias { get; set; } = new()
    {
        { "Implant", 0.15 },
        { "Crown", 0.10 },
        { "Filling", 0.05 }
    };

    /// <summary>
    /// Base threshold for forensic filtering (High Strictness).
    /// Used in ForensicAnalysisService: Threshold = Base - (Sensitivity * Slope).
    /// </summary>
    public double ForensicBaseThreshold { get; set; } = 0.85;

    /// <summary>
    /// Sensitivity slope determining how much user sensitivity affects the threshold.
    /// </summary>
    public double SensitivitySlope { get; set; } = 0.75;
}

/// <summary>
/// FDI (Fédération Dentaire Internationale) tooth numbering mapping.
/// </summary>
public class FdiMappingSettings
{
    /// <summary>
    /// Maps model output class indices to FDI tooth numbers.
    /// Alphabetically sorted labels from metadata: 
    /// 0, 1, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 2, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 3, 30, 31, 4, 46, 5, 6, 7, 8, 9
    /// </summary>
    public int[] ClassMap { get; set; } = 
    {
        0, 1, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 2, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 3, 30, 31, 4, 46, 5, 6, 7, 8, 9
    };
}
