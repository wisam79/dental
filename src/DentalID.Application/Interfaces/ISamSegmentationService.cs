using DentalID.Core.DTOs;
using SkiaSharp;
using System.Collections.Generic;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Service responsible for performing Zero-Shot Instance Segmentation using SAM (Segment Anything Model).
/// </summary>
public interface ISamSegmentationService
{
    /// <summary>
    /// Segments teeth using their bounding boxes as prompts.
    /// Modifies the input detections in-place to add polygon outlines.
    /// </summary>
    void SegmentTeeth(SKBitmap bitmap, IEnumerable<DetectedTooth> teeth);

    /// <summary>
    /// Segments pathologies using their bounding boxes as prompts.
    /// Modifies the input detections in-place to add polygon outlines.
    /// </summary>
    void SegmentPathologies(SKBitmap bitmap, IEnumerable<DetectedPathology> pathologies);
}
