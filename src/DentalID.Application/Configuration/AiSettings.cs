namespace DentalID.Application.Configuration;

public class AiSettings
{
    public float ConfidenceThreshold { get; set; } = 0.5f;
    public float IouThreshold { get; set; } = 0.4f;
    /// <summary>
    /// GPU acceleration is not yet implemented. Set to true only after CUDA/DirectML support is added.
    /// Default is false to prevent misleading behavior.
    /// </summary>
    public bool EnableGpu { get; set; } = false;
    public string SealingKey { get; set; } = string.Empty;
}
