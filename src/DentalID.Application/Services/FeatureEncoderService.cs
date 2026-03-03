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
            var tensor = _tensorPrep.PrepareEncoderTensor(bitmap, 1024, _session.EncoderBuffer);
            var inputs = new List<NamedOnnxValue>
                { NamedOnnxValue.CreateFromTensor(_session.EncoderInputName, tensor) };
            using var results = _session.Encoder.Run(inputs);
            var output = results.First().AsTensor<float>();

            // Bug #27 Fix: validate output rank before indexing dimensions
            if (output.Dimensions.Length < 4)
                return (null, $"Unexpected encoder output rank {output.Dimensions.Length}, expected [1,C,H,W].");

            int channels     = output.Dimensions[1];
            int h            = output.Dimensions[2];
            int w            = output.Dimensions[3];
            int quadH = h / 2;
            int quadW = w / 2;
            int quadSpatialCount = quadH * quadW;
            
            // 1024 floats for SAM Deep Features + 160 floats for Spatial Geometry (32 teeth * 5 floats: conf,x,y,w,h)
            int expectedDeepFeatures = channels * 4;
            int totalFeatures = expectedDeepFeatures + 160; 
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
                        float* pCurrentCh = pOutput + (c * h * w);

                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                float val = *(pCurrentCh + (y * w) + x);
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

            // Append Spatial Features
            if (detections != null)
            {
                AppendSpatialGeometry(vector, expectedDeepFeatures, detections);
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
        // Standard 32 adult teeth
        int[] fdiKeys = {
            18, 17, 16, 15, 14, 13, 12, 11,
            21, 22, 23, 24, 25, 26, 27, 28,
            48, 47, 46, 45, 44, 43, 42, 41,
            31, 32, 33, 34, 35, 36, 37, 38
        };

        var toothDict = detections.Where(t => t.FdiNumber > 0).GroupBy(t => t.FdiNumber).ToDictionary(g => g.Key, g => g.First());

        for (int i = 0; i < fdiKeys.Length; i++)
        {
            int baseIndex = offset + (i * 5);
            if (toothDict.TryGetValue(fdiKeys[i], out var tooth))
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

    // ─────────────────────────────────────────────────────────────────────────
    // Age/gender: InsightFace model — BGR 0-255, NCHW [-1,3,96,96] → [1,3]
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<(string Gender, string AgeRange, int? MedianAge, Exception? Error)> EstimateGenderAgeAsync(string imagePath, IEnumerable<DetectedTooth> detections)
    {
        var (ageRange, medianAge) = DentalAgeEstimator.EstimateAgeRange(detections);
        
        // InsightFace facial recognition removed - gender cannot be
        // reliably mathematically deduced from wide panoramic dental x-rays.
        var gender = "Unknown";
        
        return await Task.FromResult<(string Gender, string AgeRange, int? MedianAge, Exception? Error)>((gender, ageRange, medianAge, null));
    }
}
