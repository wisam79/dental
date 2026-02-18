using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;
using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;
using DentalID.Application.Configuration;

namespace DentalID.Application.Services;

/// <summary>
/// ONNX-based AI Pipeline Service for dental image analysis.
/// Uses YOLOv8-style models for detection and ResNet-style encoder for features.
/// </summary>
public class OnnxAiPipelineService : IAiPipelineService
{
    private readonly AiConfiguration _config;
    private readonly ILoggerService _logger;
    
    private InferenceSession? _teethDetector;
    private InferenceSession? _pathologyDetector;
    private InferenceSession? _encoder;
    private InferenceSession? _genderAgeEstimator;
    
    private bool _isDisposed;
    private bool _isInitialized;

    public bool IsReady => _isInitialized && _teethDetector != null && _pathologyDetector != null;

    public OnnxAiPipelineService(AiConfiguration config, ILoggerService logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task InitializeAsync(string modelsDirectory)
    {
        try
        {
            _logger.LogInformation($"Initializing ONNX models from: {modelsDirectory}");
            
            // Load models asynchronously
            await Task.Run(() =>
            {
                var teethModelPath = Path.Combine(modelsDirectory, "teeth_detect.onnx");
                var pathologyModelPath = Path.Combine(modelsDirectory, "pathology_detect.onnx");
                var encoderModelPath = Path.Combine(modelsDirectory, "encoder.onnx");
                var genderAgeModelPath = Path.Combine(modelsDirectory, "genderage.onnx");

                // Initialize sessions with optimization options
                var sessionOptions = new SessionOptions();
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
                sessionOptions.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
                
                if (File.Exists(teethModelPath))
                {
                    _teethDetector = new InferenceSession(teethModelPath, sessionOptions);
                    _logger.LogInformation("Teeth detector model loaded successfully");
                }
                else
                {
                    _logger.LogWarning($"Teeth detector model not found: {teethModelPath}");
                }

                if (File.Exists(pathologyModelPath))
                {
                    _pathologyDetector = new InferenceSession(pathologyModelPath, sessionOptions);
                    _logger.LogInformation("Pathology detector model loaded successfully");
                }
                else
                {
                    _logger.LogWarning($"Pathology detector model not found: {pathologyModelPath}");
                }

                if (File.Exists(encoderModelPath))
                {
                    _encoder = new InferenceSession(encoderModelPath, sessionOptions);
                    _logger.LogInformation("Encoder model loaded successfully");
                }
                else
                {
                    _logger.LogWarning($"Encoder model not found: {encoderModelPath}");
                }

                if (File.Exists(genderAgeModelPath))
                {
                    _genderAgeEstimator = new InferenceSession(genderAgeModelPath, sessionOptions);
                    _logger.LogInformation("Gender/Age estimator model loaded successfully");
                }
                else
                {
                    _logger.LogWarning($"Gender/Age estimator model not found: {genderAgeModelPath}");
                }
            });

            _isInitialized = true;
            _logger.LogInformation("AI Pipeline initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize AI pipeline");
            throw;
        }
    }

    public async Task<AnalysisResult> AnalyzeImageAsync(Stream imageStream, string? fileName = null)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new AnalysisResult();

        try
        {
            // Preprocess image
            var (inputTensor, originalWidth, originalHeight) = await PreprocessImageAsync(imageStream, _config.Model.DetectionInputSize);
            
            // Run teeth detection
            if (_teethDetector != null)
            {
                var teeth = await DetectTeethAsync(_teethDetector, inputTensor, originalWidth, originalHeight);
                result.Teeth = teeth;
            }

            // Run pathology detection
            if (_pathologyDetector != null)
            {
                var pathologies = await DetectPathologiesAsync(_pathologyDetector, inputTensor, originalWidth, originalHeight);
                result.Pathologies = pathologies;
            }

            // Map pathologies to teeth
            MapPathologiesToTeeth(result);

            // Extract features for matching
            if (_encoder != null)
            {
                imageStream.Position = 0; // Reset stream position to beginning
                var (features, _) = await ExtractFeaturesAsync(imageStream);
                result.FeatureVector = features;
            }

            // Estimate age and gender
            if (_genderAgeEstimator != null)
            {
                imageStream.Position = 0; // Reset stream position to beginning
                var (age, gender) = await EstimateDemographicsAsync(imageStream);
                result.EstimatedAge = age;
                result.EstimatedGender = gender;
            }

            // Generate dental fingerprint
            if (result.Teeth.Count > 0)
            {
                result.Fingerprint = GenerateDentalFingerprint(result);
            }

            // Add forensic flags
            AddForensicFlags(result);

            result.ProcessingTimeMs = stopwatch.ElapsedMilliseconds;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Analysis pipeline failed");
            result.Error = $"Analysis failed: {ex.Message}";
        }

        return result;
    }

