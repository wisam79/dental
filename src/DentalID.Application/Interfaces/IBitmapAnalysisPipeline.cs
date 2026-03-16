using DentalID.Core.DTOs;
using SkiaSharp;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Extended pipeline interface for pre-decoded bitmap analysis.
/// Lives in the Application layer (not Core) because it depends on SkiaSharp.
/// </summary>
public interface IBitmapAnalysisPipeline
{
    /// <summary>
    /// Runs the full analysis pipeline on a pre-decoded bitmap.
    /// Eliminates redundant image decoding when the caller already has an SKBitmap.
    /// </summary>
    Task<AnalysisResult> AnalyzeBitmapAsync(SKBitmap bitmap, string? fileName = null, CancellationToken ct = default);
}
