using System.Diagnostics;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;
using SkiaSharp;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;

namespace DentalID.Application.Services;

/// <summary>
/// Hardened AI Pipeline.
/// Features: Thread Confinement, Input Validation, Structured Logging, Robust Resource Management.
/// </summary>
public class OnnxInferenceService : IAiPipelineService, IDisposable
{
    private InferenceSession? _teethDetector;
    private InferenceSession? _pathologyDetector;
    private InferenceSession? _encoder;
    private InferenceSession? _genderAge;
    
    private volatile bool _isInitialized;
    public bool IsReady => _isInitialized;

    private readonly AiConfiguration _config;
    private readonly AiSettings _aiSettings;
    private readonly IImageIntegrityService? _integrityService;
    private readonly IForensicRulesEngine _rulesEngine;
    private readonly IBiometricService _biometricService;
    private readonly IDentalIntelligenceService _intelligenceService;
    private readonly ICacheService _cacheService;
    private readonly IYoloDetectionParser _yoloParser;
    private readonly IFdiSpatialService _fdiService;
    private readonly IForensicHeuristicsService _heuristicsService;
    private readonly ITensorPreparationService _tensorPrep;
    private readonly ILoggerService _logger; // Structured Logging

    // Thread Confinement: All ONNX calls run on this scheduler to prevent internal race conditions
    private readonly SemaphoreSlim _inferenceLock = new(1); 

