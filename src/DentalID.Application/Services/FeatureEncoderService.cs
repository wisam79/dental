using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Core.Interfaces;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using DentalID.Core.Entities; // Added for DentalDetection
using System.Threading.Tasks; // Added for Task
using DentalID.Core.DTOs;

namespace DentalID.Application.Services;

/// <summary>
/// SAM-encoder feature extraction and InsightFace age/gender estimation.
/// Callers must hold <see cref="IOnnxSessionManager.InferenceLock"/> before calling.
/// </summary>
public sealed class FeatureEncoderService : IFeatureEncoderService
{
    private readonly IOnnxSessionManager      _session;
    private readonly ITensorPreparationService _tensorPrep;
    private readonly AiConfiguration          _config;
    private readonly ILoggerService           _logger;

    public FeatureEncoderService(
        IOnnxSessionManager       session,
        ITensorPreparationService tensorPrep,
        AiConfiguration           config,
        ILoggerService            logger)
    {
        _session    = session    ?? throw new ArgumentNullException(nameof(session));
        _tensorPrep = tensorPrep ?? throw new ArgumentNullException(nameof(tensorPrep));
        _config     = config     ?? throw new ArgumentNullException(nameof(config));
        _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Feature extraction: SAM encoder → mean-pool [1,256,64,64] → float[1024 deep features] + float[160 spatial features]
    // ─────────────────────────────────────────────────────────────────────────

    public (float[]? vector, string? error) ExtractFeatures(SKBitmap bitmap, IEnumerable<DetectedTooth>? detections = null)
    {
        if (_session.Encoder == null) return (null, "Encoder model not loaded");
        try
        {
            // Bug #28 Fix: Apply ImageNet normalization as expected by SAM ViT encoder
            var tensor = _tensorPrep.PrepareEncoderTensor(bitmap, 1024, _session.EncoderBuffer, applyNormalization: true);
            var inputs = new List<NamedOnnxValue>
                { NamedOnnxValue.CreateFromTensor(_session.EncoderInputName, tensor) };
            using var results = _session.Encoder.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Bug #27 Fix: validate output rank before indexing dimensions
            if (output.Dimensions.Length < 4)
                return (null, $"Unexpected encoder output rank {output.Dimensions.Length}.");

            int channels, h, w;
            bool isNhwc = false;

            // Detect layout: SAM ViT-B typically has 256 channels.
            // NCHW: [1, 256, 64, 64] | NHWC: [1, 64, 64, 256]
            if (output.Dimensions[3] > output.Dimensions[1] && output.Dimensions[1] < 128)
            {
                isNhwc = true;
                h = output.Dimensions[1];
                w = output.Dimensions[2];
                channels = output.Dimensions[3];
            }
            else
            {
                channels = output.Dimensions[1];
                h = output.Dimensions[2];
                w = output.Dimensions[3];
            }

            int quadH = h / 2;
            int quadW = w / 2;
            int quadSpatialCount = Math.Max(1, quadH * quadW);
            
            // SAM Deep Features: 4 quadrants * channels
            int expectedDeepFeatures = channels * 4;
            int spatialFeatureCount = 160;
            int samDimensionCount = 96;
            int totalFeatures = expectedDeepFeatures + spatialFeatureCount + samDimensionCount; 
            var vector = new float[totalFeatures];

            unsafe
            {
                var denseOutput = output as DenseTensor<float>;
                if (denseOutput == null) 
                    return (null, "Output tensor is not a DenseTensor<float>.");

                fixed (float* pOutput = denseOutput.Buffer.Span)
                fixed (float* pVector = vector)
                {
                    for (int c = 0; c < channels; c++)
                    {
                        float sumTL = 0f, sumTR = 0f, sumBL = 0f, sumBR = 0f;

                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                float val;
                                if (isNhwc)
                                {
                                    // NHWC indexing: (y * W * C) + (x * C) + c
                                    val = *(pOutput + (y * w * channels) + (x * channels) + c);
                                }
                                else
                                {
                                    // NCHW indexing: (c * H * W) + (y * W) + x
                                    val = *(pOutput + (c * h * w) + (y * w) + x);
                                }

                                if (y < quadH)
                                {
                                    if (x < quadW) sumTL += val;
                                    else sumTR += val;
                                }
                                else
                                {
                                    if (x < quadW) sumBL += val;
                                    else sumBR += val;
                                }
                            }
                        }

                        // Store 4 vectors sequentially
                        pVector[c] = sumTL / quadSpatialCount;
                        pVector[channels + c] = sumTR / quadSpatialCount;
                        pVector[(channels * 2) + c] = sumBL / quadSpatialCount;
                        pVector[(channels * 3) + c] = sumBR / quadSpatialCount;
                    }
                }
            }

            // Append Spatial Features + SAM Dimensions
            if (detections != null)
            {
                AppendSpatialGeometry(vector, expectedDeepFeatures, detections);
                AppendSamDimensions(vector, expectedDeepFeatures + spatialFeatureCount, detections);
            }

            return (vector, null);
        }
        catch (Exception ex) 
        { 
            _logger.LogError(ex, "Failed to extract features.");
            return (null, ex.Message); 
        }
    }

    private static void AppendSpatialGeometry(float[] vector, int offset, IEnumerable<DetectedTooth> detections)
    {
        // Standard 32 adult teeth slots
        int[] fdiKeys = {
            18, 17, 16, 15, 14, 13, 12, 11,
            21, 22, 23, 24, 25, 26, 27, 28,
            48, 47, 46, 45, 44, 43, 42, 41,
            31, 32, 33, 34, 35, 36, 37, 38
        };

        var toothDict = detections.Where(t => t.FdiNumber > 0).GroupBy(t => t.FdiNumber).ToDictionary(g => g.Key, g => g.First());

        for (int i = 0; i < fdiKeys.Length; i++)
        {
            int adultFdi = fdiKeys[i];
            int baseIndex = offset + (i * 5);
            
            // Try to find adult tooth first, then fall back to mapped deciduous tooth
            if (!toothDict.TryGetValue(adultFdi, out var tooth))
            {
                int deciduousFdi = MapAdultToDeciduous(adultFdi);
                if (deciduousFdi != 0) toothDict.TryGetValue(deciduousFdi, out tooth);
            }

            if (tooth != null)
            {
                vector[baseIndex + 0] = tooth.Confidence;
                vector[baseIndex + 1] = tooth.X;
                vector[baseIndex + 2] = tooth.Y;
                vector[baseIndex + 3] = tooth.Width;
                vector[baseIndex + 4] = tooth.Height;
            }
            else
            {
                // Missing or undetected tooth - fill with zeros
                vector[baseIndex + 0] = 0f;
                vector[baseIndex + 1] = 0f;
                vector[baseIndex + 2] = 0f;
                vector[baseIndex + 3] = 0f;
                vector[baseIndex + 4] = 0f;
            }
        }
    }

    /// <summary>
    /// Appends SAM segmentation measurements (MaskWidth, MaskHeight, MaskArea) for each of the 32 FDI teeth.
    /// These dimensions encode actual tooth shape/size for highly precise biometric matching.
    /// </summary>
    private static void AppendSamDimensions(float[] vector, int offset, IEnumerable<DetectedTooth> detections)
    {
        int[] fdiKeys = {
            18, 17, 16, 15, 14, 13, 12, 11,
            21, 22, 23, 24, 25, 26, 27, 28,
            48, 47, 46, 45, 44, 43, 42, 41,
            31, 32, 33, 34, 35, 36, 37, 38
        };

        var toothDict = detections.Where(t => t.FdiNumber > 0).GroupBy(t => t.FdiNumber).ToDictionary(g => g.Key, g => g.First());

        for (int i = 0; i < fdiKeys.Length; i++)
        {
            int adultFdi = fdiKeys[i];
            int baseIndex = offset + (i * 3);

            if (!toothDict.TryGetValue(adultFdi, out var tooth))
            {
                int deciduousFdi = MapAdultToDeciduous(adultFdi);
                if (deciduousFdi != 0) toothDict.TryGetValue(deciduousFdi, out tooth);
            }

            if (tooth != null)
            {
                vector[baseIndex + 0] = tooth.MaskWidth;
                vector[baseIndex + 1] = tooth.MaskHeight;
                vector[baseIndex + 2] = tooth.MaskArea;
            }
            // else: zeros by default (undetected tooth)
        }
    }

    private static int MapAdultToDeciduous(int adultFdi)
    {
        // FDI Mapping: Primary Q5 maps to Adult Q1, Primary Q6 -> Adult Q2, etc.
        // Successor mapping: 54 (Primary 1st molar) -> 14 (Adult 1st premolar)
        int quad = adultFdi / 10;
        int unit = adultFdi % 10;
        
        // Deciduous only exist for units 1-5 (Incisiors, Canines, Molars)
        if (unit > 5) return 0;
        
        int pQuad = quad + 4;
        return (pQuad * 10) + unit;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Age/gender: InsightFace model — BGR 0-255, NCHW [-1,3,96,96] → [1,3]
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<(string Gender, string AgeRange, int? MedianAge, Exception? Error)> EstimateGenderAgeAsync(string imagePath, IEnumerable<DetectedTooth> detections)
    {
        var (ageRange, medianAge) = DentalAgeEstimator.EstimateAgeRange(detections);
        
        // InsightFace facial recognition removed - gender cannot be
        // reliably mathematically deduced from wide panoramic dental x-rays.
        var gender = "Indeterminate";
        
        return await Task.FromResult<(string Gender, string AgeRange, int? MedianAge, Exception? Error)>((gender, ageRange, medianAge, null));
    }
}
