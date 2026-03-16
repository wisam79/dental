using System.Collections.Generic;

namespace DentalID.Core.DTOs;

public class ComparisonResult
{
    public double SimilarityScore { get; set; } // Legacy or general match score based on presence
    public int TotalTeethImage1 { get; set; }
    public int TotalTeethImage2 { get; set; }
    public int MatchedTeethCount { get; set; }
    public int DifferentTeethCount { get; set; }
    public List<string> Differences { get; set; } = new();
    
    // New Advanced Metrics
    public double ConditionMatchScore { get; set; }
    public double VectorSimilarityScore { get; set; }
    public double CombinedForensicScore { get; set; }
    public Dictionary<int, List<string>> ConditionDifferences { get; set; } = new();
}
