using System.Security.Cryptography;
using System.Text;
using DentalID.Core.Interfaces;

namespace DentalID.Infrastructure.Services;

public class IntegrityService : IIntegrityService
{
    public async Task<string> ComputeFileHashAsync(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found for hashing", filePath);

        using var sha256 = SHA256.Create();
        // FIX: Use FileShare.Read to allow concurrent read access
        // This prevents IOException when multiple services access the same file
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        
        var hashBytes = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    public async Task<bool> VerifyFileAsync(string filePath, string expectedHash)
    {
        if (string.IsNullOrWhiteSpace(expectedHash)) return false;

        try
        {
            var currentHash = await ComputeFileHashAsync(filePath);
            return string.Equals(currentHash, expectedHash, StringComparison.OrdinalIgnoreCase);
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}
