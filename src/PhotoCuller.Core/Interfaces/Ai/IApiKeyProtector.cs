namespace PhotoCuller.Core.Interfaces.Ai;

/// <summary>
/// API key protector.
/// Version 1 on Windows uses DPAPI (ProtectedData); plaintext storage is not permitted.
/// </summary>
public interface IApiKeyProtector
{
    /// <summary>Encrypts a plaintext API key and returns the base64 ciphertext.</summary>
    string Encrypt(string plainText);

    /// <summary>Decrypts a base64 ciphertext and returns the plaintext API key.</summary>
    string Decrypt(string cipherText);
}
