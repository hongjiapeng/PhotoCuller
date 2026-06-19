using FluentAssertions;
using PhotoCuller.Infrastructure.Configuration;

namespace PhotoCuller.Tests.CoreTests;

public sealed class ApiKeyProtectorTests
{
    private readonly DpapiApiKeyProtector _sut = new();

    [Fact]
    public void Encrypt_ProducesBase64String()
    {
        var cipher = _sut.Encrypt("my-secret-key");
        cipher.Should().NotBeNullOrEmpty();

        // Should be valid base64
        var action = () => Convert.FromBase64String(cipher);
        action.Should().NotThrow();
    }

    [Fact]
    public void Decrypt_RoundTrip_ReturnsOriginal()
    {
        const string original = "sk-abc123XYZ!@#";
        var cipher = _sut.Encrypt(original);
        var decrypted = _sut.Decrypt(cipher);
        decrypted.Should().Be(original);
    }

    [Fact]
    public void Encrypt_DifferentCalls_ProduceDifferentCiphertexts()
    {
        // Because AES-GCM uses a random nonce each time
        const string key = "same-api-key";
        var c1 = _sut.Encrypt(key);
        var c2 = _sut.Encrypt(key);
        c1.Should().NotBe(c2, "each encryption should produce a unique nonce");
    }

    [Fact]
    public void Encrypt_EmptyString_ThrowsArgumentException()
    {
        var act = () => _sut.Encrypt(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decrypt_EmptyString_ThrowsArgumentException()
    {
        var act = () => _sut.Decrypt(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_Throws()
    {
        var cipher = _sut.Encrypt("valid-key");
        var tampered = cipher[..^4] + "XXXX"; // corrupt last bytes
        var act = () => _sut.Decrypt(tampered);
        act.Should().Throw<Exception>("authentication tag mismatch should throw");
    }

    [Theory]
    [InlineData("short")]
    [InlineData("a very long api key with spaces and unicode: 你好世界")]
    [InlineData("sk-abc123!@#$%^&*()")]
    public void RoundTrip_VariousInputs(string input)
    {
        _sut.Decrypt(_sut.Encrypt(input)).Should().Be(input);
    }
}
