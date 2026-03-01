namespace DentalID.Core.Interfaces;

public interface IEncryptionService
{
    string Encrypt(string plainText);
    string Decrypt(string cipherText);
    string ComputeDeterministicHash(string normalizedValue, string context);
}
