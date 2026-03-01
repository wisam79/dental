using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DentalID.Core.DTOs;
using DentalID.Core.Interfaces;

namespace DentalID.Application.Services;

/// <summary>
/// Service for biometric dental analysis and fingerprint generation
/// </summary>
public class BiometricService : IBiometricService
{
    // Weights for biometric significance
    private const int WeightImplant = 100;
    private const int WeightBridge = 80;
    private const int WeightCrown = 50;
    private const int WeightRootCanal = 40;
    private const int WeightFilling = 20;
    private const int WeightMissing = 10;
    private const int WeightCaries = 5;

    /// <summary>
    /// Generates a unique dental fingerprint from detected teeth and pathologies
    /// </summary>
    /// <param name="teeth">List of detected teeth</param>
    /// <param name="pathologies">List of detected pathologies</param>
    /// <returns>Dental fingerprint with unique code and uniqueness score</returns>
    public DentalFingerprint GenerateFingerprint(List<DetectedTooth> teeth, List<DetectedPathology> pathologies)
    {
        var fingerprint = new DentalFingerprint();
        var sb = new StringBuilder();
        double score = 0;
        
        // Create a map of all 32 teeth (focusing on permanent dentition for simplicity)
        // We will scan FDI 11-18, 21-28, 31-38, 41-48
        var allFdi = new List<int>();
        allFdi.AddRange(Enumerable.Range(11, 8)); // 11-18
        allFdi.AddRange(Enumerable.Range(21, 8)); // 21-28
        allFdi.AddRange(Enumerable.Range(31, 8)); // 31-38
        allFdi.AddRange(Enumerable.Range(41, 8)); // 41-48
        allFdi.Sort();

        var toothMap = new Dictionary<int, string>();
        pathologies ??= new List<DetectedPathology>();
        teeth ??= new List<DetectedTooth>();

        foreach (var fdi in allFdi)
        {
            // Find pathologies for this tooth
            var toothPathologies = pathologies.Where(p => p.ToothNumber == fdi).ToList();
            
            // Determine code
            string code = "U"; // Default Unknown/Undetected
            
            // Check for various dental conditions (priority order)
            var tooth = teeth.FirstOrDefault(t => t.FdiNumber == fdi);
            
            if (tooth != null)
            {
                code = "H"; // Detected, assume healthy unless pathology found

                if (toothPathologies.Any())
                {
                    // Check for high-value dental work
                    if (toothPathologies.Any(p => p.ClassName == "Implant"))
                    {
                        code = "I"; // Implant
                        score += WeightImplant;
                    }
                    else if (toothPathologies.Any(p => p.ClassName == "Bridge"))
                    {
                        code = "B"; // Bridge
                        score += WeightBridge;
                    }
                    else if (toothPathologies.Any(p => p.ClassName == "Crown"))
                    {
                        code = "C"; // Crown
                        score += WeightCrown;
                    }
                    else if (toothPathologies.Any(p => p.ClassName == "Root canal obturation"))
                    {
                        code = "R"; // Root canal
                        score += WeightRootCanal;
                    }
                    else if (toothPathologies.Any(p => p.ClassName == "Filling"))
                    {
                        code = "F"; // Filling
                        score += WeightFilling;
                    }
                    else if (toothPathologies.Any(p => p.ClassName == "Caries"))
                    {
                        code = "K"; // Caries (from German "Karies")
                        score += WeightCaries;
                    }
                }
            }
            
            toothMap[fdi] = code;
            sb.Append($"{fdi}:{code}-");
        }

        fingerprint.Code = sb.ToString().TrimEnd('-');
        // Score is weighted sum; cap at 100 to represent max forensic uniqueness.
        // A single high-value feature (e.g. implant = 100) already saturates the scale.
        fingerprint.UniquenessScore = Math.Min(score, 100);
        fingerprint.ToothMap = toothMap;

        return fingerprint;
    }

