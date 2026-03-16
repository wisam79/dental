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
            // Spatial Localization for Non-Tooth Pathologies
            foreach (var orphan in orphans)
            {
                if (IsClass(orphan.ClassName, "bone loss") || IsClass(orphan.ClassName, "cyst"))
                {
                    string region = GetAnatomicalRegion(orphan, result.Teeth);
                    result.SmartInsights.Add($"Spatial Alert: {ToDisplayClassName(orphan.ClassName)} detected in {region}.");
                }
            }
            
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

            // Rule 2: Implant Supremacy Conflict (with confidence weighting)
            // If a tooth has a high-confidence "Implant", having "Caries", "RootCanal", or "Filling" is improbable.
            var implants = group.Where(p => IsImplantClass(p.ClassName)).OrderByDescending(p => p.Confidence).ToList();
            if (implants.Any())
            {
                float bestImplantConf = implants[0].Confidence;
                var conflicts = group
                    .Where(p => IsImplantConflictClass(p.ClassName))
                    .ToList();
                
                foreach (var conflict in conflicts)
                {
                    // Bug Fix: Only suppress if the implant is more reliable than the conflict,
                    // or if the implant is near-certain (> 85%).
                    if (bestImplantConf > conflict.Confidence || bestImplantConf > 0.85f)
                    {
                        result.Flags.Add($"Conflict (Tooth {toothNum}): Detected '{ToDisplayClassName(conflict.ClassName)}' suppressed on a tooth with an Implant (Confidence: {bestImplantConf:P0}).");
                        result.Pathologies.Remove(conflict);
                    }
                    else
                    {
                        result.Flags.Add($"Forensic Alert (Tooth {toothNum}): High-confidence conflict between Implant ({bestImplantConf:P0}) and {ToDisplayClassName(conflict.ClassName)} ({conflict.Confidence:P0}). Verify manually.");
                    }
                }
            }
            
            // Rule 3: Clinical Grouping (Crown + Root Canal)
            bool hasImplant = implants.Any();
            bool hasCrown = group.Any(p => IsClass(p.ClassName, "crown"));
            bool hasRct = group.Any(p => IsClass(p.ClassName, "root canal") || IsClass(p.ClassName, "rootcanal"));
            if (hasCrown && hasRct && !hasImplant)
            {
                // Group them semantically for smart insights / reports
                result.SmartInsights.Add($"Clinical Grouping (Tooth {toothNum}): Endodontically treated and crowned (Post-Core/Crown complex).");
                // We keep both in the GUI bounding boxes, but flag the association.
            }
            
            // Rule 3.5: Redundant Restorations
            bool hasFilling = group.Any(p => IsClass(p.ClassName, "filling"));
            if (hasCrown && hasFilling && !hasImplant)
            {
                result.Flags.Add($"Observation (Tooth {toothNum}): 'Filling' suppressed as redundant under full 'Crown'.");
                result.Pathologies.RemoveAll(p => p.ToothNumber == toothNum && IsClass(p.ClassName, "filling"));
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

        // Bug Fix: Sanity check for midY. If the gap-based approach produced an extreme value 
        // (outside 30-70% of image height), fall back to the vertical median of all teeth.
        if (midY < 0.30f || midY > 0.70f)
        {
            midY = result.Teeth.Average(t => t.Y + t.Height / 2f);
        }
        
        // 2. Separate Arches and Quadrants
        // Bug Fix #5: Dynamic Midline Detection
        // Use the average X of the central incisors or median of all teeth if centered poorly.
        float midX = 0.5f;
        var centralTeeth = result.Teeth.Where(t => (t.FdiNumber % 10) <= 2).ToList();
        if (centralTeeth.Count >= 2)
        {
            midX = centralTeeth.Average(t => t.X + t.Width / 2f);
        }
        else
        {
            var allCenters = result.Teeth.Select(t => t.X + t.Width / 2f).OrderBy(x => x).ToList();
            midX = allCenters[allCenters.Count / 2];
        }
        // Sanity clamp: midline shouldn't be at the very edges
        midX = Math.Clamp(midX, 0.35f, 0.65f);

        var q1 = result.Teeth.Where(t => (t.Y + t.Height / 2) < midY && (t.X + t.Width / 2) < midX).ToList();
        var q2 = result.Teeth.Where(t => (t.Y + t.Height / 2) < midY && (t.X + t.Width / 2) >= midX).ToList();
        var q4 = result.Teeth.Where(t => (t.Y + t.Height / 2) >= midY && (t.X + t.Width / 2) < midX).ToList();
        var q3 = result.Teeth.Where(t => (t.Y + t.Height / 2) >= midY && (t.X + t.Width / 2) >= midX).ToList();

        // Fix Q1 (1x): 18 -> 11 (descending FDI, increasing X towards midline)
        // Note: For Q1/Q4 (Right side), higher X means closer to midline. 
        // Our CorrectSequence assumes sorted by X (left to right).
        CorrectSequence(q1, 11, descendingFdi: true);
        
        // Fix Q2 (2x): 21 -> 28 (ascending FDI, increasing X away from midline)
        CorrectSequence(q2, 21, descendingFdi: false);

        // Fix Q4 (4x): 48 -> 41 (descending FDI, increasing X towards midline)
        CorrectSequence(q4, 41, descendingFdi: true);

        // Fix Q3 (3x): 31 -> 38 (ascending FDI, increasing X away from midline)
        CorrectSequence(q3, 31, descendingFdi: false);
    }

    private void CorrectSequence(List<DetectedTooth> quadrantTeeth, int startFdiBase, bool descendingFdi)
    {
        if (quadrantTeeth.Count < 2) return;

        // Sort detections by X (physical position)
        var sortedByPos = quadrantTeeth.OrderBy(t => t.X).ToList();
        float avgWidth = sortedByPos.Average(t => t.Width);

        int step = descendingFdi ? -1 : 1;

        int fdiMin = (startFdiBase / 10) * 10 + 1;
        int fdiMax = (startFdiBase / 10) * 10 + 8;

        if (sortedByPos[0].FdiNumber < fdiMin || sortedByPos[0].FdiNumber > fdiMax)
        {
            sortedByPos[0].FdiNumber = descendingFdi ? fdiMax : fdiMin;
        }

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
            
            int expectedFdi = prev.FdiNumber + step * (1 + gaps);
            
            // Bug #25 fix: Clamp FDI to valid anatomical range to prevent out-of-range values
            // FDI valid ranges per quadrant: 11-18, 21-28, 31-38, 41-48
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

    private string GetAnatomicalRegion(DetectedPathology p, List<DetectedTooth> teeth)
    {
        if (teeth == null || teeth.Count == 0) return "Unknown Region";
        
        float midY = teeth.Average(t => t.Y + t.Height / 2);
        bool isUpper = (p.Y + p.Height / 2) < midY;
        bool isRight = (p.X + p.Width / 2) < 0.5f; // Image Left is Patient Right
        
        string arch = isUpper ? "Maxillary" : "Mandibular";
        string side = isRight ? "Right" : "Left";
        
        return $"{arch} {side} Quadrant";
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
