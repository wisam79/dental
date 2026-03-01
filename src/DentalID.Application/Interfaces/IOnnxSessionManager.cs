using Microsoft.ML.OnnxRuntime;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Manages the lifecycle of all ONNX InferenceSessions and the shared inference lock.
/// Extracted from OnnxInferenceService to follow Single Responsibility Principle.
/// </summary>
public interface IOnnxSessionManager : IDisposable
{
    /// <summary>Whether all required sessions are loaded and ready.</summary>
    bool IsReady { get; }

    /// <summary>Loads all ONNX model files and runs a self-diagnostic warmup.</summary>
    Task InitializeAsync(string modelsDirectory);

    // ── Sessions (null if model file not present) ───────────────────────────
    InferenceSession? TeethDetector { get; }
    InferenceSession? PathologyDetector { get; }
    InferenceSession? Encoder { get; }
    InferenceSession? GenderAge { get; }

    // ── Input node names ────────────────────────────────────────────────────
    string TeethInputName { get; }
    string PathologyInputName { get; }
    string EncoderInputName { get; }
    string GenderAgeInputName { get; }

    // ── LOH buffers (pre-allocated to avoid GC pressure) ───────────────────
    float[]? DetectionBuffer { get; }
    float[]? TtaDetectionBuffer { get; }
    float[]? EncoderBuffer { get; }

    /// <summary>
    /// Shared semaphore. All sub-services MUST acquire this before running any session.
    /// </summary>
    SemaphoreSlim InferenceLock { get; }
}
