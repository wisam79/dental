using System.Reflection;
using DentalID.Application.Configuration;
using DentalID.Application.Interfaces;
using DentalID.Core.Interfaces;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SkiaSharp;

namespace DentalID.Application.Services;

/// <summary>
/// Manages lifecycle of all ONNX InferenceSession objects.
/// Extracted from OnnxInferenceService to follow Single Responsibility Principle.
/// </summary>
public sealed class OnnxSessionManager : IOnnxSessionManager
{
    private const string ProviderAuto    = "AUTO";
    private const string ProviderCpu     = "CPU";
    private const string ProviderDirectMl = "DIRECTML";
    private const string ProviderCuda   = "CUDA";

    private readonly AiConfiguration _config;
    private readonly AiSettings      _aiSettings;
    private readonly ILoggerService  _logger;

    // ── Sessions ──────────────────────────────────────────────────────────────
    public InferenceSession? TeethDetector     { get; private set; }
    public InferenceSession? PathologyDetector { get; private set; }
    public InferenceSession? Encoder           { get; private set; }
    public InferenceSession? GenderAge         { get; private set; }

    // ── Input node names ──────────────────────────────────────────────────────
    public string TeethInputName      { get; private set; } = string.Empty;
    public string PathologyInputName  { get; private set; } = string.Empty;
    public string EncoderInputName    { get; private set; } = string.Empty;
    public string GenderAgeInputName  { get; private set; } = string.Empty;

    // ── LOH Buffers ───────────────────────────────────────────────────────────
    public float[]? DetectionBuffer    { get; private set; }
    public float[]? TtaDetectionBuffer { get; private set; }
    public float[]? EncoderBuffer      { get; private set; }

    /// <summary>Shared semaphore — all sub-services must acquire before running inference.</summary>
    public SemaphoreSlim InferenceLock { get; } = new(1, 1);

    private volatile bool _isReady;
    public bool IsReady => _isReady;

    public OnnxSessionManager(AiConfiguration config, AiSettings aiSettings, ILoggerService logger)
    {
        _config     = config     ?? throw new ArgumentNullException(nameof(config));
        _aiSettings = aiSettings ?? throw new ArgumentNullException(nameof(aiSettings));
        _logger     = logger     ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation($"[SessionManager] Created Instance {this.GetHashCode()}");
    }

    // ── Initialization ────────────────────────────────────────────────────────

