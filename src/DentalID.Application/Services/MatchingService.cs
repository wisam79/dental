using System.Collections.Generic;
using System.Linq;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using System.Numerics;

namespace DentalID.Application.Services;

/// <summary>
/// Provides high-performance vector matching using SIMD acceleration via System.Numerics.Vectors.
/// This implementation uses hardware intrinsics (AVX/SSE) implicitly via the JIT compiler.
/// </summary>
public class MatchingService : IMatchingService
{
    public double CalculateCosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException($"Vector length mismatch: {vectorA.Length} vs {vectorB.Length}. Vectors must have the same length for cosine similarity calculation.");
        }

        if (vectorA.Length == 0)
        {
            return 0;
        }

        // Optimized SIMD implementation using Vector<T>
        int vectorSize = Vector<float>.Count;
        var dotProductVec = Vector<float>.Zero;
        var mag1Vec = Vector<float>.Zero;
        var mag2Vec = Vector<float>.Zero;

        int i = 0;
        // Process in chunks of Vector<float>.Count (usually 4 or 8 floats depending on hardware)
        for (; i <= vectorA.Length - vectorSize; i += vectorSize)
        {
            var va = new Vector<float>(vectorA.Slice(i));
            var vb = new Vector<float>(vectorB.Slice(i));

            dotProductVec += va * vb;
            mag1Vec += va * va;
            mag2Vec += vb * vb;
        }

        // Sum up the vector lanes
        float dot = Vector.Dot(dotProductVec, Vector<float>.One);
        float mag1 = Vector.Dot(mag1Vec, Vector<float>.One);
        float mag2 = Vector.Dot(mag2Vec, Vector<float>.One);

        // Process remaining elements sequentially
        for (; i < vectorA.Length; i++)
        {
            dot += vectorA[i] * vectorB[i];
            mag1 += vectorA[i] * vectorA[i];
            mag2 += vectorB[i] * vectorB[i];
        }

        if (mag1 == 0 || mag2 == 0) return 0;

        return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
    }

    private readonly IBiometricService _biometricService;

    // Optional: Constructor injection if not already present. 
    // Since MatchingService might be transient/singleton, we need to ensure IBiometricService is available.
    // If MatchingService didn't have a constructor before, we add one.
    public MatchingService(IBiometricService biometricService)
    {
        _biometricService = biometricService;
    }

    public List<MatchCandidate> FindMatches(DentalFingerprint probe, IEnumerable<Subject> candidates, MatchingCriteria? criteria = null)
    {
        var matches = new List<MatchCandidate>();

        // Pre-Filter Candidates (Optimization)
        var filteredCandidates = candidates;

        if (criteria != null)
        {
            filteredCandidates = filteredCandidates.Where(c => 
            {
                // Gender Filter (Allow Unknowns to pass if strictness low, but usually strict)
                if (!string.IsNullOrEmpty(criteria.Gender) && 
                    !string.Equals(c.Gender, criteria.Gender, StringComparison.OrdinalIgnoreCase) && 
                    !string.Equals(c.Gender, "Unknown", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                // Age Filter (Check if subject has DOB)
                if (c.DateOfBirth.HasValue)
                {
                    var today = DateTime.UtcNow;
                    var dob = c.DateOfBirth.Value;
                    var age = today.Year - dob.Year;
                    if (dob.Date > today.AddYears(-age)) age--; // Birthday hasn't occurred yet this year
                    if (criteria.MinAge.HasValue && age < criteria.MinAge.Value) return false;
                    if (criteria.MaxAge.HasValue && age > criteria.MaxAge.Value) return false;
                }
                
                return true;
            });
        }

        // Parallelize for larger datasets, but simple loop is fine for now
        foreach (var candidate in filteredCandidates)
        {
            // Optimization: Decode candidate vector once per candidate, not per image
            float[]? candidateVector = null;
            if (candidate.FeatureVector != null && candidate.FeatureVector.Length > 0)
            {
                candidateVector = new float[candidate.FeatureVector.Length / sizeof(float)];
                Buffer.BlockCopy(candidate.FeatureVector, 0, candidateVector, 0, candidate.FeatureVector.Length);
            }

            // Find the best matching image for this candidate
            double bestScore = 0;
            DentalImage? bestImage = null;

            foreach (var img in candidate.DentalImages)
            {
                if (string.IsNullOrEmpty(img.FingerprintCode) && img.FeatureVector == null && candidate.FeatureVector == null) continue;

                double score = 0;

                // 1. Prefer Direct Vector Matching (Subject Aggregate)
                if (probe.FeatureVector != null && candidateVector != null)
                {
                    score = CalculateCosineSimilarity(probe.FeatureVector, candidateVector);
                }
                // 2. Fallback to Code Matching (Legacy/Metadata)
                // Note: per-image FeatureVector path removed — DentalImage.FeatureVector is [NotMapped] and never hydrated from DB
                else if (!string.IsNullOrEmpty(img.FingerprintCode))
                {
                     var candidateFp = _biometricService.ParseFingerprintCode(img.FingerprintCode);
                     score = _biometricService.CalculateSimilarity(probe, candidateFp);
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestImage = img;
                }
            }
            
            // If no images but Subject has vector (Edge case), we should still considering matching?
            // Current validation loop iterates images. If candidate has no images but has vector (possible?), we skip.
            // But usually Subject has images if it has vector.

            if (bestScore > 0)
            {
                matches.Add(new MatchCandidate
                {
                    Subject = candidate,
                    Score = bestScore,
                    MatchMethod = "Biometric Fingerprint",
                    MatchDetails = new Dictionary<string, double>
                    {
                        { "Fingerprint Similarity", bestScore }
                    }
                });
            }
        }

        // Fallacy #6 Fix: Removed the hardcoded MinSimilarityThreshold constant (was 0.15).
        // This constant caused DOUBLE-FILTERING: MatchingService filtered at 0.15 AND the caller
        // (MatchingViewModel) also filtered at _aiConfig.Thresholds.MatchSimilarityThreshold.
        // The caller's configurable threshold is authoritative — return all scored candidates
        // and let the caller apply its threshold as configured.
        return matches.OrderByDescending(m => m.Score).ToList();
    }
}

