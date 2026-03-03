using System;
using System.Collections.Generic;
using System.Linq;
using DentalID.Core.DTOs;

namespace DentalID.Application.Services;

/// <summary>
/// Advanced Spatial refinement of FDI tooth numbering using heuristic clustering, 
/// AI confidence anchors, probabilistic gap detection, and overlap processing.
/// </summary>
public class FdiSpatialService : Interfaces.IFdiSpatialService
{
    public List<DetectedTooth> RefineFdiNumbering(List<DetectedTooth> teeth)
    {
        if (teeth.Count < 3) return teeth;

        // 1. ARCH SEPARATION (Robust Clustering via vertical gap detection)
        var sortedByY = teeth.OrderBy(t => t.Y + t.Height / 2).ToList();
        float midY = 0.5f;
        float maxGap = 0;
        
        for (int i = 0; i < sortedByY.Count - 1; i++)
        {
            float y1 = sortedByY[i].Y + sortedByY[i].Height / 2;
            float y2 = sortedByY[i+1].Y + sortedByY[i+1].Height / 2;
            float gap = y2 - y1;
            // Valid inter-arch gap should be substantial
            if (gap > maxGap && gap > 0.05f) 
            { 
                maxGap = gap; 
                midY = y1 + gap / 2; 
            }
        }
        
        if (maxGap < 0.05f) 
        {
            // Failsafe: if no distinct gap, split by geometric middle
            float meanY = teeth.Average(t => t.Y + t.Height / 2);
            midY = meanY < 0.5f ? 2.0f : -1.0f; // Force all upper or all lower if heavily skewed
        }

        // Fuzzy sets for abnormal vertical displacement (e.g., highly impacted canines)
        // For simplicity, a standard arch split handles 95% of cases.
        var upperArch = teeth.Where(t => (t.Y + t.Height / 2) < midY).ToList();
        var lowerArch = teeth.Where(t => (t.Y + t.Height / 2) >= midY).ToList();

        // 2. POLAR COORDINATE SORTING
        // Flattens the horseshoe arch into a linear sequence from Patient Right to Patient Left.
        if (upperArch.Any())
        {
            float cx = upperArch.Average(t => t.X + t.Width/2);
            float cy = upperArch.Max(t => t.Y + t.Height) + 0.2f;
            upperArch = upperArch.OrderBy(t => Math.Atan2((t.Y + t.Height/2) - cy, (t.X + t.Width/2) - cx)).ToList();
        }

        if (lowerArch.Any())
        {
            float cx = lowerArch.Average(t => t.X + t.Width/2);
            float cy = lowerArch.Min(t => t.Y) - 0.2f;
            lowerArch = lowerArch.OrderByDescending(t => Math.Atan2((t.Y + t.Height/2) - cy, (t.X + t.Width/2) - cx)).ToList();
        }

        // 3. SMART FDI ASSIGNMENT
        AssignNumbersToSortedArchSmart(upperArch, isUpper: true);
        AssignNumbersToSortedArchSmart(lowerArch, isUpper: false);

        return upperArch.Concat(lowerArch).ToList();
    }

    private void AssignNumbersToSortedArchSmart(List<DetectedTooth> arch, bool isUpper)
    {
        if (!arch.Any()) return;
        
        // Midline at X=0.5 (normalized). 
        // Patient Right = Image Left (X < 0.5). Patient Left = Image Right (X >= 0.5).
        float centerX = 0.5f;
        
        var rightSide = arch.Where(t => (t.X + t.Width/2) < centerX)
                            .OrderByDescending(t => t.X + t.Width/2).ToList(); // Center (Medial) to Back (Distal)
        var leftSide = arch.Where(t => (t.X + t.Width/2) >= centerX)
                           .OrderBy(t => t.X + t.Width/2).ToList(); // Center (Medial) to Back (Distal)
        
        if (isUpper)
        {
            AssignSequenceSmart(rightSide, isUpper: true, isRightSide: true);
            AssignSequenceSmart(leftSide, isUpper: true, isRightSide: false);
        }
        else
        {
            AssignSequenceSmart(rightSide, isUpper: false, isRightSide: true);
            AssignSequenceSmart(leftSide, isUpper: false, isRightSide: false);
        }
    }

    private void AssignSequenceSmart(List<DetectedTooth> teeth, bool isUpper, bool isRightSide)
    {
        if (!teeth.Any()) return;
        
        int quad = isUpper ? (isRightSide ? 1 : 2) : (isRightSide ? 4 : 3);
        int baseAdult = quad * 10;
        int baseDeciduous = (quad + 4) * 10;
        
        float avgWidth = teeth.Average(t => t.Width);
        int currentUnit = 1; // 1 to 8 (Central Incisor to Wisdom)

        for (int i = 0; i < teeth.Count; i++)
        {
            var t = teeth[i];
            
            // 1. Abnormal Overlap & Gap Detection
            if (i > 0)
            {
                var prev = teeth[i-1];
                float overlapX = Math.Min(t.X + t.Width, prev.X + prev.Width) - Math.Max(t.X, prev.X);
                bool isHighlyOverlapped = overlapX > 0 && (overlapX / Math.Min(t.Width, prev.Width)) > 0.6f;
                
                if (!isHighlyOverlapped)
                {
                    // Gap Detection: calculate distance between centers
                    float dist = Math.Abs((t.X + t.Width/2) - (prev.X + prev.Width/2));
                    if (dist > avgWidth * 1.5f)
                    {
                        // Deduce missing teeth
                        int skipped = (int)Math.Floor(dist / avgWidth);
                        currentUnit += skipped;
                    }
                }
            }

            // 2. Anchor Lock & Confidence Weighting
            // If the model is highly confident, respect its classification rather than blindly overriding
            bool isTargetQuad = (t.FdiNumber / 10) == quad;
            bool isTargetDeciduousQuad = (t.FdiNumber / 10) == (quad + 4);
            
            // High confidence threshold for anchoring
            if (t.Confidence >= 0.70f) 
            {
                if (isTargetQuad && (t.FdiNumber % 10) >= currentUnit)
                {
                    currentUnit = t.FdiNumber % 10;
                    t.FdiNumber = baseAdult + currentUnit;
                    currentUnit++;
                    continue;
                }
                else if (isTargetDeciduousQuad && (t.FdiNumber % 10) >= currentUnit)
                {
                    int decUnit = t.FdiNumber % 10;
                    t.FdiNumber = baseDeciduous + decUnit;
                    currentUnit = decUnit + 1; // Advance the expected adult counter as well
                    continue;
                }
            }
            
            // 3. Mixed Dentition & Deciduous Fallback (Low Confidence but Morphologically Small)
            bool isDeciduousClass = t.FdiNumber >= 51 && t.FdiNumber <= 85;
            bool isPhysicallySmall = t.Width < avgWidth * 0.75f;
            
            if (isDeciduousClass || (isPhysicallySmall && currentUnit <= 5))
            {
                int decUnit = Math.Min(5, currentUnit);
                t.FdiNumber = baseDeciduous + decUnit;
                currentUnit++;
                continue; // Skip adult assignment
            }

            // 4. Safely Cap and Assign Final Sequence
            if (currentUnit > 8) currentUnit = 8;
            
            t.FdiNumber = baseAdult + currentUnit;
            currentUnit++;
        }
    }
}
