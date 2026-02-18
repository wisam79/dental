using System.Threading.Tasks;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

/// <summary>
/// Service responsible for generating forensic reports.
/// </summary>
public interface IReportService
{
    /// <summary>
    /// Generates a PDF report for a specific subject (Dental Profile).
    /// </summary>
    Task<byte[]> GenerateSubjectReportAsync(Subject subject);

    /// <summary>
    /// Generates a PDF report for a match result (Forensic Comparison).
    /// </summary>
    Task<byte[]> GenerateMatchReportAsync(Subject probe, MatchCandidate candidate);

    /// <summary>
    /// Generates a full case report.
    /// </summary>
    Task GenerateCaseReportAsync(Case forensicCase, string outputPath);

    /// <summary>
    /// Generates a forensic lab report from an analysis result.
    /// </summary>
    Task<byte[]> GenerateLabReportAsync(AnalysisResult result, Subject subject, string imagePath);
}
