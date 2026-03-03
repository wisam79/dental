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
    public float DefaultThreshold { get; set; } = 0.45f;

    /// <summary>
    /// Confidence threshold for teeth detection.
    /// Balanced for panoramic X-rays to reduce false positives.
    /// </summary>
    public float TeethThreshold { get; set; } = 0.50f;

    /// <summary>
    /// Minimum cosine similarity score to consider a subject a match.
    /// </summary>
    public float MatchSimilarityThreshold { get; set; } = 0.50f;

    /// <summary>
    /// Baseline cosine score considered "background similarity" for encoder vectors.
    /// Scores below this are mapped to zero after calibration.
    /// </summary>
    public float MatchCalibrationFloor { get; set; } = 0.78f;

    /// <summary>
    /// Non-linear calibration factor for vector similarity.
    /// Higher values make near-floor scores decay faster.
    /// </summary>
    public float MatchCalibrationGamma { get; set; } = 1.8f;

    /// <summary>
    /// IoU threshold for Non-Maximum Suppression (NMS).
    /// </summary>
    public float NmsIoUThreshold { get; set; } = 0.55f;

    /// <summary>
    /// Proximity threshold for mapping pathologies to teeth.
    /// </summary>
    public float ProximityThreshold { get; set; } = 0.10f;

    /// <summary>
    /// Class-specific thresholds for pathology detection.
    /// </summary>
    public Dictionary<string, float> PathologyThresholds { get; set; } = new()
    {
        { "Caries", 0.35f },
        { "Crown", 0.55f },
        { "Filling", 0.50f },
        { "Implant", 0.65f },
        { "Missing teeth", 0.55f },
        { "Periapical lesion", 0.35f },
        { "Root Piece", 0.45f },
        { "Root canal obturation", 0.55f }
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
    /// Standard permanent dentition ordering:
    /// 11..18, 21..28, 31..38, 41..48
    /// </summary>
    public int[] ClassMap { get; set; } = 
    {
        11, 12, 13, 14, 15, 16, 17, 18,
        21, 22, 23, 24, 25, 26, 27, 28,
        31, 32, 33, 34, 35, 36, 37, 38,
        41, 42, 43, 44, 45, 46, 47, 48
    };
}
