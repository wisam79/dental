namespace DentalID.Core.Interfaces;

public interface IImageIntegrityService
{
    /// <summary>
    /// Computes a SHA256 hash of the image stream for caching and integrity verification.
    /// </summary>
    string ComputeHash(Stream imageStream);

    /// <summary>
    /// Performs forensic analysis on image metadata and structure to detect potential manipulation.
    /// </summary>
    /// <param name="imagePath">Path to the image file.</param>
    /// <returns>List of warning flags (empty if clean).</returns>
    List<string> AnalyzeIntegrity(string imagePath);
}
