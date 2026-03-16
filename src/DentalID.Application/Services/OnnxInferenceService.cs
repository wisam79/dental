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
public sealed class OnnxInferenceService : IAiPipelineService, IBitmapAnalysisPipeline, IDisposable
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

    public async Task<AnalysisResult> AnalyzeImageAsync(Stream imageStream, string? fileName = null, CancellationToken ct = default)
    {
        if (imageStream == null)
            throw new ArgumentNullException(nameof(imageStream));

        await EnsureInitializedAsync();
        var (seekableStream, ownsSeekableStream) = await PrepareSeekableStreamAsync(imageStream).ConfigureAwait(false);

        try
        {
            // 1. Cache check
            string? cacheKey = null;
            if (_integrityService != null)
            {
                TryResetStream(seekableStream);
                var hash = _integrityService.ComputeHash(seekableStream);
                cacheKey = $"analysis_{hash}";
                if (_cacheService.Exists(cacheKey))
                {
                    var cached = _cacheService.Get<AnalysisResult>(cacheKey);
                    if (cached != null) return CloneAnalysisResult(cached);
                }
            }

            ct.ThrowIfCancellationRequested();

            // 2. Decode and Validate
            TryResetStream(seekableStream);
            using var bitmap = SKBitmap.Decode(seekableStream);
            if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                return new AnalysisResult { Error = "Image decoding failed." };
            }

            return await AnalyzeBitmapAsync(bitmap, fileName, ct).ConfigureAwait(false);
        }
        finally
        {
            if (ownsSeekableStream) seekableStream.Dispose();
        }
    }

    public async Task<AnalysisResult> AnalyzeBitmapAsync(SKBitmap bitmap, string? fileName = null, CancellationToken ct = default)
    {
        if (bitmap == null) throw new ArgumentNullException(nameof(bitmap));

        await EnsureInitializedAsync();
        ct.ThrowIfCancellationRequested();

        if (!_sessions.IsReady)
        {
            return new AnalysisResult { Error = "AI Engine is not initialized." };
        }

        var result = new AnalysisResult();
        var totalSw = Stopwatch.StartNew();
        
        // Bug Fix #1: Strict Serial Access to shared buffers
        await _sessions.InferenceLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _logger.LogAudit("ANALYSIS_START", "SYSTEM", $"Analyzing {bitmap.Width}x{bitmap.Height}");

            // Step 1: Sequential Detection (Safe for Shared Buffers)
            result.Teeth = _teethSvc.DetectTeeth(bitmap);
            ct.ThrowIfCancellationRequested();
            
            result.Pathologies = _pathSvc.DetectPathologies(bitmap);
            ct.ThrowIfCancellationRequested();

            result.RawTeeth = new List<DetectedTooth>(result.Teeth);
            result.RawPathologies = new List<DetectedPathology>(result.Pathologies);

            // Step 2: SAM Segmentation
            _samSvc.SegmentTeeth(bitmap, result.Teeth);
            _samSvc.SegmentPathologies(bitmap, result.Pathologies);
            ct.ThrowIfCancellationRequested();

            // Step 3: Rescue & TTA
            if (_teethSvc.ShouldApplyEdgeCropRescue(bitmap, result.RawTeeth))
            {
                _teethSvc.ApplyEdgeCropRescue(result, bitmap);
                result.Teeth = _teethSvc.BuildFinalTeeth(result.RawTeeth);
            }
            
            if (result.Teeth.Any())
                _teethSvc.ApplyTta(result, bitmap);

            // Step 4: Features
            if (_sessions.Encoder != null)
            {
                var (vector, err) = _encoderSvc.ExtractFeatures(bitmap, result.Teeth);
                result.FeatureVector = vector;
            }

            // Step 5: Post-processing
            _yoloParser.MapPathologiesToTeeth(result.RawTeeth, result.RawPathologies);
            _rulesEngine.ApplyRules(result);
            _intelligenceService.Analyze(result);

            result.Fingerprint = _biometricService.GenerateFingerprint(result.Teeth, result.Pathologies);
            if (result.FeatureVector != null) result.Fingerprint.FeatureVector = result.FeatureVector;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Inference panic");
            result.Error = ex.Message;
        }
        finally
        {
            _sessions.InferenceLock.Release();
        }

        totalSw.Stop();
        result.ProcessingTimeMs = totalSw.ElapsedMilliseconds;
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
                
                // BUG-01 Fix: Run teeth detection first so spatial features (bbox) are included in the encoding
                var teeth = _teethSvc.DetectTeeth(bitmap);
                return _encoderSvc.ExtractFeatures(bitmap, teeth);
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
            Outline = tooth.Outline?.Select(p => (p.X, p.Y)).ToList(),
            MaskWidth = tooth.MaskWidth,
            MaskHeight = tooth.MaskHeight,
            MaskArea = tooth.MaskArea
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
            Outline = pathology.Outline?.Select(p => (p.X, p.Y)).ToList(),
            MaskWidth = pathology.MaskWidth,
            MaskHeight = pathology.MaskHeight,
            MaskArea = pathology.MaskArea
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
            ProcessingTimeMs = 0,
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

