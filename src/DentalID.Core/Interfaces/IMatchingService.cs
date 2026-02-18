using System.Collections.Generic;
using DentalID.Core.DTOs;
using DentalID.Core.Entities;

namespace DentalID.Core.Interfaces;

public interface IMatchingService
{
    /// <summary>
    /// Calculates the cosine similarity between two feature vectors.
    /// </summary>
    /// <param name="vectorA">First vector</param>
    /// <param name="vectorB">Second vector</param>
    /// <returns>Similarity score between -1 and 1</returns>
    double CalculateCosineSimilarity(ReadOnlySpan<float> vectorA, ReadOnlySpan<float> vectorB);

    /// <summary>
    /// Finds the best matches for a given fingerprint from a list of subjects.
    /// </summary>
    /// <param name="probe">The fingerprint to search for.</param>
    /// <param name="candidates">The list of subjects to search against.</param>
    /// <param name="criteria">Optional filtering criteria (Age, Gender).</param>
    /// <returns>List of matches sorted by score descending.</returns>
    List<MatchCandidate> FindMatches(DentalFingerprint probe, IEnumerable<Subject> candidates, MatchingCriteria? criteria = null);
}
