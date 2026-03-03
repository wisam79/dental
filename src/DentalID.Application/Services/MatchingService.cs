using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DentalID.Application.Configuration;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;
using DentalID.Core.Interfaces;
using System.Numerics;

namespace DentalID.Application.Services;

/// <summary>
/// Provides high-performance vector matching using SIMD acceleration via System.Numerics.Vectors.
/// Supports parallel candidate evaluation for large databases.
/// </summary>
public class MatchingService : IMatchingService
{
    private const int CenteredSimilarityMinCandidates = 3;
    private const double CenteredScoreFloor = 0.50;
    private const double CenteredScoreGamma = 1.4;
    private const double MinCenteringVariancePerDimension = 1e-8;

    private readonly IBiometricService _biometricService;
    private readonly double _vectorScoreFloor;
    private readonly double _vectorScoreGamma;

    private sealed record VectorCenteringContext(float[] Centroid);

    public MatchingService(IBiometricService biometricService, AiConfiguration? config = null)
    {
        _biometricService = biometricService;
        _vectorScoreFloor = Math.Clamp(config?.Thresholds.MatchCalibrationFloor ?? 0.78f, 0.0f, 0.98f);
        _vectorScoreGamma = Math.Clamp(config?.Thresholds.MatchCalibrationGamma ?? 1.8f, 0.5f, 4.0f);
    }

    /// <summary>
    /// Calculates cosine similarity between two float vectors using SIMD hardware intrinsics.
    /// </summary>
    public double CalculateCosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {
        if (vectorA.Length != vectorB.Length)
        {
            throw new ArgumentException(
                $"Vector length mismatch: {vectorA.Length} vs {vectorB.Length}. " +
                "Vectors must have the same length for cosine similarity calculation.");
        }

        if (vectorA.Length == 0) return 0;

        // Spatial Hybrid Matching (1184 length = 1024 Deep Features + 160 Spatial Features)
        if (vectorA.Length == 1184)
        {
            double deepSim = ComputeRawCosineSimilarity(vectorA.Slice(0, 1024), vectorB.Slice(0, 1024));
            double spatialSim = ComputeRawCosineSimilarity(vectorA.Slice(1024, 160), vectorB.Slice(1024, 160));
            
            // Blend: 75% Deep Visual Identity + 25% Precise Geographic Match
            // If the spatial similarity is very low, it drags down the score significantly.
            return (deepSim * 0.75) + (Math.Max(0, spatialSim) * 0.25);
        }

        return ComputeRawCosineSimilarity(vectorA, vectorB);
    }

