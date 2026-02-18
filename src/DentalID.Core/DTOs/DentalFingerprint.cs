using System.Collections.Generic;

namespace DentalID.Core.DTOs;

/// <summary>
/// Represents the unique "Dental DNA" of a subject based on their dental map.
/// This is used for biometric matching logic.
/// </summary>
public class DentalFingerprint
{
    /// <summary>
    /// The unique string representation of the dental map.
    /// Format: "18:M-17:F-16:C..."
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// A score representing how "unique" this fingerprint is.
    /// Higher score means easier identification (e.g., Implants > Fillings).
    /// </summary>
    public double UniquenessScore { get; set; }

    /// <summary>
    /// Structured map of tooth number to condition code.
    /// Key: FDI Number, Value: Condition Code (I, C, F, M, R, P, H)
    /// </summary>
    public Dictionary<int, string> ToothMap { get; set; } = new();

    /// <summary>
    /// List of "High Value Features" found (e.g., "Implant at #36").
    /// Used for explaining the match to the user.
    /// </summary>
    /// <summary>
    /// List of "High Value Features" found (e.g., "Implant at #36").
    /// Used for explaining the match to the user.
    /// </summary>
    public List<string> Features { get; set; } = new();

    /// <summary>
    /// AI-extracted feature vector for dense matching.
    /// </summary>
    public float[]? FeatureVector { get; set; }
}
