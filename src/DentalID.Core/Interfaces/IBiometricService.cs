using System.Collections.Generic;
using DentalID.Core.DTOs;

namespace DentalID.Core.Interfaces;

/// <summary>
/// Service responsible for generating and comparing biometric dental fingerprints ("Dental DNA").
/// </summary>
public interface IBiometricService
{
    /// <summary>
    /// Generates a unique dental fingerprint from the detected teeth and pathologies.
    /// </summary>
    /// <param name="teeth">List of detected teeth (spatial info).</param>
    /// <param name="pathologies">List of detected pathologies (condition info).</param>
    /// <returns>A DentalFingerprint object containing the code and score.</returns>
    DentalFingerprint GenerateFingerprint(List<DetectedTooth> teeth, List<DetectedPathology> pathologies);

    /// <summary>
    /// Parses a fingerprint code string (e.g., "18:M-17:F") back into a DentalFingerprint object.
    /// </summary>
    DentalFingerprint ParseFingerprintCode(string code);

    /// <summary>
    /// Calculates the similarity score (0.0 to 1.0) between two fingerprints.
    /// </summary>
    double CalculateSimilarity(DentalFingerprint fp1, DentalFingerprint fp2);
}
