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
            result.Flags.Add($"Forensic Alert: Unusual tooth count detected (32 max permanent vs {rawToothCount} detected). Possible image manipulation or mixed dentition.");
        }

        // Detect Duplicate FDIs (Conflict where same tooth is in two non-overlapping places)
        DetectDuplicateFdis(result, rawTeeth);
        // ...

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

    private void DetectDuplicateFdis(AnalysisResult result, List<DetectedTooth> rawTeeth)
    {
        var groups = rawTeeth
            .Where(t => t.FdiNumber > 0)
            .GroupBy(t => t.FdiNumber)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var g in groups)
        {
            var sorted = g.OrderByDescending(t => t.Confidence).ToList();
            var primary = sorted[0];
            
            for (int i = 1; i < sorted.Count; i++)
            {
                var secondary = sorted[i];
                float iou = CalculateIoU(primary.X, primary.Y, primary.Width, primary.Height,
                                         secondary.X, secondary.Y, secondary.Width, secondary.Height);
                
                // If they don't overlap significantly, they are physically distinct boxes 
                // claiming to be the same tooth — a critical forensic conflict.
                if (iou < 0.20f)
                {
                    result.Flags.Add($"CRITICAL CONFLICT: Multiple distinct locations detected for FDI {g.Key}. Possible AI classification failure or image duplication.");
                    break;
                }
            }
        }
    }

    private void AnalyzeBilateralAsymmetry(AnalysisResult result, List<DetectedTooth> rawTeeth)
    {
        if (rawTeeth == null || rawTeeth.Count < 4) return;

        // Check for tooth-by-tooth correspondence across the midline (1x vs 2x, 4x vs 3x)
        var toothMap = rawTeeth.Where(t => t != null).GroupBy(t => t.FdiNumber).ToDictionary(g => g.Key, g => g.First());
        
        // Permanent teeth units 1-8
        for (int unit = 1; unit <= 8; unit++)
        {
            CheckSymmetry(10 + unit, 20 + unit, toothMap, result);
            CheckSymmetry(40 + unit, 30 + unit, toothMap, result);
        }

        // Keep the macro-level check for severe asymmetry
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

    private void CheckSymmetry(int fdi1, int fdi2, Dictionary<int, DetectedTooth> map, AnalysisResult result)
    {
        bool has1 = map.ContainsKey(fdi1);
        bool has2 = map.ContainsKey(fdi2);

        if (has1 != has2)
        {
            // Significant asymmetry: tooth present on one side but not the other
            // Only flag if it's a "reliable" tooth (molars/canines) and not marked as missing in a hypothetical pathology list
            int unit = fdi1 % 10;
            if (unit >= 6 || unit == 3) // Canines (3) and Molars (6,7,8)
            {
                int missingFdi = has1 ? fdi2 : fdi1;
                result.SmartInsights.Add($"Anatomical Alert: Bilateral asymmetry detected at unit {unit}. Tooth {missingFdi} is absent while its counterpart exists.");
            }
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
                Count = g.Select(t => t.FdiNumber).Distinct().Count(),
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