    private static double ComputeRawCosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB)
    {

        // SIMD-accelerated path using Vector<T> (AVX/SSE via JIT)
        int vectorSize = Vector<float>.Count;
        var dotProductVec = Vector<float>.Zero;
        var mag1Vec = Vector<float>.Zero;
        var mag2Vec = Vector<float>.Zero;

        int i = 0;
        for (; i <= vectorA.Length - vectorSize; i += vectorSize)
        {
            var va = new Vector<float>(vectorA.Slice(i));
            var vb = new Vector<float>(vectorB.Slice(i));
            dotProductVec += va * vb;
            mag1Vec += va * va;
            mag2Vec += vb * vb;
        }

        float dot  = Vector.Dot(dotProductVec, Vector<float>.One);
        float mag1 = Vector.Dot(mag1Vec, Vector<float>.One);
        float mag2 = Vector.Dot(mag2Vec, Vector<float>.One);

        // Scalar tail for remaining elements
        for (; i < vectorA.Length; i++)
        {
            dot  += vectorA[i] * vectorB[i];
            mag1 += vectorA[i] * vectorA[i];
            mag2 += vectorB[i] * vectorB[i];
        }

        if (mag1 == 0 || mag2 == 0) return 0;

        return dot / (Math.Sqrt(mag1) * Math.Sqrt(mag2));
    }

    /// <summary>
    /// Finds matching candidates for a probe fingerprint from a set of subjects.
    /// Uses parallel evaluation when the candidate list is large (>50 subjects).
    /// </summary>
    public List<MatchCandidate> FindMatches(
        DentalFingerprint probe,
        IEnumerable<Subject> candidates,
        MatchingCriteria? criteria = null)
    {
        var filteredCandidates = ApplyPreFilter(candidates, criteria).ToList();
        var candidateVectors = BuildCandidateVectors(filteredCandidates, probe.FeatureVector);
        var centeringContext = BuildCenteringContext(probe.FeatureVector, candidateVectors.Values.ToList());

        // Choose serial vs parallel based on dataset size
        var results = filteredCandidates.Count > 50
            ? FindMatchesParallel(probe, filteredCandidates, candidateVectors, centeringContext)
            : FindMatchesSerial(probe, filteredCandidates, candidateVectors, centeringContext);

        // Return all scored candidates — let the caller apply its configured threshold.
        // (Double-filtering was Bug Fallacy #6: MatchingService had a hardcoded 0.15 threshold
        //  that conflicted with the caller's configurable threshold.)
        return results
            .Where(m => m.Score > 0)
            .OrderByDescending(m => m.Score)
            .ToList();
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private static IEnumerable<Subject> ApplyPreFilter(
        IEnumerable<Subject> candidates,
        MatchingCriteria? criteria)
    {
        if (criteria == null) return candidates;

        var today = DateTime.UtcNow;

        return candidates.Where(c =>
        {
            // Gender filter (allow Unknowns to pass through)
            if (!string.IsNullOrEmpty(criteria.Gender) &&
                !string.Equals(c.Gender, criteria.Gender, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(c.Gender, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            // Age filter
            if (c.DateOfBirth.HasValue)
            {
                var age = today.Year - c.DateOfBirth.Value.Year;
                if (c.DateOfBirth.Value.Date > today.AddYears(-age)) age--;

                if (criteria.MinAge.HasValue && age < criteria.MinAge.Value) return false;
                if (criteria.MaxAge.HasValue && age > criteria.MaxAge.Value) return false;
            }

            return true;
        });
    }

    private List<MatchCandidate> FindMatchesSerial(
        DentalFingerprint probe,
        List<Subject> candidates,
        IReadOnlyDictionary<Subject, float[]> candidateVectors,
        VectorCenteringContext? centeringContext)
    {
        var results = new List<MatchCandidate>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var match = EvaluateCandidate(probe, candidate, candidateVectors, centeringContext);
            if (match != null) results.Add(match);
        }
        return results;
    }

    private List<MatchCandidate> FindMatchesParallel(
        DentalFingerprint probe,
        List<Subject> candidates,
        IReadOnlyDictionary<Subject, float[]> candidateVectors,
        VectorCenteringContext? centeringContext)
    {
        var bag = new ConcurrentBag<MatchCandidate>();

        Parallel.ForEach(candidates,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            candidate =>
            {
                var match = EvaluateCandidate(probe, candidate, candidateVectors, centeringContext);
                if (match != null) bag.Add(match);
            });

        return bag.ToList();
    }

    /// <summary>
    /// Evaluates a single candidate against the probe and returns a MatchCandidate if scored > 0.
    /// </summary>
    private MatchCandidate? EvaluateCandidate(
        DentalFingerprint probe,
        Subject candidate,
        IReadOnlyDictionary<Subject, float[]> candidateVectors,
        VectorCenteringContext? centeringContext)
    {
        candidateVectors.TryGetValue(candidate, out var candidateVector);

        double bestScore = 0;
        double bestRawScore = 0;
        double? bestCenteredScore = null;

        var images = candidate.DentalImages ?? new List<DentalImage>();

        // Edge case: subject has a feature vector but no images
        if (images.Count == 0 && probe.FeatureVector != null && candidateVector != null)
        {
            var (score, rawScore, centeredScore) = ScoreVectorMatch(probe.FeatureVector, candidateVector, centeringContext);
            bestScore = score;
            bestRawScore = rawScore;
            bestCenteredScore = centeredScore;
        }

        foreach (var img in images)
        {
            var (score, rawScore, centeredScore) = ScoreImage(probe, img, candidateVector, centeringContext);
            if (score > bestScore)
            {
                bestScore = score;
                bestRawScore = rawScore;
                bestCenteredScore = centeredScore;
            }
        }

        if (bestScore <= 0) return null;

        var details = new Dictionary<string, double>
        {
            { "Fingerprint Similarity", bestScore },
            { "Raw Fingerprint Similarity", bestRawScore }
        };
        if (bestCenteredScore.HasValue)
        {
            details["Centered Fingerprint Similarity"] = bestCenteredScore.Value;
        }

        return new MatchCandidate
        {
            Subject = candidate,
            Score = bestScore,
            MatchMethod = "Biometric Fingerprint",
            MatchDetails = details
        };
    }

    /// <summary>
    /// Scores a single image against the probe. Prefers vector matching; falls back to code matching.
    /// </summary>
    private (double score, double rawScore, double? centeredScore) ScoreImage(
        DentalFingerprint probe,
        DentalImage img,
        float[]? candidateVector,
        VectorCenteringContext? centeringContext)
    {
        // 1. Prioritize direct image-specific vector matching
        float[]? imgVector = img.FeatureVector
            ?? DecodeFeatureVector(img.FeatureVectorBlob)
            ?? TryGetParsedFeatureVector(img);
        if (probe.FeatureVector != null && imgVector != null)
        {
            return ScoreVectorMatch(probe.FeatureVector, imgVector, centeringContext);
        }

        // 2. Next, check fallback to subject-level aggregate vector
        if (probe.FeatureVector != null && candidateVector != null)
        {
            return ScoreVectorMatch(probe.FeatureVector, candidateVector, centeringContext);
        }

        // 3. Fallback: code-based matching for legacy records
        if (!string.IsNullOrEmpty(img.FingerprintCode))
        {
            var candidateFp = _biometricService.ParseFingerprintCode(img.FingerprintCode);
            double raw = _biometricService.CalculateSimilarity(probe, candidateFp);
            return (raw, raw, null);
        }

        return (0, 0, null);
    }

    private static float[]? TryGetParsedFeatureVector(DentalImage image)
    {
        try
        {
            return image.ParsedAnalysisResults?.FeatureVector;
        }
        catch (InvalidOperationException) { return null; }
    }

    private (double score, double rawScore, double? centeredScore) ScoreVectorMatch(
        float[] probeVector,
        float[] candidateVector,
        VectorCenteringContext? centeringContext)
    {
        double raw = CalculateCosineSimilarity(probeVector, candidateVector);
        double score = CalibrateVectorScore(raw);

        if (centeringContext != null &&
            TryCalculateCenteredCosineSimilarity(probeVector, candidateVector, centeringContext.Centroid, out var centeredRaw))
        {
            // Blend absolute cosine with centered cosine to suppress inflated near-perfect scores.
            double centeredScore = CalibrateCenteredScore(centeredRaw);
            score = Math.Sqrt(Math.Max(0.0, score * centeredScore));
            return (score, raw, centeredRaw);
        }

        return (score, raw, null);
    }

    /// <summary>
    /// Decodes a byte[] feature vector stored in the database back to float[].
    /// Returns null if the input is null or empty.
    /// </summary>
    private static float[]? DecodeFeatureVector(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0) return null;
        if (bytes.Length % sizeof(float) != 0) return null;

        var floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }

    private static IReadOnlyDictionary<Subject, float[]> BuildCandidateVectors(
        IReadOnlyCollection<Subject> candidates,
        float[]? probeVector)
    {
        if (probeVector == null || probeVector.Length == 0)
            return new Dictionary<Subject, float[]>();

        int probeLength = probeVector.Length;
        var vectors = new Dictionary<Subject, float[]>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var decoded = DecodeFeatureVector(candidate.FeatureVector);
            if (decoded != null && decoded.Length == probeLength)
            {
                vectors[candidate] = decoded;
            }
        }

        return vectors;
    }

    private static VectorCenteringContext? BuildCenteringContext(
        float[]? probeVector,
        IReadOnlyCollection<float[]> candidateVectors)
    {
        if (probeVector == null ||
            probeVector.Length == 0 ||
            candidateVectors.Count < CenteredSimilarityMinCandidates)
        {
            return null;
        }

        int length = probeVector.Length;
        if (candidateVectors.Any(v => v.Length != length))
            return null;

        var centroid = new float[length];
        foreach (var vector in candidateVectors)
        {
            for (int i = 0; i < length; i++)
            {
                centroid[i] += vector[i];
            }
        }

        for (int i = 0; i < length; i++)
        {
            centroid[i] /= candidateVectors.Count;
        }

        double variance = ComputeMeanVariance(candidateVectors, centroid);
        if (variance < MinCenteringVariancePerDimension)
            return null;

        return new VectorCenteringContext(centroid);
    }

    private static double ComputeMeanVariance(IReadOnlyCollection<float[]> vectors, ReadOnlySpan<float> centroid)
    {
        if (vectors.Count == 0 || centroid.Length == 0)
            return 0;

        double varianceSum = 0;

        foreach (var vector in vectors)
        {
            double distanceSquared = 0;
            for (int i = 0; i < centroid.Length; i++)
            {
                double diff = vector[i] - centroid[i];
                distanceSquared += diff * diff;
            }

            varianceSum += distanceSquared / centroid.Length;
        }

        return varianceSum / vectors.Count;
    }

    private static bool TryCalculateCenteredCosineSimilarity(
        ReadOnlySpan<float> probe,
        ReadOnlySpan<float> candidate,
        ReadOnlySpan<float> centroid,
        out double similarity)
    {
        similarity = 0;

        if (probe.Length == 0 ||
            probe.Length != candidate.Length ||
            probe.Length != centroid.Length)
        {
            return false;
        }

        double dot = 0;
        double probeNorm = 0;
        double candidateNorm = 0;

        for (int i = 0; i < probe.Length; i++)
        {
            double probeValue = probe[i] - centroid[i];
            double candidateValue = candidate[i] - centroid[i];
            dot += probeValue * candidateValue;
            probeNorm += probeValue * probeValue;
            candidateNorm += candidateValue * candidateValue;
        }

        if (probeNorm <= 1e-12 || candidateNorm <= 1e-12)
            return false;

        similarity = dot / (Math.Sqrt(probeNorm) * Math.Sqrt(candidateNorm));
        similarity = Math.Clamp(similarity, -1.0, 1.0);
        return !double.IsNaN(similarity) && !double.IsInfinity(similarity);
    }

    /// <summary>
    /// Calibrates raw cosine similarity so near-baseline scores do not appear as high-confidence matches.
    /// </summary>
    private double CalibrateVectorScore(double rawSimilarity)
    {
        if (double.IsNaN(rawSimilarity) || double.IsInfinity(rawSimilarity))
            return 0;

        double clamped = Math.Clamp(rawSimilarity, -1.0, 1.0);
        if (clamped <= _vectorScoreFloor)
            return 0;

        double normalized = (clamped - _vectorScoreFloor) / (1.0 - _vectorScoreFloor);
        return Math.Clamp(Math.Pow(normalized, _vectorScoreGamma), 0.0, 1.0);
    }

    private static double CalibrateCenteredScore(double centeredSimilarity)
    {
        if (double.IsNaN(centeredSimilarity) || double.IsInfinity(centeredSimilarity))
            return 0;

        double normalized = (Math.Clamp(centeredSimilarity, -1.0, 1.0) + 1.0) * 0.5;
        if (normalized <= CenteredScoreFloor)
            return 0;

        double scaled = (normalized - CenteredScoreFloor) / (1.0 - CenteredScoreFloor);
        return Math.Clamp(Math.Pow(scaled, CenteredScoreGamma), 0.0, 1.0);
    }
}


