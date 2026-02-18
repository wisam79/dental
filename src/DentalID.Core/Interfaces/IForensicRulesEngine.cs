using DentalID.Core.DTOs;

namespace DentalID.Core.Interfaces;

/// <summary>
/// Service that applies forensic logic rules to dental analysis results.
/// Validates biological and dental plausibility (e.g., Implants cannot have Caries).
/// </summary>
public interface IForensicRulesEngine
{
    /// <summary>
    /// Analyzes the raw detection results and adds flags/warnings for forensic anomalies.
    /// </summary>
    /// <param name="result">The analysis result to validate.</param>
    void ApplyRules(AnalysisResult result);
}
