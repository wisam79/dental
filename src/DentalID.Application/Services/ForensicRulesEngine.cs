using DentalID.Core.Interfaces;
using DentalID.Core.DTOs;
using System.Linq;
using System;
using System.Collections.Generic;

namespace DentalID.Application.Services;

/// <summary>
/// Default implementation of Forensic Rules Engine.
/// </summary>
public class ForensicRulesEngine : IForensicRulesEngine
{
    private const float OrphanDedupIouThreshold = 0.35f;

    public void ApplyRules(AnalysisResult result)
    {
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        // Rule 1: Flag Orphans (Pathologies not linked to any tooth)
        if (result.Pathologies == null) return;
        var orphans = result.Pathologies
            .Where(p => p != null && (p.ToothNumber == null || p.ToothNumber == 0))
            .ToList();
        if (orphans.Any())
        {
            int uniqueOrphanRegions = EstimateUniquePathologyRegions(orphans);
            if (uniqueOrphanRegions < orphans.Count)
            {
                result.Flags.Add(
                    $"Warning: {uniqueOrphanRegions} unmapped pathology region(s) detected " +
                    $"({orphans.Count} raw detections collapsed). Check image manually.");
            }
            else
            {
                result.Flags.Add(
                    $"Warning: {uniqueOrphanRegions} pathology region(s) could not be mapped to a specific tooth. Check image manually.");
            }
        }

        // Group by Tooth for conflict analysis
        var pathologiesByTooth = result.Pathologies
            .Where(p => p.ToothNumber != null && p.ToothNumber != 0)
            .GroupBy(p => p.ToothNumber);

        foreach (var group in pathologiesByTooth)
        {
            int toothNum = group.Key!.Value;

            // Rule 2: Implant Supremacy Conflict
            // If a tooth has an "Implant", having "Caries", "RootCanal", or "Filling" is medically highly improbable.
            bool hasImplant = group.Any(p => IsImplantClass(p.ClassName));
            if (hasImplant)
            {
                var conflicts = group
                    .Where(p => IsImplantConflictClass(p.ClassName))
                    .GroupBy(p => ToDisplayClassName(p.ClassName))
                    .ToList();
                
                foreach (var conflict in conflicts)
                {
                    string className = conflict.Key;
                    int count = conflict.Count();
                    if (count > 1)
                    {
                        result.Flags.Add(
                            $"Conflict (Tooth {toothNum}): Detected '{className}' on a tooth with an Implant " +
                            $"({count} overlapping detections). Verify manually.");
                    }
                    else
                    {
                        result.Flags.Add(
                            $"Conflict (Tooth {toothNum}): Detected '{className}' on a tooth with an Implant. Verify manually.");
                    }
                }
            }
            
            // Rule 3: Crown + Filling on same tooth — clinically possible but worth noting.
            // Flagged as an "Observation" (not Conflict) to aid manual review without alarming.
            bool hasCrown = group.Any(p => IsClass(p.ClassName, "crown"));
            bool hasFilling = group.Any(p => IsClass(p.ClassName, "filling"));
            if (hasCrown && hasFilling && !hasImplant)
            {
                result.Flags.Add($"Observation (Tooth {toothNum}): Both 'Crown' and 'Filling' detected on the same tooth. Clinically possible — review recommended.");
            }
        }

        // Rule 4: Biological Geometric Constraints (The "Dentist Logic")
        ApplyGeometricConstraints(result);

        // Keep operator-facing alerts concise and de-duplicated.
        result.Flags = result.Flags
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.Ordinal)
            .Take(40)
            .ToList();
    }

    private void ApplyGeometricConstraints(AnalysisResult result)
    {
        // Bug #28 fix: Flag when insufficient teeth for geometric analysis
        if (result.Teeth == null || result.Teeth.Count < 2)
        {
            if (result.Teeth?.Count == 1)
                result.Flags.Add("Warning: Only 1 tooth detected — geometric FDI correction skipped.");
            return;
        }

        // Bug #27 fix: Use largest Y-gap for arch separation instead of simple median
        // This is more robust for partially edentulous patients
        var sortedByY = result.Teeth.OrderBy(t => t.Y + t.Height / 2).ToList();
        
        float midY;
        if (sortedByY.Count >= 4)
        {
            // Find the largest vertical gap between consecutive teeth to detect arch boundary
            float maxGap = 0;
            int splitIdx = sortedByY.Count / 2; // Default fallback
            for (int i = 1; i < sortedByY.Count; i++)
            {
                float prevCenter = sortedByY[i - 1].Y + sortedByY[i - 1].Height / 2f;
                float currCenter = sortedByY[i].Y + sortedByY[i].Height / 2f;
                float gap = currCenter - prevCenter;
                if (gap > maxGap) { maxGap = gap; splitIdx = i; }
            }
            // midY is the average of the two centers bracketing the largest gap
            float below = sortedByY[splitIdx - 1].Y + sortedByY[splitIdx - 1].Height / 2f;
            float above = sortedByY[splitIdx].Y + sortedByY[splitIdx].Height / 2f;
            midY = (below + above) / 2f;
        }
        else
        {
            // Fall back to simple median for small sets
            midY = sortedByY[sortedByY.Count / 2].Y + sortedByY[sortedByY.Count / 2].Height / 2f;
        }
        
        // 2. Separate Arches
        var upperArch = result.Teeth.Where(t => (t.Y + t.Height / 2) < midY).ToList();
        var lowerArch = result.Teeth.Where(t => (t.Y + t.Height / 2) >= midY).ToList();

        // Fix Q1 (1x): 18 -> 11 (descending FDI, increasing X)
        CorrectSequence(upperArch.Where(t => t.FdiNumber >= 11 && t.FdiNumber <= 18).ToList(), 11, descendingFdi: true);
        
        // Fix Q2 (2x): 21 -> 28 (ascending FDI, increasing X)
        CorrectSequence(upperArch.Where(t => t.FdiNumber >= 21 && t.FdiNumber <= 28).ToList(), 21, descendingFdi: false);

        // Fix Q4 (4x): 48 -> 41 (descending FDI, increasing X)
        CorrectSequence(lowerArch.Where(t => t.FdiNumber >= 41 && t.FdiNumber <= 48).ToList(), 41, descendingFdi: true);

        // Fix Q3 (3x): 31 -> 38 (ascending FDI, increasing X)
        CorrectSequence(lowerArch.Where(t => t.FdiNumber >= 31 && t.FdiNumber <= 38).ToList(), 31, descendingFdi: false);
    }

    private void CorrectSequence(List<DetectedTooth> quadrantTeeth, int startFdiBase, bool descendingFdi)
    {
        if (quadrantTeeth.Count < 2) return;

        // Sort detections by X (physical position)
        var sortedByPos = quadrantTeeth.OrderBy(t => t.X).ToList();
        float avgWidth = sortedByPos.Average(t => t.Width);

        int step = descendingFdi ? -1 : 1;

        // Bug #24 fix: Removed unused `currentFdi` variable
        for (int i = 1; i < sortedByPos.Count; i++)
        {
            var prev = sortedByPos[i - 1];
            var curr = sortedByPos[i];
            
            // Center-to-center distance for gap estimation
            float c1 = prev.X + prev.Width / 2;
            float c2 = curr.X + curr.Width / 2;
            float centerDist = Math.Abs(c2 - c1);
            
            // Bug #26 fix: Detect up to 3 consecutive missing teeth (not just 2)
            int gaps = 0;
            if (centerDist > avgWidth * 1.6f) gaps = 1;
            if (centerDist > avgWidth * 2.6f) gaps = 2;
            if (centerDist > avgWidth * 3.6f) gaps = 3;
            
            int expectedFdi = sortedByPos[i - 1].FdiNumber + step * (1 + gaps);
            
            // Bug #25 fix: Clamp FDI to valid anatomical range to prevent out-of-range values
            // FDI valid ranges per quadrant: 11-18, 21-28, 31-38, 41-48
            int fdiMin = (startFdiBase / 10) * 10 + 1;
            int fdiMax = (startFdiBase / 10) * 10 + 8;
            if (expectedFdi < fdiMin || expectedFdi > fdiMax)
                continue; // Skip correction — gap calculation overflowed the quadrant

            if (curr.FdiNumber != expectedFdi)
            {
                curr.FdiNumber = expectedFdi;
            }
        }
    }

    private static int EstimateUniquePathologyRegions(List<DetectedPathology> detections)
    {
        if (detections.Count == 0)
            return 0;

        int unique = 0;

        // Detections with invalid geometry cannot be spatially deduplicated.
        var invalidGeometry = detections.Where(d => !HasValidBox(d)).ToList();
        unique += invalidGeometry.Count;

        var validByClass = detections
            .Where(HasValidBox)
            .GroupBy(d => NormalizeClassName(d.ClassName));

        foreach (var classGroup in validByClass)
        {
            var sorted = classGroup.OrderByDescending(d => d.Confidence).ToList();
            var suppressed = new bool[sorted.Count];

            for (int i = 0; i < sorted.Count; i++)
            {
                if (suppressed[i])
                    continue;

                unique++;
                var current = sorted[i];

                for (int j = i + 1; j < sorted.Count; j++)
                {
                    if (suppressed[j])
                        continue;

                    var other = sorted[j];
                    float iou = ForensicHeuristicsService.CalculateIoU(
                        current.X, current.Y, current.Width, current.Height,
                        other.X, other.Y, other.Width, other.Height);
                    if (iou >= OrphanDedupIouThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }
        }

        return unique;
    }

    private static bool HasValidBox(DetectedPathology pathology)
    {
        return pathology.Width > 0 && pathology.Height > 0 &&
               pathology.Width <= 1 && pathology.Height <= 1 &&
               pathology.X >= 0 && pathology.Y >= 0 &&
               pathology.X <= 1 && pathology.Y <= 1;
    }

    private static bool IsImplantClass(string? className) => IsClass(className, "implant");

    private static bool IsImplantConflictClass(string? className)
    {
        var normalized = NormalizeClassName(className);
        return normalized.Contains("caries", StringComparison.Ordinal) ||
               normalized.Contains("filling", StringComparison.Ordinal) ||
               normalized.Contains("root piece", StringComparison.Ordinal) ||
               normalized.Contains("roots", StringComparison.Ordinal) ||
               normalized.Contains("root canal", StringComparison.Ordinal) ||
               normalized.Contains("rootcanal", StringComparison.Ordinal) ||
               normalized.Contains("root canal obturation", StringComparison.Ordinal);
    }

    private static bool IsClass(string? className, string normalizedTarget)
    {
        return string.Equals(NormalizeClassName(className), normalizedTarget, StringComparison.Ordinal);
    }

    private static string ToDisplayClassName(string? className)
    {
        var normalized = NormalizeClassName(className);
        return normalized switch
        {
            "rootcanal" => "Root Canal",
            "root canal obturation" => "Root Canal",
            "root piece" => "Root Piece",
            _ => string.IsNullOrWhiteSpace(className) ? "Unknown" : className.Trim()
        };
    }

    private static string NormalizeClassName(string? className)
    {
        return (className ?? string.Empty).Trim().ToLowerInvariant();
    }
}