    public OnnxInferenceService(
        AiConfiguration config, 
        AiSettings aiSettings,
        ILoggerService logger,
        IBiometricService biometricService,
        IDentalIntelligenceService intelligenceService,
        ICacheService cacheService,
        IYoloDetectionParser yoloParser,
        IFdiSpatialService fdiService,

        IForensicHeuristicsService heuristicsService,
        ITensorPreparationService tensorPrep,
        IImageIntegrityService? integrityService = null, 
        IForensicRulesEngine? rulesEngine = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _aiSettings = aiSettings ?? throw new ArgumentNullException(nameof(aiSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _biometricService = biometricService ?? throw new ArgumentNullException(nameof(biometricService));
        _intelligenceService = intelligenceService ?? throw new ArgumentNullException(nameof(intelligenceService));
        _cacheService = cacheService ?? throw new ArgumentNullException(nameof(cacheService));
        _yoloParser = yoloParser ?? throw new ArgumentNullException(nameof(yoloParser));
        _fdiService = fdiService ?? throw new ArgumentNullException(nameof(fdiService));
        _heuristicsService = heuristicsService ?? throw new ArgumentNullException(nameof(heuristicsService));
        _tensorPrep = tensorPrep ?? throw new ArgumentNullException(nameof(tensorPrep));
        _integrityService = integrityService;
        _rulesEngine = rulesEngine ?? new ForensicRulesEngine();
    }

    public async Task InitializeAsync(string modelsDirectory)
    {
        await _inferenceLock.WaitAsync();
        try
        {
            _logger.LogInformation($"Initializing AI Engine from: {modelsDirectory}");
            _isInitialized = false;

            // Cleanup old sessions safely
            DisposeSessions();

            using var opts = new SessionOptions();
            opts.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
            
            if (_aiSettings.EnableGpu)
            {
                _logger.LogWarning("GPU acceleration is not yet implemented. Running on CPU. " +
                                   "Set 'AiSettings:EnableGpu' to false to suppress this warning.");
            }

            var teethPath = Path.Combine(modelsDirectory, "teeth_detect.onnx");
            var pathPath = Path.Combine(modelsDirectory, "pathology_detect.onnx");
            var encoderPath = Path.Combine(modelsDirectory, "encoder.onnx");
            var genderAgePath = Path.Combine(modelsDirectory, "genderage.onnx");

            // Critical Model Check
            if (!File.Exists(teethPath)) throw new FileNotFoundException("Critical Model Missing", teethPath);

            _teethDetector = new InferenceSession(teethPath, opts);
            
            if (File.Exists(pathPath)) _pathologyDetector = new InferenceSession(pathPath, opts);
            if (File.Exists(encoderPath)) _encoder = new InferenceSession(encoderPath, opts);
            if (File.Exists(genderAgePath)) _genderAge = new InferenceSession(genderAgePath, opts);

            // Bug #6 Fix: Allocate buffers BEFORE RunSelfDiagnostic so the diagnostic
            // doesn't use a null _detectionBuffer when PrepareDetectionTensor is called.
            int detectionSize = _config.Model.DetectionInputSize * _config.Model.DetectionInputSize * 3;
            _detectionBuffer = new float[detectionSize];
            _ttaDetectionBuffer = new float[detectionSize];

            // Encoder buffer (Fixed 1024x1024x3 for SAM)
            if (_encoder != null)
            {
                _encoderBuffer = new float[1024 * 1024 * 3];
            }

            // Cold Start / Self Diagnostic (buffers now allocated)
            RunSelfDiagnostic();

            _isInitialized = true;
            _logger.LogInformation("AI Engine Initialized Successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI Engine Initialization Failed");
            DisposeSessions();
            throw; // Rethrow to Bootstrapper
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    // LOH Buffers
    private float[]? _detectionBuffer;
    private float[]? _ttaDetectionBuffer;
    private float[]? _encoderBuffer;

    private void DisposeSessions()
    {
        _teethDetector?.Dispose(); _teethDetector = null;
        _pathologyDetector?.Dispose(); _pathologyDetector = null;
        _encoder?.Dispose(); _encoder = null;
        _genderAge?.Dispose(); _genderAge = null;
    }

    public async Task<AnalysisResult> AnalyzeImageAsync(Stream imageStream, string? fileName = null)
    {
        // 1. Caching Check (Optimization) - OUTSIDE LOCK (Read-only if cache is thread-safe)
        // _integrityService and _cacheService should be thread-safe or immutable for this to be safe without lock.
        // Assuming they are. If not, move this inside lock too.

        string? cacheKey = null;
        if (_integrityService != null)
        {
            try
            {
                var hash = _integrityService.ComputeHash(imageStream);
                imageStream.Position = 0;
                cacheKey = $"analysis_{hash}";
                
                if (_cacheService.Exists(cacheKey))
                {
                    var cachedResult = _cacheService.Get<AnalysisResult>(cacheKey);
                    if (cachedResult != null)
                    {
                        _logger.LogAudit("CACHE_HIT", "SYSTEM", $"Returning cached analysis for {fileName ?? "stream"}");
                        cachedResult.ProcessingTimeMs = 0; 
                        return cachedResult;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Hashing failed for cache lookup: {ex.Message}");
            }
        }

        await _inferenceLock.WaitAsync(); // Serialize Access
        var result = new AnalysisResult();
        var sw = Stopwatch.StartNew();

        try
        {
            // Check initialization AFTER acquiring lock (finally block handles Release)
            if (!_isInitialized) 
            {
                throw new InvalidOperationException("AI Engine not initialized. Call InitializeAsync first.");
            }

            _logger.LogAudit("ANALYSIS_START", "SYSTEM", $"Analyzing Stream. Size: {imageStream.Length} bytes");

            // 2. Load & Validate Image
            imageStream.Position = 0;
            using var bitmap = SKBitmap.Decode(imageStream);
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                result.Error = "Image decoding failed or image is empty.";
                return result;
            }

            // 3. Prepare Tensors (On Inference Thread)
            var (tensor640, scale640, padX640, padY640) = _tensorPrep.PrepareDetectionTensor(bitmap, _config.Model.DetectionInputSize, _detectionBuffer);
            
            // 4. Run Inference (Sequential for stability, could be parallel but locked)
            // Teeth Detection
            var teethInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_teethDetector!.InputNames[0], tensor640) };
            using var teethResults = _teethDetector.Run(teethInputs);
            
            // Delegate to YoloParser
            result.Teeth = _yoloParser.ParseTeethDetections(
                teethResults.First().AsTensor<float>(), 
                _config.Model.DetectionInputSize, 
                scale640, padX640, padY640, 
                _aiSettings.ConfidenceThreshold);
                
            result.RawTeeth = new List<DetectedTooth>(result.Teeth);

            // Pathology Detection
            if (_pathologyDetector != null)
            {
                var pathInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_pathologyDetector.InputNames[0], tensor640) };
                using var pathResults = _pathologyDetector.Run(pathInputs);
                
                // Delegate to YoloParser
                result.Pathologies = _yoloParser.ParsePathologyDetections(
                    pathResults.First().AsTensor<float>(), 
                    _config.Model.DetectionInputSize, 
                    scale640, padX640, padY640, 
                    _aiSettings.ConfidenceThreshold, 
                    _config.Model.PathologyClasses);
                    
                result.RawPathologies = new List<DetectedPathology>(result.Pathologies);
            }

            // TTA (Test Time Augmentation) Check
            if (_config.EnableTTA && result.Teeth.Any())
            {
                ApplyTestTimeAugmentation(result, bitmap);
            }

            // Feature Extraction (SAM Encoder → mean-pooled spatial embeddings)
            if (_encoder != null)
            {
                var (vector, err) = ExtractFeaturesInternal(bitmap);
                result.FeatureVector = vector;
                if (err != null) result.Error += $" | Encoder: {err}";
            }

            // Age/Gender (InsightFace model — needs BGR 0-255 input)
            if (_genderAge != null)
            {
                var (age, gender) = EstimateAgeGender(bitmap);
                result.EstimatedAge = age;
                result.EstimatedGender = gender;
            }

            // 5. Fusion & Logic
            // Map pathologies using RawTeeth to ensure mapping even if teeth are below sensitivity threshold
            _yoloParser.MapPathologiesToTeeth(result.RawTeeth, result.RawPathologies);
            
            // 6. Forensic Integrity (Heuristic Fallback for Deepfake Detection)
            _heuristicsService.ApplyChecks(result);

            // 7. Advanced Intelligence Analysis
            _intelligenceService.Analyze(result);

            _rulesEngine.ApplyRules(result);
            
            // 8. Biometric Fingerprint Generation
            result.Fingerprint = _biometricService.GenerateFingerprint(result.Teeth, result.Pathologies);
            if (result.FeatureVector != null)
            {
                result.Fingerprint.FeatureVector = result.FeatureVector;
            }

            // Cache the result if we have a key
            if (cacheKey != null && string.IsNullOrEmpty(result.Error))
            {
                _cacheService.Set(cacheKey, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inference Failed");
            result.Error = $"Internal Engine Panic: {ex.Message}";
        }
        finally
        {
            _inferenceLock.Release();
        }

        sw.Stop();
        result.ProcessingTimeMs = sw.ElapsedMilliseconds;
        _logger.LogAudit("ANALYSIS_COMPLETE", "SYSTEM", $"Time: {sw.ElapsedMilliseconds}ms | Teeth: {result.Teeth.Count} | Pathologies: {result.Pathologies.Count}");
        
        return result;
    }

    // ... (Helper methods ParseYolo, MapPathologies, etc. remain largely the same logic, but private and safe)
    // For brevity in this plan, I assume helper helper methods are carried over but protected by the Lock.

    private void RunSelfDiagnostic()
    {
        try
        {
            using var bmp = new SKBitmap(640, 640);
            using (var canvas = new SKCanvas(bmp)) { canvas.Clear(SKColors.Black); }
            var (tensor, _, _, _) = _tensorPrep.PrepareDetectionTensor(bmp, _config.Model.DetectionInputSize, _detectionBuffer);
            using var diagnosticResult = _teethDetector?.Run(new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_teethDetector!.InputNames[0], tensor) });
        }
        catch (Exception ex)
        {
            throw new Exception($"Self-Diagnostic Failed: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        DisposeSessions();
        _inferenceLock.Dispose();
    }
    
    // --- Helpers (Simplified for artifacts check) ---
    // --- Helper Logic extracted to TensorPreparationService ---

    // Mapping Logic (Same as before)
    // --- Helper Logic extracted to specialized services (YoloDetectionParser, FdiSpatialService, ForensicHeuristicsService) ---

    public async Task<(float[]? vector, string? error)> ExtractFeaturesAsync(Stream imageStream)
    {
        if (!_isInitialized) return (null, "AI Pipeline not initialized");
        await _inferenceLock.WaitAsync();
        try
        {
             imageStream.Position = 0;
             using var bitmap = SKBitmap.Decode(imageStream);
             if (bitmap == null) return (null, "Failed to decode image");
             
             return ExtractFeaturesInternal(bitmap);
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

    /// <summary>
    /// Performs heuristic-based forensic checks to detect potential image manipulation or deepfakes
    /// when the dedicated AI model is unavailable.
    /// </summary>
    // --- Forensic Checks extracted to ForensicHeuristicsService ---

    private (float[]? vector, string? error) ExtractFeaturesInternal(SKBitmap bitmap)
    {
        if (_encoder == null) return (null, "Encoder model is NULL");
        try
        {
            var tensor = _tensorPrep.PrepareEncoderTensor(bitmap, 1024, _encoderBuffer);

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_encoder.InputNames[0], tensor) };
            using var results = _encoder.Run(inputs);
            var output = results.First().AsTensor<float>();
            
            // Bug #27 Fix: Validate output tensor rank before indexing dimensions.
            // Without this check, accessing Dimensions[1..3] on a tensor with fewer dims throws IndexOutOfRangeException.
            if (output.Dimensions.Length < 4)
                return (null, $"Unexpected encoder output shape rank: {output.Dimensions.Length}. Expected [1, C, H, W].");

            // Output is [1, 256, 64, 64] — mean-pool spatial dims to get [256] descriptor
            int channels = output.Dimensions[1];
            int h = output.Dimensions[2];
            int w = output.Dimensions[3];
            int spatialCount = h * w;
            var vector = new float[channels];
            
            for (int c = 0; c < channels; c++)
            {
                float sum = 0;
                for (int sy = 0; sy < h; sy++)
                    for (int sx = 0; sx < w; sx++)
                        sum += output[0, c, sy, sx];
                vector[c] = sum / spatialCount;
            }
            
            return (vector, null);
        }
        catch (Exception ex) { return (null, ex.Message); }
    }
    
    /// <summary>
    /// Estimates age and gender using InsightFace genderage model.
    /// Model expects BGR 0-255 input in NCHW format [-1, 3, 96, 96].
    /// Output is [1, 3]: [P(female), P(male), age_scaled].
    /// </summary>
    private (int? age, string? gender) EstimateAgeGender(SKBitmap bitmap)
    {
        if (_genderAge == null) return (null, null);
        try
        {
            var tensor = _tensorPrep.PrepareAgeGenderTensor(bitmap, _config.Model.GenderAgeInputSize);

            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_genderAge.InputNames[0], tensor) };
            using var results = _genderAge.Run(inputs);
            var data = results.First().AsTensor<float>().ToArray();
            
            if (data.Length >= 3)
            {
                string gender = data[0] > data[1] ? "Female" : "Male";
                // InsightFace outputs raw age
                int age = (int)Math.Round(data[2]);
                age = Math.Clamp(age, 0, 120);
                return (age, gender);
            }
            return (null, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Age/Gender estimation failed");
            return (null, null);
        }
    }

    public async Task<List<DetectedTooth>> DetectTeethAsync(Stream imageStream)
    {
        if (!_isInitialized || _teethDetector == null)
        {
            throw new InvalidOperationException("AI Engine not initialized or teeth detector not loaded.");
        }

        if (imageStream == null || imageStream.Length == 0)
        {
            return new List<DetectedTooth>();
        }

        await _inferenceLock.WaitAsync();
        try
        {
            imageStream.Position = 0;
            using var bitmap = SKBitmap.Decode(imageStream);
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                return new List<DetectedTooth>();
            }

            var (tensor640, scale640, padX640, padY640) = _tensorPrep.PrepareDetectionTensor(bitmap, _config.Model.DetectionInputSize, _detectionBuffer);
            var teethInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_teethDetector.InputNames[0], tensor640) };
            using var teethResults = _teethDetector.Run(teethInputs);
            return _yoloParser.ParseTeethDetections(
                teethResults.First().AsTensor<float>(), 
                _config.Model.DetectionInputSize, 
                scale640, padX640, padY640, 
                _aiSettings.ConfidenceThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teeth detection failed");
            return new List<DetectedTooth>();
        }
        finally
        {
            _inferenceLock.Release();
        }
    } // End DetectTeethAsync

