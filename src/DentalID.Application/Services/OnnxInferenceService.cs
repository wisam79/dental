using System.Diagnostics;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;
using SkiaSharp;

namespace DentalID.Application.Services;

/// <summary>
/// Slim orchestrator facade (~200 lines).
/// Delegates all session management and sub-pipeline work to dedicated services.
/// Implements <see cref="IAiPipelineService"/> — public API is unchanged.
/// </summary>
public sealed class OnnxInferenceService : IAiPipelineService, IDisposable
{
    // ── Session manager ───────────────────────────────────────────────────────
    private readonly IOnnxSessionManager _sessions;

    // ── Sub-services ──────────────────────────────────────────────────────────
    private readonly ITeethDetectionService    _teethSvc;
    private readonly IPathologyDetectionService _pathSvc;
    private readonly IFeatureEncoderService    _encoderSvc;
    private readonly ISamSegmentationService   _samSvc;

    // ── Cross-cutting services ────────────────────────────────────────────────
    private readonly IYoloDetectionParser      _yoloParser;
    private readonly IForensicHeuristicsService _heuristicsService;
    private readonly IDentalIntelligenceService _intelligenceService;
    private readonly IForensicRulesEngine      _rulesEngine;
    private readonly IBiometricService         _biometricService;
    private readonly ICacheService             _cacheService;
    private readonly IImageIntegrityService?   _integrityService;
    private readonly ILoggerService            _logger;
    private string? _initializedModelsDirectory;

    public bool IsReady => _sessions.IsReady;

    public OnnxInferenceService(
        IOnnxSessionManager        sessions,
        ITeethDetectionService     teethSvc,
        IPathologyDetectionService pathSvc,
        IFeatureEncoderService     encoderSvc,
        ISamSegmentationService    samSvc,
        IYoloDetectionParser       yoloParser,
        IForensicHeuristicsService heuristicsService,
        IDentalIntelligenceService intelligenceService,
        IBiometricService          biometricService,
        ICacheService              cacheService,
        ILoggerService             logger,
        IImageIntegrityService?    integrityService = null,
        IForensicRulesEngine?      rulesEngine = null)
    {
        _sessions            = sessions            ?? throw new ArgumentNullException(nameof(sessions));
        _teethSvc            = teethSvc            ?? throw new ArgumentNullException(nameof(teethSvc));
        _pathSvc             = pathSvc             ?? throw new ArgumentNullException(nameof(pathSvc));
        _encoderSvc          = encoderSvc          ?? throw new ArgumentNullException(nameof(encoderSvc));
        _samSvc              = samSvc              ?? throw new ArgumentNullException(nameof(samSvc));
        _yoloParser          = yoloParser          ?? throw new ArgumentNullException(nameof(yoloParser));
        _heuristicsService   = heuristicsService   ?? throw new ArgumentNullException(nameof(heuristicsService));
        _intelligenceService = intelligenceService  ?? throw new ArgumentNullException(nameof(intelligenceService));
        _biometricService    = biometricService    ?? throw new ArgumentNullException(nameof(biometricService));
        _cacheService        = cacheService        ?? throw new ArgumentNullException(nameof(cacheService));
        _logger              = logger              ?? throw new ArgumentNullException(nameof(logger));
        _integrityService    = integrityService;
        _rulesEngine         = rulesEngine ?? new ForensicRulesEngine();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiPipelineService — Initialization
    // ─────────────────────────────────────────────────────────────────────────

    public Task InitializeAsync(string modelsDirectory)
    {
        if (string.IsNullOrWhiteSpace(modelsDirectory))
            throw new ArgumentException("Models directory cannot be null or empty.", nameof(modelsDirectory));

        _initializedModelsDirectory = modelsDirectory;
        return _sessions.InitializeAsync(modelsDirectory);
    }

    // ── Auto-Recovery ────────────────────────────────────────────────────────
    
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);

