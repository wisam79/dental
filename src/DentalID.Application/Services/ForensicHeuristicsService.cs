using DentalID.Core.DTOs;
using DentalID.Application.Interfaces;

namespace DentalID.Application.Services;

/// <summary>
/// Heuristic-based forensic checks for image manipulation and anomaly detection.
/// Extracted from OnnxInferenceService to enable independent testing.
/// </summary>
public class ForensicHeuristicsService : IForensicHeuristicsService
{
    public void ApplyChecks(AnalysisResult result)
    {
        // 1. Structural Anomaly Check: Impossible tooth counts
        if (result.RawTeeth.Count > 40)
        {
            result.Flags.Add("Forensic Alert: Unusual tooth count detected (>40). Possible image manipulation or composite.");
        }

        // 2. Anatomical Conflict Check: Bilateral Asymmetry
        AnalyzeBilateralAsymmetry(result);

        // 3. Overlap Density Check — high overlap density suggests AI hallucinations
        int highOverlapCount = 0;
        for (int i = 0; i < result.RawTeeth.Count; i++)
        {
            for (int j = i + 1; j < result.RawTeeth.Count; j++)
            {
                if (CalculateIoU(
                    result.RawTeeth[i].X, result.RawTeeth[i].Y, result.RawTeeth[i].Width, result.RawTeeth[i].Height,
                    result.RawTeeth[j].X, result.RawTeeth[j].Y, result.RawTeeth[j].Width, result.RawTeeth[j].Height) > 0.8f)
                {
                    highOverlapCount++;
                }
            }
        }
        if (highOverlapCount > 3)
        {
            result.Flags.Add($"Forensic Alert: {highOverlapCount} high-density overlaps detected. Possible AI artifacting.");
        }
    }

    private void AnalyzeBilateralAsymmetry(AnalysisResult result)
    {
        var leftCount = result.RawTeeth.Count(t => t.FdiNumber / 10 == 2 || t.FdiNumber / 10 == 3);
        var rightCount = result.RawTeeth.Count(t => t.FdiNumber / 10 == 1 || t.FdiNumber / 10 == 4);

        if (Math.Abs(leftCount - rightCount) > 6 && result.RawTeeth.Count > 10)
        {
            result.Flags.Add("Forensic Alert: Severe bilateral asymmetry detected. Verify image authenticity.");
        }
    }

    public static float CalculateIoU(float x1, float y1, float w1, float h1, float x2, float y2, float w2, float h2)
    {
        float xOverlap = Math.Max(0, Math.Min(x1 + w1, x2 + w2) - Math.Max(x1, x2));
        float yOverlap = Math.Max(0, Math.Min(y1 + h1, y2 + h2) - Math.Max(y1, y2));
        float intersection = xOverlap * yOverlap;
        float union = w1 * h1 + w2 * h2 - intersection;
        return union > 0 ? intersection / union : 0;
    }
}