    // --- TTA & Ensemble methods ---
    
    private void ApplyTestTimeAugmentation(AnalysisResult result, SKBitmap original)
    {
        // Pass 2: Horizontal Flip
        using var flipped = new SKBitmap(original.Width, original.Height);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.Clear();
            canvas.Scale(-1, 1, original.Width / 2.0f, original.Height / 2.0f);
            canvas.DrawBitmap(original, 0, 0);
        }

        var (flippedTeeth, flippedPathologies) = DetectObjects(flipped);

        // Transform flipped results back to original coordinates
        foreach (var tooth in flippedTeeth)
        {
            // X' = Width - (X + W) in normalized coords [0..1]
            // X_orig = 1.0 - (X_flip + W_flip)
            tooth.X = 1.0f - (tooth.X + tooth.Width);
        }

        foreach (var path in flippedPathologies)
        {
             path.X = 1.0f - (path.X + path.Width);
        }

        // Merge Results (Ensemble Voting)
        MergeDetections(result.RawTeeth, flippedTeeth);
        // Bug #20 fix: merge into a copy so RawPathologies stays as original baseline
        var mergedPathologies = new List<DetectedPathology>(result.RawPathologies);
        MergePathologies(mergedPathologies, flippedPathologies);

        // Re-apply NMS and Rules after merge
        result.Teeth = _yoloParser.ApplyNms(result.RawTeeth, _aiSettings.IouThreshold); 
        result.Teeth = _fdiService.RefineFdiNumbering(result.Teeth);
        
