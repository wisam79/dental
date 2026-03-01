using DentalID.Core.DTOs;
using SkiaSharp;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Responsible for all teeth-detection inference logic:
/// standard detection, TTA augmentation, edge-crop rescue, and NMS post-processing.
/// All methods assume the caller holds <see cref="IOnnxSessionManager.InferenceLock"/>.
/// </summary>
public interface ITeethDetectionService
{
    /// <summary>Run teeth detector on <paramref name="bitmap"/> using the main detection buffer.</summary>
    List<DetectedTooth> DetectTeeth(SKBitmap bitmap);

    /// <summary>Run teeth detector with a custom <paramref name="confidenceThreshold"/> (used by edge rescue).</summary>
    List<DetectedTooth> DetectTeethOnBitmap(SKBitmap bitmap, float confidenceThreshold);

    /// <summary>Run both teeth and pathology models on <paramref name="bitmap"/> (used by TTA).</summary>
    (List<DetectedTooth> Teeth, List<DetectedPathology> Pathologies) DetectObjects(SKBitmap bitmap);

    /// <summary>Apply horizontal-flip Test-Time Augmentation and merge results into <paramref name="result"/>.</summary>
    void ApplyTta(AnalysisResult result, SKBitmap bitmap);

    /// <summary>Whether the panoramic image warrants an edge-crop recovery pass.</summary>
    bool ShouldApplyEdgeCropRescue(SKBitmap bitmap, List<DetectedTooth> teeth);

    /// <summary>Perform edge-crop rescue: re-run detector on left/right crops and merge into <paramref name="result"/>.</summary>
    void ApplyEdgeCropRescue(AnalysisResult result, SKBitmap bitmap);

    /// <summary>Apply NMS, FDI deduplication, and limit to 32 teeth.</summary>
    List<DetectedTooth> BuildFinalTeeth(List<DetectedTooth> candidates);
}
