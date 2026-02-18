namespace DentalID.Core.Interfaces;

/// <summary>
/// Analysis result from the AI pipeline for a single dental image.
/// </summary>
using DentalID.Core.DTOs;

/// <summary>
/// Service for running AI analysis on dental images.
/// </summary>
public interface IAiPipelineService : IDisposable
{
    /// <summary>Whether all required ONNX models are loaded.</summary>
    bool IsReady { get; }

    /// <summary>Loads all ONNX model files from the specified directory.</summary>
    Task InitializeAsync(string modelsDirectory);

    /// <summary>Runs the full analysis pipeline on an image stream.</summary>
    Task<AnalysisResult> AnalyzeImageAsync(Stream imageStream, string? fileName = null);

    /// <summary>Runs only teeth detection.</summary>
    Task<List<DetectedTooth>> DetectTeethAsync(Stream imageStream);

    /// <summary>Runs only pathology detection.</summary>
    Task<List<DetectedPathology>> DetectPathologiesAsync(Stream imageStream);

    /// <summary>Extracts feature vector for matching.</summary>
    Task<(float[]? vector, string? error)> ExtractFeaturesAsync(Stream imageStream);
}