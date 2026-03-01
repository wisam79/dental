using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DentalID.Core.DTOs;
using SkiaSharp;
namespace DentalID.Application.Interfaces;

/// <summary>
/// Responsible for SAM-encoder feature extraction and InsightFace age/gender estimation.
/// All methods assume the caller holds <see cref="IOnnxSessionManager.InferenceLock"/>.
/// </summary>
public interface IFeatureEncoderService
{
    /// <summary>
    /// Runs the SAM encoder and mean-pools the spatial output [1,256,64,64] → float[256].
    /// </summary>
    (float[]? vector, string? error) ExtractFeatures(SKBitmap bitmap);

    /// <summary>
    /// Computes age mathematically using DentalAgeEstimator and returns gender as Unknown.
    /// InsightFace facial recognition was removed for X-Ray incompatibility.
    /// </summary>
    Task<(string Gender, string AgeRange, int? MedianAge, Exception? Error)> EstimateGenderAgeAsync(string imagePath, IEnumerable<DetectedTooth> detections);
}
