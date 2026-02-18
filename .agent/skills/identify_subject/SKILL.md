---
name: identify_subject
description: Identify a subject by matching a dental query image against the database.
---

# Identify Subject Skill

Use this skill to find the identity of an unknown subject ("John Doe") by comparing their post-mortem (PM) dental X-ray against the database of ante-mortem (AM) records.

## Dependencies
- `DentalID.Core.Interfaces.IMatchingService` (Vector Matching)
- `DentalID.Core.Interfaces.IAiPipelineService` (Feature Extraction)
- `DentalID.Core.DTOs.DentalFingerprint` (Biometric Data)

## Workflow

1.  **Extract Features (Query)**:
    - Before matching, the query image MUST be processed to extract a `FeatureVector`.
    - Call `IAiPipelineService.ExtractFeaturesAsync(stream)`.
    - **Validation**: Ensure `FeatureVector` is not null and has length 2048 (default embedding size).

2.  **Prepare Probe**:
    - Create a `DentalFingerprint` object.
    - Set `Code` to "PROBE".
    - Set `FeatureVector` to the extracted float array.

3.  **Optimization: Smart Filtering**:
    - **Constraint**: Matching against 10,000+ subjects is slow.
    - **Action**: Before calling `FindMatches`, filter the `candidates` list.
        - If `AnalysisResult.EstimatedGender` is "Male", exclude all "Female" subjects (allow "Unknown").
        - If `EstimatedAge` is 30, exclude subjects < 10 or > 80 (allow margin of error +/- 15 years).

4.  **Execute Match**:
    - Call `IMatchingService.FindMatches(probe, filteredCandidates)`.
    - This uses SIMD-accelerated Cosine Similarity.

5.  **Evaluate Results**:
    - Matches are returned sorted by `Score` (0.0 to 1.0).
    - **Thresholds**:
        - `> 0.80`: High Probability Match.
        - `0.60 - 0.79`: Possible Match (Requires manual review).
        - `< 0.60`: Inconclusive / No Match.

## Example Usage (C#)

```csharp
public async Task<List<MatchCandidate>> IdentifyUnknown(string queryImagePath, IAiPipelineService ai, IMatchingService matcher, IEnumerable<Subject> database)
{
    // 1. Extract
    using var stream = File.OpenRead(queryImagePath);
    var (vector, error) = await ai.ExtractFeaturesAsync(stream);
    
    if (vector == null) throw new Exception("Feature extraction failed");

    // 2. Probe
    var probe = new DentalFingerprint { FeatureVector = vector };

    // 3. Match
    var matches = matcher.FindMatches(probe, database);

    // 4. Report Top Match
    var best = matches.FirstOrDefault();
    if (best != null && best.Score > 0.85)
    {
        Console.WriteLine($"POSITIVE ID: {best.Subject.FullName} ({best.Score:P1})");
    }

    return matches;
}
```
