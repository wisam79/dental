using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;
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
    private readonly byte[] _aesKey;
    private readonly byte[] _hmacKey;
    private const int IvSize = 16;     // AES block size
    private const int HmacSize = 32;   // SHA-256 digest size


    public EncryptionService(IConfiguration configuration)
    {
        // 1. Try environment variable first (highest priority, best practice for production)
        var envKey = Environment.GetEnvironmentVariable("DENTALID_ENCRYPTION_KEY");
        if (!string.IsNullOrWhiteSpace(envKey))
        {
            try
            {
                var keyBytes = Convert.FromBase64String(envKey);
                if (keyBytes.Length == 32)
                {
                    _key = keyBytes;
                    _aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_AES"));
                    _hmacKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_HMAC"));
                    return;
                }
            }
            catch { /* Invalid base64, fall through */ }
        }

        // 2. Try to load OS-protected key from local storage
        if (TryLoadKeyFromStorage(out _key))
        {
            _aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_AES"));
            _hmacKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_HMAC"));
            return;
        }

        // 3. Migration: Check appsettings.json for legacy key
        var configKey = configuration["Security:EncryptionKey"];
        if (!string.IsNullOrEmpty(configKey) &&
            !configKey.Contains("CHANGE-THIS") &&
            !configKey.Contains("DO-NOT-USE"))
        {
            try
            {
                var legacyKeyBytes = Convert.FromBase64String(configKey);
                if (legacyKeyBytes.Length == 32)
                {
                    _key = legacyKeyBytes;
                    // Migrate to OS-protected storage and stop using appsettings
                    SaveKeyToStorage(_key);
                    
                    _aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_AES"));
                    _hmacKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_HMAC"));
                    return;
                }
            }
            catch { /* If invalid, ignore and generate new */ }
        }

        // 4. Generate New Key (Secure Default for first run)
        _key = new byte[32];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(_key);
        }
        SaveKeyToStorage(_key);
        
        // Derive specific operational keys from the master key
        _aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_AES"));
        _hmacKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_HMAC"));
    }

    private bool TryLoadKeyFromStorage(out byte[] key)
    {
        key = Array.Empty<byte>();
        try
        {
            var keyPath = GetKeyPath();
            if (!File.Exists(keyPath)) return false;

            var protectedBytes = File.ReadAllBytes(keyPath);

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
            {
                if (TryUnprotectWindows(protectedBytes, DataProtectionScope.CurrentUser, out key) &&
                    key.Length == 32)
                {
                    return true;
                }

                if (TryUnprotectWindows(protectedBytes, DataProtectionScope.LocalMachine, out key) &&
                    key.Length == 32)
                {
                    // Migrate legacy LocalMachine scope to per-user scope.
                    TryMigrateWindowsKeyProtection(key);
                    return true;
                }

                return false;
            }
            else
            {
                // Non-Windows: validate and use the stored key bytes, but warn loudly.
                // RECOMMENDATION: Use DENTALID_ENCRYPTION_KEY environment variable or a vault in production.
                if (protectedBytes.Length != 32)
                {
                    Console.Error.WriteLine(
                        "[SECURITY WARNING] Non-Windows: stored key has unexpected length. " +
                        "Regenerating. Existing encrypted data may be unreadable.");
                    return false;
                }
                Console.Error.WriteLine(
                    "[SECURITY WARNING] Non-Windows platform detected. Encryption key is stored " +
                    "with OS-level file permissions only. For production, set the " +
                    "DENTALID_ENCRYPTION_KEY environment variable to a 32-byte Base64 value, " +
                    "or use a secrets manager (HashiCorp Vault, Azure Key Vault, etc.).");
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

            byte[] dataToWrite;
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Windows))
            {
                dataToWrite = ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(keyPath, dataToWrite);
            }
            else
            {
                // Non-Windows: secure permissions directly upon creation
                var options = new FileStreamOptions
                {
                    Mode = FileMode.Create,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
                using var fs = new FileStream(keyPath, options);
                fs.Write(key);
            }
        }
        catch (Exception ex)
        {
            throw new CryptographicException("Failed to persist encryption key to secure storage.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool TryUnprotectWindows(byte[] protectedBytes, DataProtectionScope scope, out byte[] key)
    {
        try
        {
            key = ProtectedData.Unprotect(protectedBytes, null, scope);
            return true;
        }
        catch
        {
            key = Array.Empty<byte>();
            return false;
        }
    }

    private void TryMigrateWindowsKeyProtection(byte[] key)
    {
        try
        {
            SaveKeyToStorage(key);
        }
        catch
        {
            // Continue with loaded key even if migration fails.
        }
    }

    /// <summary>
    /// Attempts to set chmod 600 (owner read/write only) on Linux/macOS key files.
    /// Silently ignores failures (e.g. when running as root or on unsupported FS).
    /// </summary>
    private static void TrySetRestrictiveFilePermissions(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            // UnixFileMode is only available on .NET 6+ on Unix platforms
            if (File.Exists(filePath))
            {
                File.SetUnixFileMode(filePath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
            // Best-effort; if this fails, the admin needs to handle permissions manually
        }
    }

    private string GetKeyPath() => Path.Combine(AppContext.BaseDirectory, "data", "security", "master.key");

    /// <summary>
    /// For unit testing only: creates an instance with a pre-supplied raw key,
    /// bypassing all key storage and derivation logic.
    /// </summary>
    internal EncryptionService(byte[] rawKey)
    {
        if (rawKey == null || rawKey.Length != 32)
            throw new ArgumentException("Key must be exactly 32 bytes (256-bit AES).", nameof(rawKey));
        _key = rawKey;
        _aesKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_AES"));
        _hmacKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, _key, 32, Encoding.UTF8.GetBytes("DentalID_HMAC"));
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;

        using var aes = Aes.Create();
        aes.Key = _aesKey;
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

        // Build: IV + Ciphertext
        var payload = new byte[IvSize + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, payload, 0, IvSize);
        Buffer.BlockCopy(cipherBytes, 0, payload, IvSize, cipherBytes.Length);

        // Compute HMAC over IV+Ciphertext (Encrypt-then-MAC)
        byte[] hmac;
        using (var hmacSha = new HMACSHA256(_hmacKey))
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
            using (var hmacSha = new HMACSHA256(_hmacKey))
            {
                computedHmac = hmacSha.ComputeHash(payload);
            }

            if (!CryptographicOperations.FixedTimeEquals(computedHmac, storedHmac))
            {
                // HMAC mismatch — possible tampering or legacy data format
                throw new CryptographicException("HMAC verification failed. Data might be tampered or corrupted.");
            }

            // Decrypt
            using var aes = Aes.Create();
            aes.Key = _aesKey;
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
            // Fall back to legacy decryption if modern format fails
            return TryLegacyDecrypt(cipherText);
        }
    }

    /// <summary>
    /// Attempts to decrypt data in the legacy format: plain AES-CBC with a static IV.
    /// Static IV = "1234567890123456" (UTF-8). Returns the original string if decryption fails.
    /// </summary>
    private string TryLegacyDecrypt(string cipherText)
    {
        try
        {
            var cipherBytes = Convert.FromBase64String(cipherText);
            var staticIv = Encoding.UTF8.GetBytes("1234567890123456");

            using var aes = Aes.Create();
            aes.Key = _aesKey;
            aes.IV = staticIv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var ms = new MemoryStream(cipherBytes);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            return sr.ReadToEnd();
        }
        catch
        {
            // Legacy decryption also failed — return original cipherText as-is
            return cipherText;
        }
    }

    public string ComputeDeterministicHash(string normalizedValue, string context)
    {
        if (string.IsNullOrWhiteSpace(normalizedValue))
        {
            return string.Empty;
        }

        if (string.IsNullOrWhiteSpace(context))
        {
            throw new ArgumentException("Hash context is required.", nameof(context));
        }

        var payload = Encoding.UTF8.GetBytes($"{context}:{normalizedValue}");
        using var hmac = new HMACSHA256(_hmacKey);
        var hash = hmac.ComputeHash(payload);
        return Convert.ToHexString(hash);
    }
}
