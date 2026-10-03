using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Security;
using System.Security.Cryptography;
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
    /// A zero-length plaintext round-trips.
    /// </summary>
    [Fact]
    public void EncryptThenDecrypt_EmptyPlaintext_RoundTrips()
    {
        var key = MachineBoundEncryption.DeriveKey("machine-a", Salt);

        var encrypted = MachineBoundEncryption.Encrypt([], key);

        Assert.True(MachineBoundEncryption.TryDecrypt(encrypted, key, out var decrypted));
        Assert.Empty(decrypted!);
    }

    /// <summary>
    /// Data saved under the fallback secret decrypts when the reader's secret came from the primary source.
    /// </summary>
    [Fact]
    public void TryDecryptWithSecret_PrimarySource_RetriesFallbackSecret()
    {
        var encrypted = MachineBoundEncryption.EncryptWithSecret(Plaintext, MachineBoundEncryption.GetFallbackMachineSecret(), Salt);

        Assert.True(MachineBoundEncryption.TryDecryptWithSecret(encrypted, new MachineSecret("primary-id", true), Salt, out var decrypted));
        Assert.Equal(Plaintext, decrypted);
    }

    /// <summary>
    /// The fallback is only retried for a primary-sourced secret, and data from another primary secret never decrypts.
    /// </summary>
    [Fact]
    public void TryDecryptWithSecret_WithoutMatchingSecret_Fails()
    {
        var fromFallback = MachineBoundEncryption.EncryptWithSecret(Plaintext, MachineBoundEncryption.GetFallbackMachineSecret(), Salt);
        var fromOtherMachine = MachineBoundEncryption.EncryptWithSecret(Plaintext, "primary-id-b", Salt);

        Assert.False(MachineBoundEncryption.TryDecryptWithSecret(fromFallback, new MachineSecret("not-the-fallback", false), Salt, out _));
        Assert.False(MachineBoundEncryption.TryDecryptWithSecret(fromOtherMachine, new MachineSecret("primary-id-a", true), Salt, out _));
    }

    /// <summary>
    /// Known answer: a GitHub token file produced by the pre-refactor token storage code decrypts
    /// with the token salt and the unchanged PBKDF2 parameters.
    /// </summary>
    [Fact]
    public void TryDecrypt_PreRefactorTokenFile_RecoversPlaintext()
    {
        var blob = Convert.FromBase64String("AZVez7DY44IGJz677HRWxqoat7QbXw3CSVCJlF/1Ew2HokaI6mh5IW4TJOijp2A1ml8uO3VJ2DGohIA=");
        var key = MachineBoundEncryption.DeriveKey("known-answer-machine-secret", GitHubConstants.TokenFileKeySalt);

        Assert.True(MachineBoundEncryption.TryDecrypt(blob, key, out var plainBytes));
        Assert.Equal("ghp_KnownAnswerToken0123456789", Encoding.UTF8.GetString(plainBytes!));
    }

    /// <summary>
    /// Known answer for the byte layout: version at 0, nonce at 1, tag after the nonce, ciphertext after
    /// the header. Bytes assembled by hand with AesGcm decrypt, and Encrypt output decrypts by hand.
    /// </summary>
    [Fact]
    public void FileLayout_MatchesFixedOffsets()
    {
        const int nonceOffset = 1;
        const int tagOffset = nonceOffset + MachineBoundEncryptionConstants.NonceSizeBytes;
        const int headerLength = tagOffset + MachineBoundEncryptionConstants.TagSizeBytes;
        var key = MachineBoundEncryption.DeriveKey("known-answer-machine-secret", GitHubConstants.TokenFileKeySalt);
        var nonce = Enumerable.Range(1, MachineBoundEncryptionConstants.NonceSizeBytes).Select(i => (byte)i).ToArray();
        var cipherBytes = new byte[Plaintext.Length];
        var tag = new byte[MachineBoundEncryptionConstants.TagSizeBytes];
        using (var aes = new AesGcm(key, MachineBoundEncryptionConstants.TagSizeBytes))
        {
            aes.Encrypt(nonce, Plaintext, cipherBytes, tag);
        }

        byte[] handBuilt = [GitHubConstants.TokenFileFormatVersion, .. nonce, .. tag, .. cipherBytes];
        Assert.True(MachineBoundEncryption.TryDecrypt(handBuilt, key, out var decrypted));
        Assert.Equal(Plaintext, decrypted);

        var encrypted = MachineBoundEncryption.Encrypt(Plaintext, key);
        var manual = new byte[encrypted.Length - headerLength];
        using (var aes = new AesGcm(key, MachineBoundEncryptionConstants.TagSizeBytes))
        {
            aes.Decrypt(
                encrypted.AsSpan(nonceOffset, MachineBoundEncryptionConstants.NonceSizeBytes),
                encrypted.AsSpan(headerLength),
                encrypted.AsSpan(tagOffset, MachineBoundEncryptionConstants.TagSizeBytes),
                manual);
        }

        Assert.Equal(GitHubConstants.TokenFileFormatVersion, encrypted[0]);
        Assert.Equal(Plaintext, manual);
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
