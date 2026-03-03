using DentalID.Core.DTOs;
using DentalID.Application.Interfaces;

namespace DentalID.Application.Services;

/// <summary>
/// Heuristic-based forensic checks for image manipulation and anomaly detection.
/// Extracted from OnnxInferenceService to enable independent testing.
/// </summary>
public class ForensicHeuristicsService : IForensicHeuristicsService
{
    // Bug #48 fix: Limit the O(n²) IoU loop to a reasonable tooth count
    private const int MaxTeethForIoUCheck = 80;

    public void ApplyChecks(AnalysisResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        int rawToothCount = result.RawTeeth?.Count ?? 0;
        var rawTeeth = (result.RawTeeth ?? new List<DetectedTooth>())
            .Where(t => t != null)
            .ToList();

        // Bug #49 fix: Adult permanent dentition max is 32; >32 is already anatomically impossible
        if (rawToothCount > 32)
        {
            result.Flags.Add($"Forensic Alert: Unusual tooth count detected (>{rawToothCount}). Possible image manipulation or composite.");
        }

        // 2. Anatomical Conflict Check: Bilateral Asymmetry
        AnalyzeBilateralAsymmetry(result, rawTeeth);

        // Bug #48 fix: Guard against O(n²) explosion for large detection sets
        if (rawToothCount > MaxTeethForIoUCheck)
        {
            result.Flags.Add($"Forensic Note: IoU overlap check skipped — too many detections ({rawToothCount} > {MaxTeethForIoUCheck}).");
            return;
        }

        // 3. Overlap Density Check — high overlap density suggests AI hallucinations
        int highOverlapCount = 0;
        for (int i = 0; i < rawTeeth.Count; i++)
        {
            for (int j = i + 1; j < rawTeeth.Count; j++)
            {
                // Bug #46 fix: adjusted IoU threshold for density overlap to 0.65 to reduce over-flagging
                if (CalculateIoU(
                    rawTeeth[i].X, rawTeeth[i].Y, rawTeeth[i].Width, rawTeeth[i].Height,
                    rawTeeth[j].X, rawTeeth[j].Y, rawTeeth[j].Width, rawTeeth[j].Height) > 0.65f)
                {
                    highOverlapCount++;
                }
            }
        }
        if (highOverlapCount > 3)
        {
            result.Flags.Add($"Forensic Alert: {highOverlapCount} high-density overlaps detected (IoU > 0.65). Possible AI artifacting zone.");
        }

        CheckSupernumerary(result, rawTeeth);
        CheckRetainedDeciduous(result, rawTeeth);
    }

    private void AnalyzeBilateralAsymmetry(AnalysisResult result, List<DetectedTooth> rawTeeth)
    {
        // Bug #47 fix: FdiNumber/10 == 2 matches FDI 20 (invalid tooth) and 21-29 (valid Q2)
        // Correct filter: quadrant 2 is FDI 21-28, quadrant 3 is FDI 31-38
        var leftCount = rawTeeth.Count(t =>
            t != null &&
            ((t.FdiNumber >= 21 && t.FdiNumber <= 28) ||
             (t.FdiNumber >= 31 && t.FdiNumber <= 38)));

        var rightCount = rawTeeth.Count(t =>
            t != null &&
            ((t.FdiNumber >= 11 && t.FdiNumber <= 18) ||
             (t.FdiNumber >= 41 && t.FdiNumber <= 48)));

        if (Math.Abs(leftCount - rightCount) > 8 && rawTeeth.Count > 10)
        {
            result.Flags.Add("Forensic Alert: Severe bilateral asymmetry detected. Verify image authenticity.");
        }
    }

    private void CheckSupernumerary(AnalysisResult result, List<DetectedTooth> rawTeeth)
    {
        // Check for more than 8 permanent teeth in any quadrant
        var quadrantCounts = rawTeeth
            .Where(t => t != null && t.FdiNumber >= 11 && t.FdiNumber <= 48)
            .GroupBy(t => t.FdiNumber / 10)
            .Select(g => new { 
                Quadrant = g.Key, 
                Count = g.Count(),
                HasWisdomTooth = g.Any(t => t.FdiNumber % 10 == 8)
            })
            .ToList();

        foreach (var qc in quadrantCounts)
        {
            // Skip alert if the count is exactly 9 but includes a wisdom tooth (common artifact zone at edges)
            if (qc.Count > 8 && !(qc.Count == 9 && qc.HasWisdomTooth))
            {
                result.Flags.Add($"Forensic Alert: Supernumerary teeth detected in Quadrant {qc.Quadrant} (Count: {qc.Count} > 8 max permanent).");
            }
        }
    }

    private void CheckRetainedDeciduous(AnalysisResult result, List<DetectedTooth> rawTeeth)
    {
        // Retained deciduous check: if a primary tooth exists alongside its permanent successor
        // Example: Primary 54 (1st molar) -> Permanent 14 (1st premolar)
        // FDI Mapping relation: Primary Q5 maps to Adult Q1, Primary Q6 -> Adult Q2, etc.
        var permanentFdis = rawTeeth.Where(t => t != null && t.FdiNumber >= 11 && t.FdiNumber <= 48).Select(t => t.FdiNumber).ToHashSet();
        var primaryFdis = rawTeeth.Where(t => t != null && t.FdiNumber >= 51 && t.FdiNumber <= 85).Select(t => t.FdiNumber).ToHashSet();

        foreach (var primary in primaryFdis)
        {
            // Calculate adult successor FDI
            int pQuad = primary / 10;
            int offset = primary % 10;
            
            // Map 5->1, 6->2, 7->3, 8->4
            int aQuad = pQuad - 4;
            int adultSuccessor = (aQuad * 10) + offset;
            
            if (permanentFdis.Contains(adultSuccessor))
            {
                result.Flags.Add($"Forensic Alert: Retained Deciduous tooth detected (Primary {primary} concurrent with Permanent {adultSuccessor}). High evidentiary value.");
            }
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
