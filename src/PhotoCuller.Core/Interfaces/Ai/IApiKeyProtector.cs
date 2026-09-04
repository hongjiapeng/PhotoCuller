namespace PhotoCuller.Core.Interfaces.Ai;

/// <summary>
/// API key protector.
/// The current implementation uses AES-256-GCM; plaintext storage is not permitted.
/// </summary>
public interface IApiKeyProtector
{
    /// <summary>Encrypts a plaintext API key and returns the base64 ciphertext.</summary>
    string Encrypt(string plainText);

    /// <summary>Decrypts a base64 ciphertext and returns the plaintext API key.</summary>
    string Decrypt(string cipherText);
}
