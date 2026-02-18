using DentalID.Core.DTOs;

namespace DentalID.Application.Services;

/// <summary>
/// Spatial refinement of FDI tooth numbering using polar coordinate sorting.
/// Extracted from OnnxInferenceService to enable independent testing.
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
        
        int startIdx = (int)(sortedByY.Count * 0.2);
        int endIdx = (int)(sortedByY.Count * 0.8);

        for (int i = startIdx; i < endIdx - 1; i++)
        {
            float y1 = sortedByY[i].Y + sortedByY[i].Height / 2;
            float y2 = sortedByY[i+1].Y + sortedByY[i+1].Height / 2;
            float gap = y2 - y1;
            if (gap > maxGap) { maxGap = gap; midY = y1 + gap / 2; }
        }
        
        if (maxGap < 0.05f) 
        {
            float meanY = teeth.Average(t => t.Y + t.Height / 2);
            if (meanY < 0.5f) midY = 2.0f;
            else midY = -1.0f;
        }

        var upperArch = teeth.Where(t => (t.Y + t.Height / 2) < midY).ToList();
        var lowerArch = teeth.Where(t => (t.Y + t.Height / 2) >= midY).ToList();

        // 2. POLAR COORDINATE SORTING (Forensic Standard)
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

        // 3. ASSIGNMENT & GAP DETECTION
        AssignNumbersToSortedArch(upperArch, isUpper: true);
        AssignNumbersToSortedArch(lowerArch, isUpper: false);

        return upperArch.Concat(lowerArch).ToList();
    }

    private void AssignNumbersToSortedArch(List<DetectedTooth> arch, bool isUpper)
    {
        if (!arch.Any()) return;
        
        float centerX = 0.5f;
        var rightSide = arch.Where(t => (t.X + t.Width/2) < centerX).ToList();
        var leftSide = arch.Where(t => (t.X + t.Width/2) >= centerX).ToList();
        
        if (isUpper)
        {
            AssignSequence(rightSide, 18, -1);
            AssignSequence(leftSide, 21, 1);
        }
        else
        {
            AssignSequence(rightSide, 48, -1);
            AssignSequence(leftSide, 31, 1);
        }
    }

    private void AssignSequence(List<DetectedTooth> teeth, int startFdi, int step)
    {
        if (!teeth.Any()) return;
        
        int currentFdi = startFdi;
        float avgWidth = teeth.Average(t => t.Width);
        
        for (int i = 0; i < teeth.Count; i++)
        {
            if (i > 0)
            {
                float c1 = teeth[i-1].X + teeth[i-1].Width/2;
                float c2 = teeth[i].X + teeth[i].Width/2;
                float dist = Math.Abs(c2 - c1);
                
                if (dist > avgWidth * 3.0f) currentFdi += step * 2; // 2 missing teeth gap
                else if (dist > avgWidth * 2.0f) currentFdi += step; // 1 missing tooth gap
            }
            
            // Bug #14 Fix: The original check `currentFdi % 10 >= 1 && <= 8` was correct for units
            // but it ALSO needs to validate that the tens digit is 1-4 (FDI quadrants 1-4).
            // FDI valid teeth: 11-18, 21-28, 31-38, 41-48.
            // Without the tens check, values like 0, 10, 20, 30, 40 pass through silently or
            // the assignment counter drifts outside valid FDI space.
            int tens = currentFdi / 10;
            int units = currentFdi % 10;
            if (tens >= 1 && tens <= 4 && units >= 1 && units <= 8)
            {
                teeth[i].FdiNumber = currentFdi;
                currentFdi += step;
            }
            else
            {
                teeth[i].FdiNumber = 0;
            }
        }
    }
}
