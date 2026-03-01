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
    // Feature extraction: SAM encoder → mean-pool [1,256,64,64] → float[256]
    // ─────────────────────────────────────────────────────────────────────────

    public (float[]? vector, string? error) ExtractFeatures(SKBitmap bitmap)
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
            var vector = new float[channels * 4];

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
            return (vector, null);
        }
        catch (Exception ex) { return (null, ex.Message); }
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
