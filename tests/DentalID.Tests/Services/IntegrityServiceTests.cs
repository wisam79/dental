using Xunit;
using DentalID.Infrastructure.Services;

namespace DentalID.Tests.Services;

/// <summary>
/// Unit tests for IntegrityService file hashing and verification.
/// </summary>
public class IntegrityServiceTests
{
    [Fact]
    public async Task ComputeFileHashAsync_ShouldReturnConsistentHash()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile, "test content");
        
        try
        {
            // Act
            var hash1 = await service.ComputeFileHashAsync(testFile);
            var hash2 = await service.ComputeFileHashAsync(testFile);
            
            // Assert
            Assert.Equal(hash1, hash2);
            Assert.Equal(64, hash1.Length); // SHA256 = 64 hex characters
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_ShouldReturnDifferentHash_ForDifferentContent()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile1 = Path.GetTempFileName();
        var testFile2 = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile1, "content 1");
        await File.WriteAllTextAsync(testFile2, "content 2");
        
        try
        {
            // Act
            var hash1 = await service.ComputeFileHashAsync(testFile1);
            var hash2 = await service.ComputeFileHashAsync(testFile2);
            
            // Assert
            Assert.NotEqual(hash1, hash2);
        }
        finally
        {
            File.Delete(testFile1);
            File.Delete(testFile2);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_ShouldReturnKnownHash_ForKnownContent()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile, "hello world");
        
        try
        {
            // Act
            var hash = await service.ComputeFileHashAsync(testFile);
            
            // Assert - Known SHA256 hash for "hello world"
            var expectedHash = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";
            Assert.Equal(expectedHash, hash);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_ShouldThrowFileNotFoundException_ForNonExistentFile()
    {
        // Arrange
        var service = new IntegrityService();
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        
        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => service.ComputeFileHashAsync(nonExistentFile));
    }

    [Fact]
    public async Task VerifyFileAsync_ShouldReturnTrue_ForMatchingHash()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile, "test content");
        var expectedHash = await service.ComputeFileHashAsync(testFile);
        
        try
        {
            // Act
            var result = await service.VerifyFileAsync(testFile, expectedHash);
            
            // Assert
            Assert.True(result);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_ShouldReturnFalse_ForMismatchingHash()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile, "test content");
        var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";
        
        try
        {
            // Act
            var result = await service.VerifyFileAsync(testFile, wrongHash);
            
            // Assert
            Assert.False(result);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_ShouldReturnFalse_ForNonExistentFile()
    {
        // Arrange
        var service = new IntegrityService();
        var nonExistentFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var anyHash = "b94d27b9934d3e08a52e52d7da7dabfac484efe37a5380ee9088f7ace2efcde9";
        
        // Act
        var result = await service.VerifyFileAsync(nonExistentFile, anyHash);
        
        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task VerifyFileAsync_ShouldReturnFalse_ForEmptyHash()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile, "test content");
        
        try
        {
            // Act
            var result = await service.VerifyFileAsync(testFile, "");
            
            // Assert
            Assert.False(result);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_ShouldReturnFalse_ForNullHash()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile, "test content");
        
        try
        {
            // Act
            var result = await service.VerifyFileAsync(testFile, null!);
            
            // Assert
            Assert.False(result);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task VerifyFileAsync_ShouldBeCaseInsensitive()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        await File.WriteAllTextAsync(testFile, "test content");
        var expectedHash = await service.ComputeFileHashAsync(testFile);
        var uppercaseHash = expectedHash.ToUpperInvariant();
        
        try
        {
            // Act
            var result = await service.VerifyFileAsync(testFile, uppercaseHash);
            
            // Assert
            Assert.True(result);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_ShouldHandleLargeFiles()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        // Create a 1MB file
        var largeContent = new byte[1024 * 1024];
        new Random(42).NextBytes(largeContent);
        await File.WriteAllBytesAsync(testFile, largeContent);
        
        try
        {
            // Act
            var hash = await service.ComputeFileHashAsync(testFile);
            
            // Assert
            Assert.Equal(64, hash.Length);
            Assert.Matches("^[a-f0-9]{64}$", hash);
        }
        finally
        {
            File.Delete(testFile);
        }
    }

    [Fact]
    public async Task ComputeFileHashAsync_ShouldHandleEmptyFile()
    {
        // Arrange
        var service = new IntegrityService();
        var testFile = Path.GetTempFileName();
        // Create empty file
        await File.WriteAllBytesAsync(testFile, Array.Empty<byte>());
        
        try
        {
            // Act
            var hash = await service.ComputeFileHashAsync(testFile);
            
            // Assert - Known SHA256 hash for empty content
            var expectedHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            Assert.Equal(expectedHash, hash);
        }
        finally
        {
            File.Delete(testFile);
        }
    }
}
