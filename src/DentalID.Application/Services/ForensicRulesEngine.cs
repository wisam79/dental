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
            // If a tooth has an "Implant", having "Caries", "RootCanal", or "Filling" is medically highly improbable (or model error).
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
            
            // Rule 3: Multiple contradictory restorations
            bool hasCrown = group.Any(p => p.ClassName == "Crown");
            bool hasFilling = group.Any(p => p.ClassName == "Filling");
            if (hasCrown && hasFilling)
            {
                 // Note: A tooth can have a filling under a crown, but X-ray usually shows crown hiding it.
                 // We flag it as an observation.
                 result.Flags.Add($"Observation (Tooth {toothNum}): Both Crown and Filling detected."); 
            }
        }

        // Rule 4: Biological Geometric Constraints (The "Dentist Logic")
        ApplyGeometricConstraints(result);
    }

    private void ApplyGeometricConstraints(AnalysisResult result)
    {
        if (result.Teeth == null || result.Teeth.Count < 2) return;

        // 1. Sort by Y to roughly separate arches
        var sortedY = result.Teeth.OrderBy(t => t.Y + t.Height/2).ToList();
        float midY = sortedY[sortedY.Count / 2].Y + sortedY[sortedY.Count / 2].Height / 2;
        
        // 2. Separate Arches
        var upperArch = result.Teeth.Where(t => (t.Y + t.Height/2) < midY).ToList();
        var lowerArch = result.Teeth.Where(t => (t.Y + t.Height/2) >= midY).ToList();

        // 3. Normalize & Sort X
        // Upper: 18 -> 11 | 21 -> 28
        // Image X: 0 -----> Width
        // Order expected: 18, 17 ... 11, 21 ... 28
        
        // Q1 (Right Upper): 11..18. Should be Right->Left in FDI logic, but Left->Right in visual X?
        // FDI 18 is Patient Right (Image Left).
        // So Image Left -> Right: 18, 17, 16... 11, 21, 22... 28.
        
        // Fix Q1 (1x): X should be INCREASING as FDI DECREASES (18->11)
        CorrectSequence(upperArch.Where(t => t.FdiNumber >= 11 && t.FdiNumber <= 18).ToList(), 11, descendingFdi: true);
        
        // Fix Q2 (2x): X should be INCREASING as FDI INCREASES (21->28)
        CorrectSequence(upperArch.Where(t => t.FdiNumber >= 21 && t.FdiNumber <= 28).ToList(), 21, descendingFdi: false);

        // Lower: 48 -> 41 | 31 -> 38
        // Image Left -> Right: 48, 47... 41, 31... 38.
        
        // Fix Q4 (4x): X should be INCREASING as FDI DECREASES (48->41)
        CorrectSequence(lowerArch.Where(t => t.FdiNumber >= 41 && t.FdiNumber <= 48).ToList(), 41, descendingFdi: true);

        // Fix Q3 (3x): X should be INCREASING as FDI INCREASES (31->38)
        CorrectSequence(lowerArch.Where(t => t.FdiNumber >= 31 && t.FdiNumber <= 38).ToList(), 31, descendingFdi: false);
    }

    private void CorrectSequence(List<DetectedTooth> quadrantTeeth, int startFdiBase, bool descendingFdi)
    {
        if (quadrantTeeth.Count < 2) return;

        // Sort detections by X (physical position)
        var sortedByPos = quadrantTeeth.OrderBy(t => t.X).ToList();
        float avgWidth = sortedByPos.Average(t => t.Width);

        // Determine starting FDI for the Left-most tooth (smallest X)
        // If descending (18 -> 11), the first tooth (leftmost) is highest number (e.g. 17 or 18)
        // If ascending (21 -> 28), the first tooth (leftmost) is lowest number (e.g. 21)
        
        // We need an anchor. We can use the 'most confident' tooth as anchor, or just assume the sequence is roughly correct but check gaps.
        // Let's assume the user meant to fix gaps within the detected set.
        
        // If we simply reassign 11, 12, 13, we lose gap info.
        // We will traverse and assign.
        
        // 1. Identify the most confident tooth to use as anchor (optional, but robust)
        // For simplicity as per instruction "check for GAPS", we can just iterate.
        // But what is the starting number?
        // If we iterate 18->11 (descending), we need to know where we start.
        // Let's rely on the FIRST tooth's ID as a hint, or better, try to fit the sequence.
        
        // Strategy: Use the FIRST tooth in the sorted list as the seed, but validate it.
        // Or simpler: Just ensure the relative FDIs match the spacing.
        
        int currentFdi = sortedByPos[0].FdiNumber;
        int step = descendingFdi ? -1 : 1;

        for (int i = 1; i < sortedByPos.Count; i++)
        {
            var prev = sortedByPos[i - 1];
            var curr = sortedByPos[i];
            
            float dist = curr.X - prev.X;
            // Gap detection: Distance between centers roughly (or left edges)
            // Center distance is better.
            float c1 = prev.X + prev.Width / 2;
            float c2 = curr.X + curr.Width / 2;
            float centerDist = Math.Abs(c2 - c1);
            
            // Expected distance is roughly 1 width.
            // If distance > 1.6 * width, there is a gap (missing tooth).
            int gaps = 0;
            if (centerDist > avgWidth * 1.6f) gaps = 1;
            if (centerDist > avgWidth * 2.6f) gaps = 2; // Two missing teeth
            
            int expectedFdi = sortedByPos[i-1].FdiNumber + step * (1 + gaps);
            
            // If the current label is DIFFERENT from expected, we might need to correct it
            // trusting the spatial gap over the classification if they disagree?
            // "Found {11, 12, 14} -> Detects gap ... Labels remain {11, 12, 14}"
            // This means: If we found 11, 12, 14 (and 14 is spatially far), we KEEP it 14.
            // If we found 11, 12, 13 (but 13 is spatially far), we should probably RENAME it 14?
            
            // Let's implement the rename logic:
            // Let's implement the rename logic:
            if (curr.FdiNumber != expectedFdi)
            {
                // Only rename if the gap suggests a different number
                // Example: labeled 13, expected 14.
                curr.FdiNumber = expectedFdi;
            }
        }
    }
}
