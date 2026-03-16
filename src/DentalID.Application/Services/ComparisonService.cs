using System;
using System.Collections.Generic;
using System.Linq;
using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;

namespace DentalID.Application.Services;

public class ComparisonService : IComparisonService
{
    private readonly IMatchingService _matchingService;

    public ComparisonService(IMatchingService matchingService)
    {
        _matchingService = matchingService ?? throw new ArgumentNullException(nameof(matchingService));
    }

    public ComparisonResult CompareAnalyses(AnalysisResult image1, AnalysisResult image2)
    {
        var result = new ComparisonResult
        {
            TotalTeethImage1 = image1.Teeth?.Count ?? 0,
            TotalTeethImage2 = image2.Teeth?.Count ?? 0
        };

        var teeth1Dict = image1.Teeth?.GroupBy(t => t.FdiNumber).ToDictionary(g => g.Key, g => g.First()) ?? new();
        var teeth2Dict = image2.Teeth?.GroupBy(t => t.FdiNumber).ToDictionary(g => g.Key, g => g.First()) ?? new();

        var path1Dict = image1.Pathologies?.GroupBy(p => p.ToothNumber ?? 0).ToDictionary(g => g.Key, g => g.Select(p => p.ClassName).ToList()) ?? new();
        var path2Dict = image2.Pathologies?.GroupBy(p => p.ToothNumber ?? 0).ToDictionary(g => g.Key, g => g.Select(p => p.ClassName).ToList()) ?? new();

        var allToothNumbers = teeth1Dict.Keys.Union(teeth2Dict.Keys)
            .Union(path1Dict.Keys.Where(k => k != 0))
            .Union(path2Dict.Keys.Where(k => k != 0))
            .Distinct()
            .ToList();

        int conditionMatchCount = 0;

        foreach (var fdi in allToothNumbers)
        {
            var inImg1 = teeth1Dict.TryGetValue(fdi, out var t1);
            var inImg2 = teeth2Dict.TryGetValue(fdi, out var t2);

            if (inImg1 && inImg2)
            {
                result.MatchedTeethCount++;

                // Compare conditions (pathologies)
                var p1 = path1Dict.GetValueOrDefault(fdi, new List<string>());
                var p2 = path2Dict.GetValueOrDefault(fdi, new List<string>());

                bool conditionMatches = p1.Count == p2.Count && !p1.Except(p2).Any() && !p2.Except(p1).Any();
                if (conditionMatches)
                {
                    conditionMatchCount++;
                }
                else
                {
                    var added = p1.Except(p2).ToList();
                    var removed = p2.Except(p1).ToList();
                    
                    if (!result.ConditionDifferences.ContainsKey(fdi)) 
                        result.ConditionDifferences[fdi] = new List<string>();

                    foreach (var a in added) result.ConditionDifferences[fdi].Add($"Pathology '{a}' found in Evidence but not Record.");
                    foreach (var r in removed) result.ConditionDifferences[fdi].Add($"Pathology '{r}' found in Record but not Evidence.");
                }
            }
            else
            {
                result.DifferentTeethCount++;
                if (inImg1 && !inImg2)
                    result.Differences.Add($"Tooth {fdi} present in Evidence, missing in Record.");
                else if (!inImg1 && inImg2)
                    result.Differences.Add($"Tooth {fdi} missing in Evidence, present in Record.");
            }
        }

        double totalTeethCompared = result.MatchedTeethCount + result.DifferentTeethCount;
        result.SimilarityScore = totalTeethCompared > 0 ? (double)result.MatchedTeethCount / totalTeethCompared : 0;
        result.ConditionMatchScore = result.MatchedTeethCount > 0 ? (double)conditionMatchCount / result.MatchedTeethCount : 0;

        // Vector Similarity
        bool hasVectors = image1.FeatureVector != null && image1.FeatureVector.Length > 0 &&
                          image2.FeatureVector != null && image2.FeatureVector.Length > 0;
        
        if (hasVectors)
        {
            result.VectorSimilarityScore = _matchingService.CalculateCosineSimilarity(image1.FeatureVector, image2.FeatureVector);
        }

        // Combined Score: 50% Vector, 30% Condition, 20% Presence
        // Only if vectors are available to not break legacy checks
        if (hasVectors)
        {
            // Floor cosine sim at 0 for combined score calculation
            double vectorSim = Math.Max(0, result.VectorSimilarityScore); 
            result.CombinedForensicScore = (vectorSim * 0.5) + (result.ConditionMatchScore * 0.3) + (result.SimilarityScore * 0.2);
        }
        else
        {
            result.CombinedForensicScore = (result.ConditionMatchScore * 0.6) + (result.SimilarityScore * 0.4);
        }

        // General pathologies (not attached to a specific tooth)
        var genP1 = path1Dict.GetValueOrDefault(0, new List<string>());
        var genP2 = path2Dict.GetValueOrDefault(0, new List<string>());
        var genAdded = genP1.Except(genP2).ToList();
        var genRemoved = genP2.Except(genP1).ToList();
        foreach (var a in genAdded) result.Differences.Add($"[General] Pathology '{a}' found in Evidence but not Record.");
        foreach (var r in genRemoved) result.Differences.Add($"[General] Pathology '{r}' found in Record but not Evidence.");

        return result;
    }
}
