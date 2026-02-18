using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using DentalID.Core.Interfaces;
using Microsoft.Extensions.Configuration;

namespace DentalID.Infrastructure.Services;

/// <summary>
/// AES-256-CBC encryption with random IV per encryption and HMAC-SHA256 authentication.
/// Ciphertext format: [16-byte IV][N-byte ciphertext][32-byte HMAC]
/// Encoded as a single Base64 string.
/// </summary>
public class EncryptionService : IEncryptionService
{
    private readonly byte[] _key;
    private const int IvSize = 16;     // AES block size
    private const int HmacSize = 32;   // SHA-256 digest size

    public EncryptionService(IConfiguration configuration)
    {
        // 1. Try to load DPAPI-protected key from local storage
        if (TryLoadKeyFromStorage(out _key))
        {
            return;
        }

        // 2. Migration: Check appsettings.json for legacy key
        var configKey = configuration["Security:EncryptionKey"];
        if (!string.IsNullOrEmpty(configKey))
        {
            try 
            {
                _key = Convert.FromBase64String(configKey);
                if (_key.Length != 32) throw new CryptographicException("Legacy key must be 32 bytes.");
                
                // Migrate to Secure Storage
                SaveKeyToStorage(_key);
                return;
            }
            catch (Exception) { /* If invalid, ignore and generate new */ }
        }

        // 3. Generate New Key (Secure Default)
        _key = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(_key);
        }
        SaveKeyToStorage(_key);
    }

    private bool TryLoadKeyFromStorage(out byte[] key)
    {
        key = Array.Empty<byte>();
        try
        {
            var keyPath = GetKeyPath();
            if (!File.Exists(keyPath)) return false;

            var protectedBytes = File.ReadAllBytes(keyPath);
            
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                key = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.LocalMachine);
            }
            else
            {
                // Bug #69: Non-Windows fallback stores key in plaintext — warn loudly in production
                // TODO: Replace with OS keyring (libsecret on Linux, Keychain on macOS) for production deployments
                Console.Error.WriteLine("[SECURITY WARNING] Running on non-Windows platform: encryption key stored in plaintext. " +
                    "Use a proper secret manager for production deployments.");
                key = protectedBytes; 
            }
            return key.Length == 32;
        }
        catch 
        { 
            return false; 
        }
    }

    private void SaveKeyToStorage(byte[] key)
    {
        try
        {
            var keyPath = GetKeyPath();
            Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);

            byte[] protectedBytes;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                protectedBytes = ProtectedData.Protect(key, null, DataProtectionScope.LocalMachine);
            }
            else
            {
                protectedBytes = key; // Fallback
            }

            File.WriteAllBytes(keyPath, protectedBytes);
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to persist encryption key to secure storage.", ex);
        }
    }

    private string GetKeyPath() => Path.Combine(AppContext.BaseDirectory, "data", "security", "master.key");

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = _key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV(); // Random IV for each encryption

        byte[] cipherBytes;
        using (var ms = new MemoryStream())
        using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainText);
            cs.Write(plainBytes, 0, plainBytes.Length);
            cs.FlushFinalBlock();
            cipherBytes = ms.ToArray();
        }

        // Build: IV + Ciphertext + HMAC
        var payload = new byte[IvSize + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, payload, IvSize, cipherBytes.Length);

        // Compute HMAC over IV+Ciphertext (Encrypt-then-MAC)
        byte[] hmac;
        using (var hmacSha = new HMACSHA256(_key))
        {
            hmac = hmacSha.ComputeHash(payload);
        }

        // Final: IV + Ciphertext + HMAC
        var result = new byte[payload.Length + HmacSize];
        Buffer.BlockCopy(payload, 0, result, 0, payload.Length);
        Buffer.BlockCopy(hmac, 0, result, payload.Length, HmacSize);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        try
        {
            var fullBytes = Convert.FromBase64String(cipherText);

            if (fullBytes.Length < IvSize + HmacSize + 1)
            {
                // Too short to be valid — might be legacy unencrypted data
                return cipherText;
            }

            // Split: IV | Ciphertext | HMAC
            int cipherLength = fullBytes.Length - IvSize - HmacSize;
            var iv = new byte[IvSize];
            var cipher = new byte[cipherLength];
            var storedHmac = new byte[HmacSize];

            Buffer.BlockCopy(fullBytes, 0, iv, 0, IvSize);
            Buffer.BlockCopy(fullBytes, IvSize, cipher, 0, cipherLength);
            Buffer.BlockCopy(fullBytes, fullBytes.Length - HmacSize, storedHmac, 0, HmacSize);

            // Verify HMAC first (Encrypt-then-MAC: verify before decrypting)
            var payload = new byte[IvSize + cipherLength];
            Buffer.BlockCopy(fullBytes, 0, payload, 0, payload.Length);

            byte[] computedHmac;
            using (var hmacSha = new HMACSHA256(_key))
            {
                computedHmac = hmacSha.ComputeHash(payload);
            }

            if (!CryptographicOperations.FixedTimeEquals(computedHmac, storedHmac))
            {
                // HMAC mismatch — possible tampering or legacy data format
                // Try legacy decryption (static IV format) for backward compatibility
                return TryLegacyDecrypt(cipherText);
            }

            // Decrypt
            using var aes = Aes.Create();
            aes.Key = _key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream(cipher);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch
        {
            // If all decryption fails, return original (might be plaintext or legacy)
            return cipherText;
        }
    }

    /// <summary>
    /// Bug #68: TryLegacyDecrypt must return the original string (not throw) when legacy data
    /// cannot be decrypted. Throwing inside Decrypt's try-block was swallowed by the outer catch,
    /// causing the method to silently return cipherText anyway — but the intent was "Try" (no-throw).
    /// Returning cipherText allows the caller to display the raw value rather than crashing.
    /// </summary>
    private string TryLegacyDecrypt(string cipherText)
    {
        // Legacy format is no longer supported. Return the ciphertext as-is so the caller
        // can decide what to do (display placeholder, prompt re-encryption, etc.)
        return cipherText;
    }
}
