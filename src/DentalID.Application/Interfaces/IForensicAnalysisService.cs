using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;

namespace DentalID.Application.Interfaces;

public interface IForensicAnalysisService
{
    Task<AnalysisResult> AnalyzeImageAsync(string imagePath, double sensitivity = 0.5);
    Task<DentalImage> SaveEvidenceAsync(string imagePath, AnalysisResult result, int subjectId);
    void UpdateForensicFilter(AnalysisResult result, double sensitivity);
}
