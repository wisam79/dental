using DentalID.Core.DTOs;

namespace DentalID.Application.Interfaces;

/// <summary>
/// Heuristic-based forensic checks for image manipulation and anomaly detection.
/// </summary>
public interface IForensicHeuristicsService
{
    /// <summary>
    /// Applies heuristic checks to detect potential image manipulation or anomalies.
    /// Adds flags to the result if anomalies are detected.
    /// </summary>
    void ApplyChecks(AnalysisResult result);
}