    public async Task<List<DetectedTooth>> DetectTeethAsync(Stream imageStream)
    {
        if (_teethDetector == null)
            return new List<DetectedTooth>();

        var (inputTensor, originalWidth, originalHeight) = await PreprocessImageAsync(imageStream, _config.Model.DetectionInputSize);
        return await DetectTeethAsync(_teethDetector, inputTensor, originalWidth, originalHeight);
    }

    public async Task<List<DetectedPathology>> DetectPathologiesAsync(Stream imageStream)
    {
        if (_pathologyDetector == null)
            return new List<DetectedPathology>();

        var (inputTensor, originalWidth, originalHeight) = await PreprocessImageAsync(imageStream, _config.Model.DetectionInputSize);
        return await DetectPathologiesAsync(_pathologyDetector, inputTensor, originalWidth, originalHeight);
    }

    public async Task<(float[]? vector, string? error)> ExtractFeaturesAsync(Stream imageStream)
    {
        if (_encoder == null)
            return (null, "Encoder model not loaded");

        try
        {
            var (inputTensor, _, _) = await PreprocessImageAsync(imageStream, _config.Model.EncoderInputSize);
            
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor)
            };

            using var results = await Task.Run(() => _encoder.Run(inputs));
            var output = results.FirstOrDefault();
            
            if (output == null)
                return (null, "No output from encoder");

