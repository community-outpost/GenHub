using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using System.Text;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for <see cref="MachineBoundEncryption"/>.
/// </summary>
public sealed class MachineBoundEncryptionTests
{
    private const string Salt = "GenHub.Tests.MachineBoundEncryption.v1";

    private static readonly byte[] Plaintext = Encoding.UTF8.GetBytes("{\"keys\":[\"plaintext-marker\"]}");

    /// <summary>
    /// Encrypted bytes decrypt back to the original with the same key.
    /// </summary>
    [Fact]
    public void EncryptThenDecrypt_SameKey_RoundTrips()
    {
        var key = MachineBoundEncryption.DeriveKey("machine-a", Salt);

        var encrypted = MachineBoundEncryption.Encrypt(Plaintext, key);

        Assert.True(MachineBoundEncryption.TryDecrypt(encrypted, key, out var decrypted));
        Assert.Equal(Plaintext, decrypted);
    }

    /// <summary>
    /// The output starts with the format version, carries a random nonce, and never holds the plaintext.
    /// </summary>
    [Fact]
    public void Encrypt_WritesVersionedHeaderAndHidesPlaintext()
    {
        var key = MachineBoundEncryption.DeriveKey("machine-a", Salt);

        var first = MachineBoundEncryption.Encrypt(Plaintext, key);
        var second = MachineBoundEncryption.Encrypt(Plaintext, key);

        Assert.Equal(MachineBoundEncryptionConstants.FormatVersion, first[0]);
        Assert.Equal(
            1 + MachineBoundEncryptionConstants.NonceSizeBytes + MachineBoundEncryptionConstants.TagSizeBytes + Plaintext.Length,
            first.Length);
        Assert.NotEqual(first, second);
        Assert.DoesNotContain("plaintext-marker", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    /// <summary>
    /// A key derived from another machine secret or another salt cannot decrypt.
    /// </summary>
    [Fact]
    public void TryDecrypt_KeyFromOtherSecretOrSalt_Fails()
    {
        var encrypted = MachineBoundEncryption.Encrypt(Plaintext, MachineBoundEncryption.DeriveKey("machine-a", Salt));

        Assert.False(MachineBoundEncryption.TryDecrypt(encrypted, MachineBoundEncryption.DeriveKey("machine-b", Salt), out var otherMachine));
        Assert.False(MachineBoundEncryption.TryDecrypt(encrypted, MachineBoundEncryption.DeriveKey("machine-a", Salt + ".other"), out var otherSalt));
        Assert.Null(otherMachine);
        Assert.Null(otherSalt);
    }

    /// <summary>
    /// Any modified byte, a wrong version, or truncated input fails authentication.
    /// </summary>
    [Fact]
    public void TryDecrypt_TamperedOrTruncatedInput_Fails()
    {
        var key = MachineBoundEncryption.DeriveKey("machine-a", Salt);
        var encrypted = MachineBoundEncryption.Encrypt(Plaintext, key);

        for (var index = 0; index < encrypted.Length; index++)
        {
            var tampered = (byte[])encrypted.Clone();
            tampered[index] ^= 0x01;
            Assert.False(MachineBoundEncryption.TryDecrypt(tampered, key, out _), $"byte {index} was accepted after tampering");
        }

        Assert.False(MachineBoundEncryption.TryDecrypt(encrypted[..^1], key, out _));
        Assert.False(MachineBoundEncryption.TryDecrypt([], key, out _));
        Assert.False(MachineBoundEncryption.TryDecrypt(Plaintext, key, out _));
    }

    /// <summary>
    /// The fallback secret is derived from the machine and user names.
    /// </summary>
    [Fact]
    public void GetFallbackMachineSecret_UsesMachineAndUserName()
    {
        Assert.Equal($"{Environment.MachineName}:{Environment.UserName}", MachineBoundEncryption.GetFallbackMachineSecret());
    }
}