        // Update pathologies list from merged copy
        result.Pathologies = mergedPathologies; 
    }
    
    // Internal helper for TTA to allow running full detection pipeline on a generated bitmap (without lock if already locked)
    private (List<DetectedTooth> Teeth, List<DetectedPathology> Pathologies) DetectObjects(SKBitmap bitmap)
    {
        // Bug #9 fix: use separate TTA buffer to avoid overwriting original tensor
        var (tensor, scale, padX, padY) = _tensorPrep.PrepareDetectionTensor(bitmap, _config.Model.DetectionInputSize, _ttaDetectionBuffer);
        
        var teeth = new List<DetectedTooth>();
        var pathologies = new List<DetectedPathology>();
        
        if (_teethDetector != null)
        {
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_teethDetector.InputNames[0], tensor) };
            using var results = _teethDetector.Run(inputs);
            teeth = _yoloParser.ParseTeethDetections(results.First().AsTensor<float>(), _config.Model.DetectionInputSize, scale, padX, padY, _aiSettings.ConfidenceThreshold);
        }

        if (_pathologyDetector != null)
        {
             var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_pathologyDetector.InputNames[0], tensor) };
             using var results = _pathologyDetector.Run(inputs);
             pathologies = _yoloParser.ParsePathologyDetections(results.First().AsTensor<float>(), _config.Model.DetectionInputSize, scale, padX, padY, _aiSettings.ConfidenceThreshold, _config.Model.PathologyClasses);
        }
        
        return (teeth, pathologies);
    }

    private void MergeDetections(List<DetectedTooth> original, List<DetectedTooth> augmented)
    {
        foreach (var aug in augmented)
        {
            var match = original.FirstOrDefault(o => ForensicHeuristicsService.CalculateIoU(o.X, o.Y, o.Width, o.Height, aug.X, aug.Y, aug.Width, aug.Height) > _aiSettings.IouThreshold);
            if (match != null)
            {
                // Confirmed by TTA -> Boost confidence
                match.Confidence = Math.Min(0.99f, match.Confidence + 0.15f);
                // Average coordinates for precision
                match.X = (match.X + aug.X) / 2;
                match.Y = (match.Y + aug.Y) / 2;
                match.Width = (match.Width + aug.Width) / 2;
                match.Height = (match.Height + aug.Height) / 2;
            }
            else
            {
                // New detection from TTA? Only add if high confidence
                if (aug.Confidence > _aiSettings.ConfidenceThreshold)
                {
                    original.Add(aug);
                }
            }
        }
    }

    private void MergePathologies(List<DetectedPathology> original, List<DetectedPathology> augmented)
    {
         foreach (var aug in augmented)
        {
            var match = original.FirstOrDefault(o => o.ClassName == aug.ClassName && ForensicHeuristicsService.CalculateIoU(o.X, o.Y, o.Width, o.Height, aug.X, aug.Y, aug.Width, aug.Height) > 0.3f);
            if (match != null)
            {
                match.Confidence = Math.Min(0.99f, match.Confidence + 0.10f);
            }
            else if (aug.Confidence > _aiSettings.ConfidenceThreshold)
            {
                original.Add(aug);
            }
        }
    }

    public async Task<List<DetectedPathology>> DetectPathologiesAsync(Stream imageStream)
    {
        if (!_isInitialized || _pathologyDetector == null)
        {
            return new List<DetectedPathology>();
        }

        if (imageStream == null || imageStream.Length == 0)
        {
            return new List<DetectedPathology>();
        }

        await _inferenceLock.WaitAsync();
        try
        {
            imageStream.Position = 0;
            using var bitmap = SKBitmap.Decode(imageStream);
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                return new List<DetectedPathology>();
            }

            var (tensor640, scale640, padX640, padY640) = _tensorPrep.PrepareDetectionTensor(bitmap, _config.Model.DetectionInputSize, _detectionBuffer);
            var pathInputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_pathologyDetector.InputNames[0], tensor640) };
            using var pathResults = _pathologyDetector.Run(pathInputs);
            
            return _yoloParser.ParsePathologyDetections(
                pathResults.First().AsTensor<float>(), 
                _config.Model.DetectionInputSize, 
                scale640, padX640, padY640, 
                _aiSettings.ConfidenceThreshold,
                _config.Model.PathologyClasses);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pathology detection failed");
            return new List<DetectedPathology>();
        }
        finally
        {
            _inferenceLock.Release();
        }
    }

}
