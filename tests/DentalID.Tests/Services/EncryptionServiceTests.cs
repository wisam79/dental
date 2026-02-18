using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DentalID.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace DentalID.Tests.Services;

public class EncryptionServiceTests
{
    private readonly EncryptionService _service;
    private readonly Mock<IConfiguration> _mockConfig;
    private readonly string _testKey;

    public EncryptionServiceTests()
    {
        // 32-byte key (256-bit)
        _testKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("12345678901234567890123456789012")); 
        _mockConfig = new Mock<IConfiguration>();
        _mockConfig.Setup(c => c["Security:EncryptionKey"]).Returns(_testKey);

        _service = new EncryptionService(_mockConfig.Object);
    }

    [Fact]
    public void Encrypt_Decrypt_ShouldRoundtrip()
    {
        var original = "Secret Text 123!";
        var encrypted = _service.Encrypt(original);
        var decrypted = _service.Decrypt(encrypted);

        decrypted.Should().Be(original);
        encrypted.Should().NotBe(original);
    }

    [Fact]
    public void Encrypt_ShouldProduceDifferentCiphertext_ForSameInput()
    {
        var original = "Text";
        var enc1 = _service.Encrypt(original);
        var enc2 = _service.Encrypt(original);

        enc1.Should().NotBe(enc2); // IV differs
    }

    [Fact]
    public void Decrypt_TamperedPayload_ShouldReturnOriginalOrLegacy()
    {
        var original = "Secret";
        var encrypted = _service.Encrypt(original);
        var bytes = Convert.FromBase64String(encrypted);
        
        // Tamper with HMAC (last 32 bytes)
        if (bytes.Length > 0) 
            bytes[^1] ^= 0xFF; 
        
        var tampered = Convert.ToBase64String(bytes);
        
        // Should verify HMAC, fail, try legacy, fail, return tampered
        var result = _service.Decrypt(tampered);
        
        result.Should().Be(tampered);
    }

    [Fact]
    public void Decrypt_LegacyFormat_ShouldDecrypt_IfLongEnough()
    {
        // Construct legacy payload: AES-CBC, Key, IV="1234567890123456"
        // Needs to be > 48 bytes to trigger fallback logic in current implementation
        var plain = "Legacy Data that is sufficiently long to trigger the fallback logic properly 123";
        var iv = Encoding.UTF8.GetBytes("1234567890123456");
        var key = Convert.FromBase64String(_testKey);
        
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        
        byte[] cipherBytes;
        using (var ms = new MemoryStream())
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
             var b = Encoding.UTF8.GetBytes(plain);
             cs.Write(b, 0, b.Length);
             cs.FlushFinalBlock();
             cipherBytes = ms.ToArray();
        }
        var cipher = Convert.ToBase64String(cipherBytes);
        
        // Correctness check: Payload size
        cipherBytes.Length.Should().BeGreaterThan(48);

        // Decrypt using service (should fall back to legacy)
        var result = _service.Decrypt(cipher);
        
        result.Should().Be(plain);
    }
}