    private async Task EnsureInitializedAsync()
    {
        if (_sessions.IsReady) return;

        await _recoveryLock.WaitAsync();
        try
        {
            if (_sessions.IsReady) return;

            var modelsDir = string.IsNullOrWhiteSpace(_initializedModelsDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "models")
                : _initializedModelsDirectory;
            _initializedModelsDirectory = modelsDir;
            _logger.LogWarning($"[Auto-Recovery] AI Engine not initialized. Attempting to initialize from {modelsDir}...");
            
            await InitializeAsync(modelsDir);
            _logger.LogInformation("[Auto-Recovery] Recovered successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Auto-Recovery] Failed.");
            throw; // Propagate failure
        }
        finally
        {
            _recoveryLock.Release();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiPipelineService — Full Analysis Pipeline
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<AnalysisResult> AnalyzeImageAsync(Stream imageStream, string? fileName = null)
    {
        if (imageStream == null)
            throw new ArgumentNullException(nameof(imageStream));

        await EnsureInitializedAsync();
        var (seekableStream, ownsSeekableStream) = await PrepareSeekableStreamAsync(imageStream).ConfigureAwait(false);

        // 1. Cache check (outside lock — read-only)
        string? cacheKey = null;
        if (_integrityService != null)
        {
            try
            {
                TryResetStream(seekableStream);
                var hash = _integrityService.ComputeHash(seekableStream);
                TryResetStream(seekableStream);
                cacheKey = $"analysis_{hash}";
                if (_cacheService.Exists(cacheKey))
                {
                    var cached = _cacheService.Get<AnalysisResult>(cacheKey);
                    if (cached != null)
                    {
                        _logger.LogAudit("CACHE_HIT", "SYSTEM", $"Returning cached result for {fileName ?? "stream"}");
                        var cachedCopy = CloneAnalysisResult(cached);
                        cachedCopy.ProcessingTimeMs = 0;
                        if (ownsSeekableStream)
                        {
                            seekableStream.Dispose();
                        }
                        return cachedCopy;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Hashing failed for cache lookup: {ex.Message}");
            }
        }

        // 2. Acquire inference lock
        var result = new AnalysisResult();
        var sw     = Stopwatch.StartNew();
        var lockAcquired = false;

        try
        {
            await _sessions.InferenceLock.WaitAsync().ConfigureAwait(false);
            lockAcquired = true;

            // Execute heavy CPU-bound operations on a background thread pool explicitly
            // to prevent complete freezing of Avalonia's UI dispatcher.
            await Task.Run(async () =>
            {
                // Redundant check, but safe
                if (!_sessions.IsReady)
                    throw new InvalidOperationException($"AI Engine not initialized. SessionManager {_sessions.GetHashCode()} is not ready.");

                var streamLength = TryGetStreamLength(seekableStream);
                _logger.LogAudit("ANALYSIS_START", "SYSTEM",
                    streamLength.HasValue
                        ? $"Analyzing stream. Size: {streamLength.Value} bytes"
                        : "Analyzing stream. Size: unknown");

                TryResetStream(seekableStream);
                using var bitmap = SKBitmap.Decode(seekableStream);
                if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
                {
                    result.Error = "Image decoding failed or image is empty.";
                    return;
                }

                // 3. Teeth detection
                result.Teeth = _teethSvc.DetectTeeth(bitmap);
                result.RawTeeth = new List<DetectedTooth>(result.Teeth);

                // 4. Pathology detection
                result.Pathologies = _pathSvc.DetectPathologies(bitmap);
                result.RawPathologies = new List<DetectedPathology>(result.Pathologies);

                // 4.5 SAM Instance Segmentation (Zero-Shot using Bounding Boxes as Prompts)
                _samSvc.SegmentTeeth(bitmap, result.Teeth);
                _samSvc.SegmentPathologies(bitmap, result.Pathologies);

                // 5. Edge-crop rescue
                if (_teethSvc.ShouldApplyEdgeCropRescue(bitmap, result.RawTeeth))
                {
                    _teethSvc.ApplyEdgeCropRescue(result, bitmap);
                    if (!_sessions.IsReady) 
                    {
                        _logger.LogWarning("[Auto-Recovery] Session became unavailable mid-analysis. Skipping encoder steps.");
                    }
                    else
                    {
                        result.Teeth = _teethSvc.BuildFinalTeeth(result.RawTeeth);
                    }
                }

                if (_sessions.IsReady)
                {
                    // 6. Test-Time Augmentation
                    if (result.Teeth.Any())
                        _teethSvc.ApplyTta(result, bitmap);

                    // 7. Feature extraction (encoder)
                    if (_sessions.Encoder != null)
                    {
                        var (vector, err) = _encoderSvc.ExtractFeatures(bitmap, result.Teeth);
                        result.FeatureVector = vector;
                        if (err != null) result.Error += $" | Encoder: {err}";
                    }

                    // 8. Age / Gender (Dental Rules Engine)
                    if (_sessions.Encoder != null) // Tied to Encoder step now, not InsightFace genderAge
                    {
                        var dictImage = "stream";
                        // Note: imagePath was removed in an earlier iteration.
                        // We'll pass the list of detections to the scientific rules engine.
                        var ageGenderResult = await _encoderSvc.EstimateGenderAgeAsync(dictImage, result.Teeth);
                        result.EstimatedAgeRange = ageGenderResult.AgeRange;
                        result.EstimatedGender = ageGenderResult.Gender;
                        result.EstimatedAge = ageGenderResult.MedianAge;
                    }
                }

                // PostProcessing:

                // 9. Map pathologies → teeth
                _yoloParser.MapPathologiesToTeeth(result.RawTeeth, result.RawPathologies);

                // 10. Forensic checks
                _heuristicsService.ApplyChecks(result);

                // 11. AI intelligence analysis
                _intelligenceService.Analyze(result);
                _rulesEngine.ApplyRules(result);

                // 12. Biometric fingerprint
                result.Fingerprint = _biometricService.GenerateFingerprint(result.Teeth, result.Pathologies);
                if (result.FeatureVector != null)
                    result.Fingerprint.FeatureVector = result.FeatureVector;

            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inference failed");
            result.Error = $"Internal Engine Panic: {ex.Message}";
        }
        finally
        {
            if (lockAcquired)
            {
                _sessions.InferenceLock.Release();
            }
            if (ownsSeekableStream)
            {
                seekableStream.Dispose();
            }
        }

        sw.Stop();
        result.ProcessingTimeMs = sw.ElapsedMilliseconds;
        _logger.LogAudit("ANALYSIS_COMPLETE", "SYSTEM",
            $"Time: {result.ProcessingTimeMs}ms | Teeth: {result.Teeth.Count} | Pathologies: {result.Pathologies.Count}");

        if (cacheKey != null && string.IsNullOrEmpty(result.Error))
        {
            var copy = CloneAnalysisResult(result);
            copy.ProcessingTimeMs = result.ProcessingTimeMs;
            _cacheService.Set(cacheKey, copy);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiPipelineService — Individual Operations (thin lock wrappers)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<List<DetectedTooth>> DetectTeethAsync(Stream imageStream)
    {
        await EnsureInitializedAsync();

        if (!_sessions.IsReady || _sessions.TeethDetector == null)
            throw new InvalidOperationException("AI Engine not initialized or teeth detector not loaded.");
        if (imageStream == null)
            return [];

        var (seekableStream, ownsSeekableStream) = await PrepareSeekableStreamAsync(imageStream).ConfigureAwait(false);
        if (TryGetStreamLength(seekableStream) == 0)
        {
            if (ownsSeekableStream)
                seekableStream.Dispose();
            return [];
        }

        await _sessions.InferenceLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                TryResetStream(seekableStream);
                using var bitmap = SKBitmap.Decode(seekableStream);
                if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0) return new List<DetectedTooth>();
                return _teethSvc.DetectTeeth(bitmap);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Teeth detection failed");
            return [];
        }
        finally
        {
            _sessions.InferenceLock.Release();
            if (ownsSeekableStream)
            {
                seekableStream.Dispose();
            }
        }
    }

    public async Task<List<DetectedPathology>> DetectPathologiesAsync(Stream imageStream)
    {
        await EnsureInitializedAsync();

        if (!_sessions.IsReady || _sessions.PathologyDetector == null) return [];
        if (imageStream == null) return [];

        var (seekableStream, ownsSeekableStream) = await PrepareSeekableStreamAsync(imageStream).ConfigureAwait(false);
        if (TryGetStreamLength(seekableStream) == 0)
        {
            if (ownsSeekableStream)
                seekableStream.Dispose();
            return [];
        }

        await _sessions.InferenceLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                TryResetStream(seekableStream);
                using var bitmap = SKBitmap.Decode(seekableStream);
                if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0) return new List<DetectedPathology>();
                return _pathSvc.DetectPathologies(bitmap);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Pathology detection failed");
            return [];
        }
        finally
        {
            _sessions.InferenceLock.Release();
            if (ownsSeekableStream)
            {
                seekableStream.Dispose();
            }
        }
    }

    public async Task<(float[]? vector, string? error)> ExtractFeaturesAsync(Stream imageStream)
    {
        await EnsureInitializedAsync();

        if (!_sessions.IsReady) return (null, "AI Pipeline not initialized");
        if (imageStream == null) return (null, "Image stream is null");

        var (seekableStream, ownsSeekableStream) = await PrepareSeekableStreamAsync(imageStream).ConfigureAwait(false);
        if (TryGetStreamLength(seekableStream) == 0)
        {
            if (ownsSeekableStream)
                seekableStream.Dispose();
            return (null, "Image stream is empty");
        }

        await _sessions.InferenceLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                TryResetStream(seekableStream);
                using var bitmap = SKBitmap.Decode(seekableStream);
                if (bitmap == null) return (null, "Failed to decode image");
                return _encoderSvc.ExtractFeatures(bitmap);
            }).ConfigureAwait(false);
        }
        finally
        {
            _sessions.InferenceLock.Release();
            if (ownsSeekableStream)
            {
                seekableStream.Dispose();
            }
        }
    }

    private static async Task<(Stream stream, bool ownsStream)> PrepareSeekableStreamAsync(Stream source)
    {
        if (source.CanSeek)
        {
            TryResetStream(source);
            return (source, false);
        }

        var buffer = new MemoryStream();
        await source.CopyToAsync(buffer).ConfigureAwait(false);
        buffer.Position = 0;
        return (buffer, true);
    }

    private static bool TryResetStream(Stream stream)
    {
        if (!stream.CanSeek)
            return false;

        try
        {
            stream.Position = 0;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static long? TryGetStreamLength(Stream stream)
    {
        if (!stream.CanSeek)
            return null;

        try
        {
            return stream.Length;
        }
        catch
        {
            return null;
        }
    }

    private static AnalysisResult CloneAnalysisResult(AnalysisResult source)
    {
        var sourceTeeth = source.Teeth ?? new List<DetectedTooth>();
        var sourcePathologies = source.Pathologies ?? new List<DetectedPathology>();
        var sourceRawTeeth = source.RawTeeth ?? new List<DetectedTooth>();
        var sourceRawPathologies = source.RawPathologies ?? new List<DetectedPathology>();

        static DetectedTooth CloneTooth(DetectedTooth tooth) => new()
        {
            FdiNumber = tooth.FdiNumber,
            Confidence = tooth.Confidence,
            X = tooth.X,
            Y = tooth.Y,
            Width = tooth.Width,
            Height = tooth.Height,
            Outline = tooth.Outline?.Select(p => (p.X, p.Y)).ToList()
        };

        static DetectedPathology ClonePathology(DetectedPathology pathology) => new()
        {
            ClassName = pathology.ClassName,
            Confidence = pathology.Confidence,
            ToothNumber = pathology.ToothNumber,
            X = pathology.X,
            Y = pathology.Y,
            Width = pathology.Width,
            Height = pathology.Height,
            Outline = pathology.Outline?.Select(p => (p.X, p.Y)).ToList()
        };

        return new AnalysisResult
        {
            Teeth = sourceTeeth.Select(CloneTooth).ToList(),
            Pathologies = sourcePathologies.Select(ClonePathology).ToList(),
            RawTeeth = sourceRawTeeth.Select(CloneTooth).ToList(),
            RawPathologies = sourceRawPathologies.Select(ClonePathology).ToList(),
            EstimatedAge = source.EstimatedAge,
            EstimatedAgeRange = source.EstimatedAgeRange,
            EstimatedGender = source.EstimatedGender,
            FeatureVector = source.FeatureVector?.ToArray(),
            Fingerprint = source.Fingerprint == null ? null : new DentalFingerprint
            {
                Code = source.Fingerprint.Code,
                UniquenessScore = source.Fingerprint.UniquenessScore,
                ToothMap = source.Fingerprint.ToothMap?.ToDictionary(x => x.Key, x => x.Value) ?? new Dictionary<int, string>(),
                Features = source.Fingerprint.Features?.ToList() ?? new List<string>(),
                FeatureVector = source.Fingerprint.FeatureVector?.ToArray()
            },
            ProcessingTimeMs = source.ProcessingTimeMs,
            Error = source.Error,
            Flags = source.Flags?.ToList() ?? new List<string>(),
            SmartInsights = source.SmartInsights?.ToList() ?? new List<string>()
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Testability shim: kept for unit tests that discover it via reflection
    // ─────────────────────────────────────────────────────────────────────────

    // ReSharper disable once MemberCanBeMadeStatic.Local
    private float CalculateIoU(float x1, float y1, float w1, float h1,
                                float x2, float y2, float w2, float h2)
        => ForensicHeuristicsService.CalculateIoU(x1, y1, w1, h1, x2, y2, w2, h2);

    // ─────────────────────────────────────────────────────────────────────────
    // Disposal
    // ─────────────────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _recoveryLock.Dispose();
        _sessions.Dispose();
    }
}