            var tensor = output.AsTensor<float>();
            return (tensor.ToArray(), null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Feature extraction failed");
            return (null, ex.Message);
        }
    }

    private async Task<(Tensor<float> tensor, int originalWidth, int originalHeight)> PreprocessImageAsync(Stream imageStream, int targetSize)
    {
        return await Task.Run(() =>
        {
            using var bitmap = SKBitmap.Decode(imageStream);
            var originalWidth = bitmap.Width;
            var originalHeight = bitmap.Height;

            // Resize maintaining aspect ratio
            var (newWidth, newHeight) = CalculateResizeDimensions(originalWidth, originalHeight, targetSize);
            
            using var resized = bitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
            
            // Convert to tensor (normalized to [0, 1])
            var tensor = new DenseTensor<float>(new[] { 1, 3, targetSize, targetSize });
            
            // Add padding if needed
            var padX = (targetSize - newWidth) / 2;
            var padY = (targetSize - newHeight) / 2;
            
            for (int y = 0; y < targetSize; y++)
            {
                for (int x = 0; x < targetSize; x++)
                {
                    var srcX = x - padX;
                    var srcY = y - padY;
                    
                    if (srcX >= 0 && srcX < newWidth && srcY >= 0 && srcY < newHeight)
                    {
                        var pixel = resized.GetPixel(srcX, srcY);
                        tensor[0, 0, y, x] = pixel.Red / 255.0f;
                        tensor[0, 1, y, x] = pixel.Green / 255.0f;
                        tensor[0, 2, y, x] = pixel.Blue / 255.0f;
                    }
                    else
                    {
                        // Padding - black
                        tensor[0, 0, y, x] = 0;
                        tensor[0, 1, y, x] = 0;
                        tensor[0, 2, y, x] = 0;
                    }
                }
            }

            return (tensor, originalWidth, originalHeight);
        });
    }

    private (int width, int height) CalculateResizeDimensions(int originalWidth, int originalHeight, int targetSize)
    {
        var ratio = Math.Min((double)targetSize / originalWidth, (double)targetSize / originalHeight);
        return ((int)(originalWidth * ratio), (int)(originalHeight * ratio));
    }

    private Task<List<DetectedTooth>> DetectTeethAsync(InferenceSession session, Tensor<float> inputTensor, int originalWidth, int originalHeight)
    {
        return Task.Run(() =>
        {
            var teeth = new List<DetectedTooth>();
            var threshold = _config.Thresholds.TeethThreshold;

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            using var results = session.Run(inputs);
            var output = results.FirstOrDefault();

            if (output == null) return teeth;

            var detections = output.AsTensor<float>();
            // Parse YOLOv8 output format (batch, boxes, properties)
            // This is a simplified parser - actual implementation depends on model output format
            
            // Apply NMS
            var filtered = ApplyNms(teeth, _config.Thresholds.NmsIoUThreshold);
            
            return filtered.Take(32).ToList(); // Max 32 teeth
        });
    }

    private Task<List<DetectedPathology>> DetectPathologiesAsync(InferenceSession session, Tensor<float> inputTensor, int originalWidth, int originalHeight)
    {
        return Task.Run(() =>
        {
            var pathologies = new List<DetectedPathology>();
            
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("images", inputTensor)
            };

            using var results = session.Run(inputs);
            var output = results.FirstOrDefault();

            if (output == null) return pathologies;

            var detections = output.AsTensor<float>();
            // Parse detection output

            // Bug #2 Fix: use LINQ Where instead of Remove-during-foreach to avoid InvalidOperationException
            pathologies = pathologies
                .Where(pathology =>
                {
                    var classThreshold = _config.Thresholds.PathologyThresholds.GetValueOrDefault(
                        pathology.ClassName,
                        _config.Thresholds.DefaultThreshold);
                    return pathology.Confidence >= classThreshold;
                })
                .ToList();

            return pathologies;
        });
    }

    private void MapPathologiesToTeeth(AnalysisResult result)
    {
        foreach (var pathology in result.Pathologies)
        {
            foreach (var tooth in result.Teeth)
            {
                if (IsOverlapping(pathology, tooth, _config.Thresholds.ProximityThreshold))
                {
                    pathology.ToothNumber = tooth.FdiNumber;
                    break;
                }
            }
        }
    }

    private bool IsOverlapping(DetectedPathology pathology, DetectedTooth tooth, float threshold)
    {
        // Fallacy #10 Fix: Use intersection-over-pathology area instead of center-point containment.
        // The old center-point check had two critical problems:
        //   1. The 'threshold' parameter was completely IGNORED — always used binary containment.
        //   2. A pathology whose center lands in a tooth but has minimal actual overlap was
        //      incorrectly associated. Conversely, large pathologies spanning two teeth were
        //      only assigned to one based on center position alone.
        // The area-based IoP (Intersection over Pathology) check correctly measures how much
        // of the pathology is covered by the tooth bounding box, consistent with YoloDetectionParser.
        float left = Math.Max(pathology.X, tooth.X);
        float top = Math.Max(pathology.Y, tooth.Y);
        float right = Math.Min(pathology.X + pathology.Width, tooth.X + tooth.Width);
        float bottom = Math.Min(pathology.Y + pathology.Height, tooth.Y + tooth.Height);

        float intersectionArea = Math.Max(0, right - left) * Math.Max(0, bottom - top);
        float pathologyArea = pathology.Width * pathology.Height;

        return pathologyArea > 0 && (intersectionArea / pathologyArea) > threshold;
    }

    private async Task<(int? age, string? gender)> EstimateDemographicsAsync(Stream imageStream)
    {
        if (_genderAgeEstimator == null)
            return (null, null);

        try
        {
            var (inputTensor, _, _) = await PreprocessImageAsync(imageStream, _config.Model.GenderAgeInputSize);
            
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input", inputTensor)
            };

            using var results = await Task.Run(() => _genderAgeEstimator.Run(inputs));
            var output = results.FirstOrDefault();

            if (output == null) return (null, null);

            var predictions = output.AsTensor<float>();
            
            // Parse age and gender from model output
            // This depends on the specific model architecture
            var gender = predictions[0] > 0.5 ? "Male" : "Female";
            var age = (int)(predictions[1] * 100); // Assuming normalized age

            return (age, gender);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Demographic estimation failed");
            return (null, null);
        }
    }

    private DentalFingerprint GenerateDentalFingerprint(AnalysisResult result)
    {
        var fingerprint = new DentalFingerprint
        {
            Code = $"DF-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString()[..8].ToUpper()}",
            ToothMap = new Dictionary<int, string>()
        };

        // Map dental conditions
        foreach (var tooth in result.Teeth)
        {
            var conditions = new List<string>();
            
            // Check for pathologies on this tooth
            var toothPathologies = result.Pathologies.Where(p => p.ToothNumber == tooth.FdiNumber);
            foreach (var path in toothPathologies)
            {
                conditions.Add(path.ClassName switch
                {
                    "Caries" => "C",
                    "Crown" => "C",
                    "Filling" => "F",
                    "Implant" => "I",
                    "Root Piece" => "R",
                    "Root canal obturation" => "RCT",
                    "Missing teeth" => "M",
                    _ => "?"
                });
            }

            fingerprint.ToothMap[tooth.FdiNumber] = string.Join(",", conditions.Distinct());
        }

        // Calculate uniqueness score based on features
        fingerprint.UniquenessScore = CalculateUniquenessScore(result);

        fingerprint.Features = new List<string>();
        if (result.Pathologies.Any(p => p.ClassName.Contains("Implant")))
            fingerprint.Features.Add($"Implant at #{result.Pathologies.First(p => p.ClassName.Contains("Implant")).ToothNumber}");
        if (result.Pathologies.Any(p => p.ClassName.Contains("Crown")))
            fingerprint.Features.Add($"Crowns: {result.Pathologies.Count(p => p.ClassName.Contains("Crown"))}");

        return fingerprint;
    }

    private double CalculateUniquenessScore(AnalysisResult result)
    {
        // Scientifically-based uniqueness calculation
        double score = 0.5; // Base score

        // Missing teeth increase uniqueness
        var missingStandard = 28 - result.Teeth.Count(t => t.FdiNumber % 10 < 8);
        score += missingStandard * 0.05;

        // Restorations indicate uniqueness
        score += result.Pathologies.Count(p => p.ClassName.Contains("Filling") || p.ClassName.Contains("Crown")) * 0.03;

        // Implants are highly unique
        score += result.Pathologies.Count(p => p.ClassName.Contains("Implant")) * 0.1;

        return Math.Min(1.0, score);
    }

    private void AddForensicFlags(AnalysisResult result)
    {
        // Low confidence detection
        if (result.Teeth.Any(t => t.Confidence < 0.5))
            result.Flags.Add("Low Confidence - Enhancement Recommended");

        // Check for potential deepfake/manipulation (simplified)
        if (result.Teeth.Count == 0 && result.Pathologies.Count == 0)
            result.Flags.Add("Suspicious - No detections");

        // Duplicate checks
        var duplicates = result.Teeth.GroupBy(t => t.FdiNumber).Where(g => g.Count() > 1);
        if (duplicates.Any())
            result.Flags.Add("Duplicate Detections - Review Required");
    }

    private List<DetectedTooth> ApplyNms(List<DetectedTooth> detections, float iouThreshold)
    {
        // Non-Maximum Suppression implementation
        var sorted = detections.OrderByDescending(d => d.Confidence).ToList();
        var keep = new List<DetectedTooth>();

        while (sorted.Count > 0)
        {
            var best = sorted[0];
            keep.Add(best);
            sorted.RemoveAt(0);

            sorted.RemoveAll(d => CalculateIoU(best, d) > iouThreshold);
        }

        return keep;
    }

    private float CalculateIoU(DetectedTooth a, DetectedTooth b)
    {
        var x1 = Math.Max(a.X, b.X);
        var y1 = Math.Max(a.Y, b.Y);
        var x2 = Math.Min(a.X + a.Width, b.X + b.Width);
        var y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);

        if (x2 < x1 || y2 < y1) return 0;

        var intersection = (x2 - x1) * (y2 - y1);
        var areaA = a.Width * a.Height;
        var areaB = b.Width * b.Height;
        var union = areaA + areaB - intersection;

        return union > 0 ? intersection / union : 0;
    }

    public void Dispose()
    {
        if (_isDisposed) return;

        _teethDetector?.Dispose();
        _pathologyDetector?.Dispose();
        _encoder?.Dispose();
        _genderAgeEstimator?.Dispose();

        _isDisposed = true;
    }
}
