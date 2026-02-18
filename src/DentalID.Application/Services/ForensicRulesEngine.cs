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
    public void ApplyRules(AnalysisResult result)
    {
        // Rule 1: Flag Orphans (Pathologies not linked to any tooth)
        if (result.Pathologies == null) return;
        var orphans = result.Pathologies.Where(p => p.ToothNumber == null || p.ToothNumber == 0).ToList();
        if (orphans.Any())
        {
            result.Flags.Add($"Warning: {orphans.Count} pathology detection(s) could not be mapped to a specific tooth. Check image manually.");
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
            bool hasImplant = group.Any(p => p.ClassName == "Implant");
            if (hasImplant)
            {
                var conflicts = group
                    .Where(p => p.ClassName == "Caries" || p.ClassName == "RootCanal" || p.ClassName == "Filling" || p.ClassName == "Root Piece" || p.ClassName == "Roots")
                    .ToList();
                
                foreach (var conflict in conflicts)
                {
                    result.Flags.Add($"Conflict (Tooth {toothNum}): Detected '{conflict.ClassName}' on a tooth with an Implant. Verify manually.");
                }
            }
            
            // Bug #29 fix: Rule 3 (Crown+Filling) removed — this is a common clinical scenario,
            // not a conflict. Flagging it generates forensic noise without actionable value.
        }

        // Rule 4: Biological Geometric Constraints (The "Dentist Logic")
        ApplyGeometricConstraints(result);
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
}