    public async Task InitializeAsync(string modelsDirectory)
    {
        _logger.LogInformation($"[SessionManager] InitializeAsync called on Instance {this.GetHashCode()}");
        
        await InferenceLock.WaitAsync();
        try
        {
            _logger.LogInformation($"[SessionManager] Loading models from: {modelsDirectory}");
            _isReady = false;
            DisposeSessions();

            using var opts = CreateSessionOptions(out var executionProvider);

            var teethPath    = Path.Combine(modelsDirectory, "teeth_detect.onnx");
            var pathPath     = Path.Combine(modelsDirectory, "pathology_detect.onnx");
            var encoderPath  = Path.Combine(modelsDirectory, "encoder.onnx");
            var genderAgePath = Path.Combine(modelsDirectory, "genderage.onnx");

            _logger.LogInformation($"teeth:     {DescribeFile(teethPath)}");
            _logger.LogInformation($"pathology: {DescribeFile(pathPath)}");
            _logger.LogInformation($"encoder:   {DescribeFile(encoderPath)}");
            _logger.LogInformation($"genderage: {DescribeFile(genderAgePath)}");

            if (!File.Exists(teethPath))   throw new FileNotFoundException("Critical model missing", teethPath);
            if (!File.Exists(pathPath))    throw new FileNotFoundException("Critical model missing", pathPath);
            if (!File.Exists(encoderPath)) throw new FileNotFoundException("Critical model missing", encoderPath);

            TeethDetector     = new InferenceSession(teethPath,   opts);
            PathologyDetector = new InferenceSession(pathPath,    opts);
            Encoder           = new InferenceSession(encoderPath, opts);
            if (File.Exists(genderAgePath))
                GenderAge = new InferenceSession(genderAgePath, opts);

            TeethInputName     = ResolveInputName(TeethDetector,     "teeth_detect.onnx");
            PathologyInputName = ResolveInputName(PathologyDetector, "pathology_detect.onnx");
            EncoderInputName   = ResolveInputName(Encoder,           "encoder.onnx");
            GenderAgeInputName = GenderAge == null ? string.Empty : ResolveInputName(GenderAge, "genderage.onnx");

            // Pre-allocate LOH buffers
            int detSize = _config.Model.DetectionInputSize * _config.Model.DetectionInputSize * 3;
            DetectionBuffer    = new float[detSize];
            TtaDetectionBuffer = new float[detSize];
            EncoderBuffer      = new float[1024 * 1024 * 3];

            // Warmup / Self-Diagnostic
            RunSelfDiagnostic();

            _isReady = true;
            _logger.LogInformation($"[SessionManager] Ready. Provider: {executionProvider}. Instance {this.GetHashCode()} is now READY.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"[SessionManager] Initialization failed on Instance {this.GetHashCode()}");
            DisposeSessions();
            throw;
        }
        finally
        {
            InferenceLock.Release();
        }
    }

    // ── Session Options / Provider ────────────────────────────────────────────

    private SessionOptions CreateSessionOptions(out string provider)
    {
        var opts = new SessionOptions
        {
            GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            ExecutionMode = _aiSettings.EnableParallelExecution
                ? ExecutionMode.ORT_PARALLEL
                : ExecutionMode.ORT_SEQUENTIAL,
            EnableCpuMemArena    = _aiSettings.EnableCpuMemArena,
            EnableMemoryPattern  = _aiSettings.EnableMemoryPattern
        };
        if (_aiSettings.IntraOpNumThreads > 0) opts.IntraOpNumThreads = _aiSettings.IntraOpNumThreads;
        if (_aiSettings.InterOpNumThreads > 0) opts.InterOpNumThreads = _aiSettings.InterOpNumThreads;
        provider = ConfigureExecutionProvider(opts);
        return opts;
    }

    private string ConfigureExecutionProvider(SessionOptions opts)
    {
        var requested = NormalizeName(_aiSettings.ExecutionProvider);
        if (requested == ProviderCpu) return ProviderCpu;

        if (requested == ProviderDirectMl || requested == ProviderCuda)
        {
            if (TryEnableProvider(opts, requested)) return requested;
            if (_aiSettings.RequireGpu)
                throw new InvalidOperationException($"Provider '{requested}' not available.");
            _logger.LogWarning($"Provider '{requested}' unavailable, falling back to CPU.");
            return ProviderCpu;
        }

        if (requested != ProviderAuto)
            _logger.LogWarning($"Unknown provider '{_aiSettings.ExecutionProvider}', using Auto.");

        bool gpuWanted = _aiSettings.EnableGpu || requested == ProviderDirectMl || requested == ProviderCuda;
        if (!gpuWanted) return ProviderCpu;

        if (TryEnableProvider(opts, ProviderDirectMl)) return ProviderDirectMl;
        if (TryEnableProvider(opts, ProviderCuda))     return ProviderCuda;

        if (_aiSettings.RequireGpu)
            throw new InvalidOperationException("GPU required but no GPU provider available.");
        _logger.LogWarning("GPU wanted but no GPU provider available, falling back to CPU.");
        return ProviderCpu;
    }

    private bool TryEnableProvider(SessionOptions opts, string provider)
    {
        string[] names = provider switch
        {
            ProviderDirectMl => ["AppendExecutionProvider_DML", "AppendExecutionProvider_DirectML"],
            ProviderCuda     => ["AppendExecutionProvider_CUDA", "AppendExecutionProvider_Cuda"],
            _                => []
        };
        return names.Any(n => TryInvokeMethod(opts, n, _aiSettings.PreferredGpuDeviceId));
    }

    private bool TryInvokeMethod(SessionOptions opts, string methodName, int deviceId)
    {
        var methods = typeof(SessionOptions)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.Ordinal))
            .OrderBy(m => m.GetParameters().Length);

        foreach (var m in methods)
        {
            try
            {
                var p = m.GetParameters();
                if (p.Length == 0) { m.Invoke(opts, null); return true; }
                if (p.Length == 1 && p[0].ParameterType == typeof(int))
                { m.Invoke(opts, [deviceId]); return true; }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Provider activation {methodName} failed: {ex.Message}");
            }
        }
        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string NormalizeName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? ProviderAuto : name.Trim().ToUpperInvariant();

    private static string ResolveInputName(InferenceSession session, string modelFile)
    {
        var name = session.InputNames.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Model '{modelFile}' has no input node.");
        return name;
    }

    private static string DescribeFile(string path) =>
        File.Exists(path) ? $"{path} [{new FileInfo(path).Length / 1024 / 1024} MB]" : $"{path} [missing]";

    private void RunSelfDiagnostic()
    {
        try
        {
            using var bmp = new SKBitmap(640, 640);
            using (var canvas = new SKCanvas(bmp)) canvas.Clear(SKColors.Black);

            // Inline minimal tensor for warmup (avoids circular dep on ITensorPreparationService)
            int size = _config.Model.DetectionInputSize;
            var data = new DenseTensor<float>(new[] { 1, 3, size, size });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(TeethInputName, data)
            };
            using var _ = TeethDetector?.Run(inputs);
            _logger.LogInformation("[SessionManager] Self-diagnostic passed.");
        }
        catch (Exception ex)
        {
            throw new Exception($"Self-diagnostic failed: {ex.Message}", ex);
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    private void DisposeSessions()
    {
        TeethDetector?.Dispose();     TeethDetector     = null;
        PathologyDetector?.Dispose(); PathologyDetector = null;
        Encoder?.Dispose();           Encoder           = null;
        GenderAge?.Dispose();         GenderAge         = null;
        TeethInputName = PathologyInputName = EncoderInputName = GenderAgeInputName = string.Empty;
    }

    public void Dispose()
    {
        DisposeSessions();
        InferenceLock.Dispose();
    }
}