    /// <summary>
    /// Parses a dental fingerprint code back into components
    /// </summary>
    /// <param name="code">Fingerprint code string</param>
    /// <returns>Dental fingerprint object</returns>
    public DentalFingerprint ParseFingerprintCode(string code)
    {
        var fingerprint = new DentalFingerprint();
        
        if (string.IsNullOrEmpty(code))
        {
            return fingerprint;
        }

        fingerprint.Code = code;
        
        var toothMap = new Dictionary<int, string>();
        double score = 0;

        var parts = code.Split('-', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var toothParts = part.Split(':');
            if (toothParts.Length == 2 && int.TryParse(toothParts[0], out int fdi))
            {
                var toothCode = toothParts[1];
                toothMap[fdi] = toothCode;
                
                // Add to uniqueness score
                score += toothCode switch
                {
                    "I" => WeightImplant,
                    "B" => WeightBridge,
                    "C" => WeightCrown,
                    "R" => WeightRootCanal,
                    "F" => WeightFilling,
                    "M" => WeightMissing,
                    "K" => WeightCaries,
                    _ => 0
                };
            }
        }

        fingerprint.ToothMap = toothMap;
        fingerprint.UniquenessScore = Math.Min(score, 100);

        return fingerprint;
    }

    /// <summary>
    /// Calculates similarity between two dental fingerprints using cosine similarity
    /// </summary>
    /// <param name="fingerprint1">First dental fingerprint</param>
    /// <param name="fingerprint2">Second dental fingerprint</param>
    /// <returns>Similarity score between 0 and 1</returns>
    public double CalculateSimilarity(DentalFingerprint fingerprint1, DentalFingerprint fingerprint2)
    {
        if (fingerprint1?.ToothMap == null || fingerprint2?.ToothMap == null)
        {
            return 0;
        }

        // Get common FDI numbers where NEITHER is Unknown
        var commonFdi = fingerprint1.ToothMap.Keys
            .Intersect(fingerprint2.ToothMap.Keys)
            .Where(k => fingerprint1.ToothMap[k] != "U" && fingerprint2.ToothMap[k] != "U")
            .ToList();

        if (commonFdi.Count == 0)
        {
            return 0;
        }

        // Create vectors
        var vector1 = new double[commonFdi.Count];
        var vector2 = new double[commonFdi.Count];

        for (int i = 0; i < commonFdi.Count; i++)
        {
            vector1[i] = GetCodeValue(fingerprint1.ToothMap[commonFdi[i]]);
            vector2[i] = GetCodeValue(fingerprint2.ToothMap[commonFdi[i]]);
        }

        // Calculate cosine similarity
        return CalculateCosineSimilarity(vector1, vector2);
    }

    /// <summary>
/// Gets numeric value for tooth code.
/// Bug #13 fix: H (Healthy) gets a midpoint value so healthy teeth
/// still contribute to cosine similarity differentiation.
/// </summary>
private static double GetCodeValue(string code)
{
    return code switch
    {
        "I" => 10,  // Implant - highest forensic value
        "B" => 8,   // Bridge
        "C" => 6,   // Crown
        "R" => 5,   // Root canal
        // Fallacy #2 Fix: Missing teeth (M) are forensically MORE distinctive than Healthy (H).
        // In dental identification, a missing tooth narrows the suspect pool significantly —
        // it's an irreversible condition that creates a unique dental signature.
        // A healthy tooth is the most common state and contributes LEAST to individualization.
        // The original ordering (H=4, M=2) was backwards from forensic best practice.
        "M" => 5,   // Missing — same tier as Root canal (highly distinctive, irreversible)
        "F" => 3,   // Filling
        "H" => 2,   // Healthy — low value (extremely common in population)
        "K" => 1,   // Caries
        "U" => 0,   // Unknown - filtered out in similarity calculation
        _ => 0
    };
}

    /// <summary>
    /// Calculates cosine similarity between two vectors
    /// </summary>
    private static double CalculateCosineSimilarity(double[] vector1, double[] vector2)
    {
        if (vector1.Length != vector2.Length || vector1.Length == 0)
        {
            return 0;
        }

        double dotProduct = 0;
        double norm1 = 0;
        double norm2 = 0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            norm1 += vector1[i] * vector1[i];
            norm2 += vector2[i] * vector2[i];
        }

        if (norm1 == 0 || norm2 == 0)
        {
            return 0;
        }

        return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
    }
}
