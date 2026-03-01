namespace DentalID.Application.Configuration;

public class AiSettings
{
    public float ConfidenceThreshold { get; set; } = 0.5f;
    public float IouThreshold { get; set; } = 0.4f;
    /// <summary>
    /// Legacy GPU toggle. If true and provider is Auto, the service tries GPU providers before CPU fallback.
    /// </summary>
    public bool EnableGpu { get; set; } = false;
    /// <summary>
    /// Requested ONNX execution provider: Auto, CPU, DirectML, CUDA.
    /// </summary>
    public string ExecutionProvider { get; set; } = "Auto";
    /// <summary>
    /// Preferred GPU device id for provider APIs that accept a device index.
    /// </summary>
    public int PreferredGpuDeviceId { get; set; } = 0;
    /// <summary>
    /// If true, initialization fails when the requested GPU provider cannot be activated.
    /// </summary>
    public bool RequireGpu { get; set; } = false;
    /// <summary>
    /// Intra-op thread count (0 = ORT default/auto).
    /// </summary>
    public int IntraOpNumThreads { get; set; } = 0;
    /// <summary>
    /// Inter-op thread count (0 = ORT default/auto).
    /// </summary>
    public int InterOpNumThreads { get; set; } = 0;
    /// <summary>
    /// Enables ORT parallel execution mode.
    /// </summary>
    public bool EnableParallelExecution { get; set; } = true;
    /// <summary>
    /// Enables ORT CPU memory arena for allocation reuse.
    /// </summary>
    public bool EnableCpuMemArena { get; set; } = true;
    /// <summary>
    /// Enables ORT memory pattern optimization for repeated shapes.
    /// </summary>
    public bool EnableMemoryPattern { get; set; } = true;
    public bool EnableModelIntegrity { get; set; } = true;
    public bool AllowIntegrityBaselineCreation { get; set; } = true;
    public string ModelIntegrityManifestPath { get; set; } = "data/model_integrity.json";
    public string SealingKey { get; set; } = string.Empty;
}
