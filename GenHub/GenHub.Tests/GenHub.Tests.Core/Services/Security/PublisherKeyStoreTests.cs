using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Security;
using GenHub.Core.Services.Security;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// Tests for <see cref="PublisherKeyStore"/>.
/// </summary>
public sealed class PublisherKeyStoreTests : IDisposable
{
    private const string TestMachineSecret = "test-machine-secret";

    private static readonly DateTimeOffset TrustedAt = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

    private readonly string _appDataPath = Path.Combine(Path.GetTempPath(), "GenHubKeyStoreTests", Guid.NewGuid().ToString("N"));
    private readonly CapturingLogger<PublisherKeyStore> _logger = new();

    private string StorePath => Path.Combine(_appDataPath, PublisherKeyConstants.StoreFileName);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_appDataPath))
        {
            Directory.Delete(_appDataPath, recursive: true);
        }
    }

    /// <summary>
    /// A missing store file reads as an empty store.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetKeysAsync_NoFile_ReturnsEmpty()
    {
        var result = await CreateStore().GetKeysAsync();

        Assert.True(result.Success, result.FirstError);
        Assert.Empty(result.Data!);
    }

    /// <summary>
    /// Saved keys load back intact through a fresh store instance.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveKeyAsync_RoundTripsThroughNewInstance()
    {
        var rsaKey = CreateTrustedKey("publisher-rsa", PublicKeyAlgorithm.Rsa);
        var ecdsaKey = CreateTrustedKey("publisher-ec", PublicKeyAlgorithm.Ecdsa);
        var store = CreateStore();

        Assert.True((await store.SaveKeyAsync(rsaKey)).Success);
        Assert.True((await store.SaveKeyAsync(ecdsaKey)).Success);

        var reloaded = CreateStore();
        var all = await reloaded.GetKeysAsync();
        var single = await reloaded.GetKeyAsync("PUBLISHER-RSA");

        Assert.True(all.Success, all.FirstError);
        Assert.Equal([rsaKey, ecdsaKey], all.Data!);
        Assert.Equal(rsaKey, single.Data);
    }

    /// <summary>
    /// Saving a key for a known publisher replaces the previous key rather than adding a second one.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveKeyAsync_ExistingPublisher_ReplacesKey()
    {
        var store = CreateStore();
        var original = CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa);
        var replacement = CreateTrustedKey("Publisher", PublicKeyAlgorithm.Ecdsa);

        await store.SaveKeyAsync(original);
        await store.SaveKeyAsync(replacement);

        var all = await CreateStore().GetKeysAsync();
        Assert.Equal([replacement], all.Data!);
    }

    /// <summary>
    /// Removing a key reports whether a key was removed and persists the removal.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task RemoveKeyAsync_RemovesAndReportsMissing()
    {
        var store = CreateStore();
        await store.SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));

        var removed = await store.RemoveKeyAsync("publisher");
        var missing = await store.RemoveKeyAsync("publisher");

        Assert.True(removed.Success && removed.Data);
        Assert.True(missing.Success);
        Assert.False(missing.Data);
        Assert.Null((await CreateStore().GetKeyAsync("publisher")).Data);
    }

    /// <summary>
    /// Saving writes through a temporary sibling and leaves no temporary files behind.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveKeyAsync_LeavesOnlyTheStoreFile()
    {
        var store = CreateStore();

        await store.SaveKeyAsync(CreateTrustedKey("first", PublicKeyAlgorithm.Rsa));
        await store.SaveKeyAsync(CreateTrustedKey("second", PublicKeyAlgorithm.Ecdsa));

        Assert.Equal([StorePath], Directory.GetFiles(_appDataPath));
    }

    /// <summary>
    /// A cancelled save leaves the existing store file untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveKeyAsync_Cancelled_LeavesExistingFileIntact()
    {
        var store = CreateStore();
        await store.SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));
        var before = await File.ReadAllBytesAsync(StorePath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveKeyAsync(CreateTrustedKey("other", PublicKeyAlgorithm.Ecdsa), cts.Token));

        Assert.Equal(before, await File.ReadAllBytesAsync(StorePath));
        Assert.Equal([StorePath], Directory.GetFiles(_appDataPath));
    }

    /// <summary>
    /// A corrupt store file is reported through the result, and a save does not overwrite it.
    /// </summary>
    /// <param name="contents">The corrupt file contents.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("{ not json")]
    [InlineData("null")]
    [InlineData("{\"schemaVersion\":99,\"keys\":[]}")]
    [InlineData("{\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"keys\":[{\"publisherId\":\"\",\"publicKey\":null}]}")]
    [InlineData("{\"schemaVersion\":1,\"keys\":[{\"publisherId\":\"a\",\"publicKey\":{\"algorithm\":\"Rsa\",\"subjectPublicKeyInfo\":\"\",\"fingerprint\":\"f\"}}]}")]
    [InlineData("{\"schemaVersion\":1,\"keys\":[{\"publisherId\":\"a\",\"publicKey\":{\"algorithm\":\"Dsa\",\"subjectPublicKeyInfo\":\"AA==\",\"fingerprint\":\"f\"}}]}")]
    public async Task CorruptFile_ReturnsFailureAndIsNotOverwritten(string contents)
    {
        await WriteEncryptedAsync(contents);

        await AssertUnreadableAndUntouchedAsync(CreateStore());
    }

    /// <summary>
    /// The file on disk holds none of the key data, fingerprint, or publisher ID in plaintext.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SaveKeyAsync_EncryptsFileAtRest()
    {
        var key = CreateTrustedKey("plaintext-publisher-id", PublicKeyAlgorithm.Rsa);
        var spki = Convert.FromBase64String(key.PublicKey.SubjectPublicKeyInfo);

        await CreateStore().SaveKeyAsync(key);

        var fileBytes = await File.ReadAllBytesAsync(StorePath);
        var fileText = Encoding.UTF8.GetString(fileBytes);
        Assert.Equal(MachineBoundEncryptionConstants.FormatVersion, fileBytes[0]);
        Assert.DoesNotContain(key.PublicKey.SubjectPublicKeyInfo[..24], fileText, StringComparison.Ordinal);
        Assert.DoesNotContain(key.PublicKey.Fingerprint, fileText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(key.PublisherId, fileText, StringComparison.Ordinal);
        Assert.DoesNotContain("BEGIN", fileText, StringComparison.Ordinal);
        Assert.Equal(-1, fileBytes.AsSpan().IndexOf(spki.AsSpan(0, 32)));
    }

    /// <summary>
    /// Tampered ciphertext is reported as unreadable and the file is left unchanged.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task TamperedCiphertext_ReturnsFailureAndIsNotOverwritten()
    {
        await CreateStore().SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));
        var fileBytes = await File.ReadAllBytesAsync(StorePath);
        fileBytes[^1] ^= 0x01;
        await File.WriteAllBytesAsync(StorePath, fileBytes);

        await AssertUnreadableAndUntouchedAsync(CreateStore());
    }

    /// <summary>
    /// A store written on another machine cannot be decrypted and is left unchanged.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FileFromOtherMachine_ReturnsFailureAndIsNotOverwritten()
    {
        await CreateStore("other-machine-secret").SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));

        await AssertUnreadableAndUntouchedAsync(CreateStore());
    }

    /// <summary>
    /// A plaintext JSON store is not accepted: it is reported as unreadable and left unchanged.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task PlaintextFile_ReturnsFailureAndIsNotOverwritten()
    {
        Directory.CreateDirectory(_appDataPath);
        await File.WriteAllTextAsync(StorePath, "{\"schemaVersion\":1,\"keys\":[]}");

        await AssertUnreadableAndUntouchedAsync(CreateStore());
    }

    /// <summary>
    /// A store saved with the fallback secret, while the platform machine ID was unavailable,
    /// still loads once the machine ID is back.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FileWrittenWithFallbackSecret_LoadsWithPrimarySecret()
    {
        var key = CreateTrustedKey("publisher", PublicKeyAlgorithm.Ecdsa);
        await CreateStore(MachineBoundEncryption.GetFallbackMachineSecret(), fromPrimarySource: false).SaveKeyAsync(key);

        var loaded = await CreateStore().GetKeysAsync();

        Assert.True(loaded.Success, loaded.FirstError);
        Assert.Equal([key], loaded.Data!);
    }

    /// <summary>
    /// Duplicate publisher entries on disk are treated as corruption.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DuplicatePublisherEntries_ReturnFailure()
    {
        await CreateStore().SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));
        var json = await ReadDecryptedAsync();
        var entryStart = json.IndexOf('{', json.IndexOf('[', StringComparison.Ordinal));
        var entryEnd = json.LastIndexOf(']');
        var entry = json[entryStart..entryEnd].TrimEnd();
        await WriteEncryptedAsync(json.Insert(entryEnd, "," + entry));

        Assert.False((await CreateStore().GetKeysAsync()).Success);
    }

    /// <summary>
    /// Key material never reaches the store's log output or failure messages.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task StoreOperations_NeverLogKeyMaterial()
    {
        var key = CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa);
        var store = CreateStore();
        await store.SaveKeyAsync(key);
        await store.GetKeysAsync();
        await store.RemoveKeyAsync("publisher");
        await WriteEncryptedAsync("{\"schemaVersion\":1,\"keys\":[{\"publisherId\":\"a\",\"publicKey\":{\"algorithm\":\"Rsa\",\"subjectPublicKeyInfo\":\"" + key.PublicKey.SubjectPublicKeyInfo + "\"");
        var failure = await store.GetKeysAsync();

        var output = string.Join("\n", _logger.Entries.Concat(failure.Errors));
        Assert.NotEmpty(_logger.Entries);
        Assert.DoesNotContain(key.PublicKey.SubjectPublicKeyInfo[..24], output, StringComparison.Ordinal);
    }

    /// <summary>
    /// Invalid arguments violate the contract and throw.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InvalidArguments_Throw()
    {
        var store = CreateStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.SaveKeyAsync(null!));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.SaveKeyAsync(CreateTrustedKey(" ", PublicKeyAlgorithm.Rsa)));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.GetKeyAsync(string.Empty));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => store.RemoveKeyAsync(" "));
    }

    private static TrustedPublisherKey CreateTrustedKey(string publisherId, PublicKeyAlgorithm algorithm)
    {
        byte[] spki;
        if (algorithm == PublicKeyAlgorithm.Rsa)
        {
            using var rsa = RSA.Create(2048);
            spki = rsa.ExportSubjectPublicKeyInfo();
        }
        else
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            spki = ecdsa.ExportSubjectPublicKeyInfo();
        }

        var publicKey = new PublisherPublicKey(
            algorithm,
            Convert.ToBase64String(spki),
            Convert.ToHexString(SHA256.HashData(spki)).ToLowerInvariant());
        return new TrustedPublisherKey(publisherId, publicKey, TrustedAt);
    }

    private PublisherKeyStore CreateStore(string machineSecret = TestMachineSecret, bool fromPrimarySource = true)
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(c => c.GetApplicationDataPath()).Returns(_appDataPath);
        return new PublisherKeyStore(
            configuration.Object,
            _logger,
            () => new MachineSecret(machineSecret, fromPrimarySource));
    }

    private async Task WriteEncryptedAsync(string contents)
    {
        Directory.CreateDirectory(_appDataPath);
        var key = MachineBoundEncryption.DeriveKey(TestMachineSecret, PublisherKeyConstants.StoreKeySalt);
        await File.WriteAllBytesAsync(StorePath, MachineBoundEncryption.Encrypt(Encoding.UTF8.GetBytes(contents), key));
    }

    private async Task<string> ReadDecryptedAsync()
    {
        var key = MachineBoundEncryption.DeriveKey(TestMachineSecret, PublisherKeyConstants.StoreKeySalt);
        Assert.True(MachineBoundEncryption.TryDecrypt(await File.ReadAllBytesAsync(StorePath), key, out var plainBytes));
        return Encoding.UTF8.GetString(plainBytes!);
    }

    private async Task AssertUnreadableAndUntouchedAsync(PublisherKeyStore store)
    {
        var before = await File.ReadAllBytesAsync(StorePath);

        var keys = await store.GetKeysAsync();
        var key = await store.GetKeyAsync("a");
        var save = await store.SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));
        var remove = await store.RemoveKeyAsync("a");

        Assert.False(keys.Success);
        Assert.False(key.Success);
        Assert.False(save.Success);
        Assert.False(remove.Success);
        Assert.Equal(before, await File.ReadAllBytesAsync(StorePath));
    }
}
