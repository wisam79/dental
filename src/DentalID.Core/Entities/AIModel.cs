namespace DentalID.Core.Entities;

/// <summary>
/// Represents an AI model registered in the system.
/// </summary>
public class AIModel : BaseEntity
{
    // Id inherited
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? Parameters { get; set; }
    public double? Accuracy { get; set; }
    public int? ProcessingTimeMs { get; set; }
    public DateTime? LastUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
