using DentalID.Core.DTOs;

namespace DentalID.Core.Interfaces;

/// <summary>
/// Service responsible for comparing two dental analysis results.
/// </summary>
public interface IComparisonService
{
    /// <summary>
    /// Compares two analysis results and returns the comparison metrics and differences.
    /// </summary>
    ComparisonResult CompareAnalyses(AnalysisResult image1, AnalysisResult image2);
}
