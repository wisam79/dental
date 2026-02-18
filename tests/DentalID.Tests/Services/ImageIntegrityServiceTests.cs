using Xunit;
using DentalID.Infrastructure.Services;
using System.IO;
using System;
using System.Threading.Tasks;

namespace DentalID.Tests.Services;

public class ImageIntegrityServiceTests : IDisposable
{
    private readonly ImageIntegrityService _service = new();
    private readonly string _tempFile;

    public ImageIntegrityServiceTests()
    {
        _tempFile = Path.GetTempFileName();
    }

    [Fact]
    public void AnalyzeIntegrity_ShouldDetectFakeExtension()
    {
        var fakeJpg = Path.ChangeExtension(_tempFile, ".jpg");
        File.WriteAllText(fakeJpg, "This is not a real image header");

        try 
        {
            var warnings = _service.AnalyzeIntegrity(fakeJpg);
            Assert.Contains(warnings, w => w.Contains("Integrity Alert"));
        }
        finally
        {
            if (File.Exists(fakeJpg)) File.Delete(fakeJpg);
        }
    }

    [Fact]
    public void AnalyzeIntegrity_ShouldDetectSoftwareSignatures()
    {
        var manipulatedFile = Path.ChangeExtension(_tempFile, ".jpg");
        
        byte[] content = new byte[] { 
            0xFF, 0xD8, 
            0x41, 0x64, 0x6F, 0x62, 0x65, 0x20, 0x50, 0x68, 0x6F, 0x74, 0x6F, 0x73, 0x68, 0x6F, 0x70 
        };
        
        File.WriteAllBytes(manipulatedFile, content);

        try 
        {
            var warnings = _service.AnalyzeIntegrity(manipulatedFile);
            Assert.Contains(warnings, w => w.Contains("Manipulation Alert"));
        }
        finally
        {
            if (File.Exists(manipulatedFile)) File.Delete(manipulatedFile);
        }
    }
    
    [Fact]
    public void ComputeHash_ShouldReturnConsistentHash()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        using var stream1 = new MemoryStream(data);
        using var stream2 = new MemoryStream(data);

        var hash1 = _service.ComputeHash(stream1);
        var hash2 = _service.ComputeHash(stream2);

        Assert.Equal(hash1, hash2);
        Assert.False(string.IsNullOrEmpty(hash1));
    }

    [Fact]
    public void ComputeHash_ShouldReturnDifferentHashForDifferentContent()
    {
        using var stream1 = new MemoryStream(new byte[] { 1, 2, 3 });
        using var stream2 = new MemoryStream(new byte[] { 1, 2, 4 });

        var hash1 = _service.ComputeHash(stream1);
        var hash2 = _service.ComputeHash(stream2);

        Assert.NotEqual(hash1, hash2);
    }
    
    [Fact]
    public void ComputeHash_ShouldResetStreamPosition()
    {
        var data = new byte[] { 1, 2, 3 };
        using var stream = new MemoryStream(data);
        
        _service.ComputeHash(stream);
        
        Assert.Equal(0, stream.Position);
    }
    
    [Fact]
    public void ComputeHash_ThrowsOnNullStream()
    {
       Assert.Throws<ArgumentNullException>(() => _service.ComputeHash(null!));
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }
}
