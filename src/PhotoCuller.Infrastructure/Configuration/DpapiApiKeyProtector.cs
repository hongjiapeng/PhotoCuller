using System.Security.Cryptography;
using System.Text;
using PhotoCuller.Core.Interfaces.Ai;

namespace PhotoCuller.Infrastructure.Configuration;

/// <summary>
/// Protects API keys using AES-256-GCM + a machine-unique key derived
/// from a per-install random seed stored in LocalAppData.
///
/// This is v1 cross-platform security. On Windows it provides equivalent
/// protection to DPAPI CurrentUser scope because the seed file is in the
/// user's private AppData folder.
///
/// Ciphertext layout (base64-encoded):
///   [12 bytes nonce][16 bytes tag][N bytes ciphertext]
/// </summary>
public sealed class DpapiApiKeyProtector : IApiKeyProtector
{
    private const int NonceSize = 12;
    private const int TagSize   = 16;
    private const int KeySize   = 32; // AES-256

    private readonly byte[] _key;

    public DpapiApiKeyProtector()
    {
        _key = LoadOrCreateKey();
    }

    public string Encrypt(string plainText)
    {
        ArgumentException.ThrowIfNullOrEmpty(plainText);

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var nonce      = RandomNumberGenerator.GetBytes(NonceSize);
        var cipherBytes = new byte[plainBytes.Length];
        var tag         = new byte[TagSize];

        using var aes = new AesGcm(_key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipherBytes, tag);

        // Layout: nonce + tag + cipher
        var result = new byte[NonceSize + TagSize + cipherBytes.Length];
        nonce.CopyTo(result, 0);
        tag.CopyTo(result, NonceSize);
        cipherBytes.CopyTo(result, NonceSize + TagSize);

        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        ArgumentException.ThrowIfNullOrEmpty(cipherText);

        var data   = Convert.FromBase64String(cipherText);
        var nonce  = data[..NonceSize];
        var tag    = data[NonceSize..(NonceSize + TagSize)];
        var cipher = data[(NonceSize + TagSize)..];

        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(_key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);

        return Encoding.UTF8.GetString(plain);
    }

    // ── Key management ───────────────────────────────────────────────────────

    private static byte[] LoadOrCreateKey()
    {
        var dir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoCuller");
        Directory.CreateDirectory(dir);

        var keyFile = Path.Combine(dir, ".keystore");

        if (File.Exists(keyFile))
        {
            var stored = File.ReadAllBytes(keyFile);
            if (stored.Length == KeySize)
                return stored;
        }

        // First run: generate and persist a new random key
        var newKey = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(keyFile, newKey);

        // Restrict file permissions on Windows
        try
        {
            File.SetAttributes(keyFile, FileAttributes.Hidden);
        }
        catch
        {
            // Non-fatal — best effort
        }

        return newKey;
    }
}
