using DentalID.Core.DTOs;
using SkiaSharp;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Responsible for pathology detection inference.
/// All methods assume the caller holds <see cref="IOnnxSessionManager.InferenceLock"/>.
/// </summary>
public interface IPathologyDetectionService
{
    /// <summary>Run pathology detector on <paramref name="bitmap"/>.</summary>
    List<DetectedPathology> DetectPathologies(SKBitmap bitmap);
}
