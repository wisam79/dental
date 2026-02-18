namespace DentalID.Core.Interfaces;

/// <summary>
/// Service responsible for verifying the integrity and authenticity of digital assets.
/// </summary>
public interface IIntegrityService
{
    /// <summary>
    /// Computes the SHA-256 hash of the specified file.
    /// </summary>
    /// <param name="filePath">Full path to the file.</param>
    /// <returns>Hexadecimal string representation of the hash.</returns>
    Task<string> ComputeFileHashAsync(string filePath);

    /// <summary>
    /// Verifies if the file at the given path matches the expected hash.
    /// </summary>
    /// <param name="filePath">Full path to the file.</param>
    /// <param name="expectedHash"> The expected SHA-256 hash.</param>
    /// <returns>True if the hash matches, false otherwise.</returns>
    Task<bool> VerifyFileAsync(string filePath, string expectedHash);
}
