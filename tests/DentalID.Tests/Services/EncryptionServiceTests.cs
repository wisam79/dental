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

        // Bug fix: Use the internal raw-key constructor to bypass DPAPI key storage.
        // The standard IConfiguration constructor tries TryLoadKeyFromStorage() first, which
        // may find a key from a previous test run that differs from _testKey, breaking legacy
        // decrypt tests that must encrypt and decrypt with the SAME deterministic key.
        var rawKey = Convert.FromBase64String(_testKey);
        _service = new EncryptionService(rawKey);
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
        
        // Match EncryptionService internal key derivation
        var rawKey = Convert.FromBase64String(_testKey);
        var derivedKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, rawKey, 32, Encoding.UTF8.GetBytes("DentalID_AES"));
        
        using var aes = Aes.Create();
        aes.Key = derivedKey;
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

    [Fact]
    public void ComputeDeterministicHash_ShouldBeStable_ForSameInputAndContext()
    {
        var hash1 = _service.ComputeDeterministicHash("JOHN DOE", "subject:full-name:v1");
        var hash2 = _service.ComputeDeterministicHash("JOHN DOE", "subject:full-name:v1");

        hash1.Should().Be(hash2);
    }

    [Fact]
    public void ComputeDeterministicHash_ShouldDiffer_ForDifferentContexts()
    {
        var hash1 = _service.ComputeDeterministicHash("ABC123", "subject:national-id:v1");
        var hash2 = _service.ComputeDeterministicHash("ABC123", "subject:full-name:v1");

        hash1.Should().NotBe(hash2);
    }
}
