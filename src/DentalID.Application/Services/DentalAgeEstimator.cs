using System.Collections.Generic;
using System.Linq;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;

namespace DentalID.Application.Services;

/// <summary>
/// A heuristic-based rules engine for scientifically estimating chronological age
/// chronologically based on erupted and calcified teeth detected in a dental panoramic X-Ray.
/// Replaces the inaccurate facial recognition model for X-Rays.
/// </summary>
public static class DentalAgeEstimator
{
    private static readonly int[] WisdomTeeth = [18, 28, 38, 48];
    private static readonly int[] SecondMolars = [17, 27, 37, 47];
    private static readonly int[] Canines = [13, 23, 33, 43];
    private static readonly int[] FirstPremolars = [14, 24, 34, 44];
    private static readonly int[] SecondPremolars = [15, 25, 35, 45];
    
    // Deciduous (Primary) teeth quadrants 50, 60, 70, 80
    
    public static (string Range, int? MedianAge) EstimateAgeRange(IEnumerable<DetectedTooth> detections)
    {
        var fdiNumbers = detections
            .Select(d => d.FdiNumber)
            .Where(fdi => fdi is > 10 and < 90)
            .ToHashSet();

        if (fdiNumbers.Count == 0)
        {
            return ("Unknown (Insufficient Data)", null);
        }

        bool hasDeciduous = fdiNumbers.Any(fdi => fdi >= 50 && fdi <= 85);
        
        // Late Adulthood check (Wisdom teeth fully present)
        bool hasWisdomTeeth = WisdomTeeth.Any(w => fdiNumbers.Contains(w));
        // All four wisdom teeth means definitely older adulthood
        bool allWisdomTeeth = WisdomTeeth.All(w => fdiNumbers.Contains(w));
        
        // Middle adolescence check
        bool hasAllSecondMolars = SecondMolars.All(m => fdiNumbers.Contains(m));
        
        bool hasCanines = Canines.Any(c => fdiNumbers.Contains(c));
        bool hasPremolars = FirstPremolars.Any(p => fdiNumbers.Contains(p)) || SecondPremolars.Any(p => fdiNumbers.Contains(p));


        if (hasDeciduous)
        {
            if (fdiNumbers.Any(fdi => fdi is > 10 and < 50))
            {
                // Mixed dentition
                return ("6 - 12 Years (Mixed Dentition)", 9);
            }
            // Pure deciduous
            return ("Under 6 Years (Primary Dentition)", 5);
        }

        if (allWisdomTeeth)
        {
            return ("Over 21 Years (Full Adult Dentition)", 25);
        }

        if (hasWisdomTeeth && hasAllSecondMolars)
        {
            return ("18 - 21 Years (Late Adolescence / Early Adulthood)", 20);
        }

        if (hasAllSecondMolars)
        {
            return ("12 - 15 Years (Early Adolescence)", 14);
        }

        if (hasCanines && hasPremolars)
        {
             return ("9 - 12 Years (Late Childhood)", 11);
        }

        // Default or undetermined adulthood without wisdom teeth (often extracted or impacted and not detected)
        // If there are no deciduous teeth and typical adult teeth exist in large numbers.
        if (fdiNumbers.Count >= 24)
            return ("Over 18 Years (Assumed Adult)", 25);

        return ("Unknown (Complex/Atypical)", null);
    }
}
