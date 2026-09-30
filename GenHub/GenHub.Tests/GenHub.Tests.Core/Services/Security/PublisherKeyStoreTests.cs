using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Security;
using GenHub.Core.Services.Security;
using Moq;
using System.Security.Cryptography;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// Tests for <see cref="PublisherKeyStore"/>.
/// </summary>
public sealed class PublisherKeyStoreTests : IDisposable
{
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
        var before = await File.ReadAllTextAsync(StorePath);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.SaveKeyAsync(CreateTrustedKey("other", PublicKeyAlgorithm.Ecdsa), cts.Token));

        Assert.Equal(before, await File.ReadAllTextAsync(StorePath));
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
        Directory.CreateDirectory(_appDataPath);
        await File.WriteAllTextAsync(StorePath, contents);
        var store = CreateStore();

        var keys = await store.GetKeysAsync();
        var key = await store.GetKeyAsync("a");
        var save = await store.SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));
        var remove = await store.RemoveKeyAsync("a");

        Assert.False(keys.Success);
        Assert.False(key.Success);
        Assert.False(save.Success);
        Assert.False(remove.Success);
        Assert.Equal(contents, await File.ReadAllTextAsync(StorePath));
    }

    /// <summary>
    /// Duplicate publisher entries on disk are treated as corruption.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task DuplicatePublisherEntries_ReturnFailure()
    {
        await CreateStore().SaveKeyAsync(CreateTrustedKey("publisher", PublicKeyAlgorithm.Rsa));
        var json = await File.ReadAllTextAsync(StorePath);
        var entryStart = json.IndexOf('{', json.IndexOf('[', StringComparison.Ordinal));
        var entryEnd = json.LastIndexOf(']');
        var entry = json[entryStart..entryEnd].TrimEnd();
        await File.WriteAllTextAsync(StorePath, json.Insert(entryEnd, "," + entry));

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
        await File.WriteAllTextAsync(StorePath, "{\"schemaVersion\":1,\"keys\":[{\"publisherId\":\"a\",\"publicKey\":{\"algorithm\":\"Rsa\",\"subjectPublicKeyInfo\":\"" + key.PublicKey.SubjectPublicKeyInfo + "\"");
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

    private PublisherKeyStore CreateStore()
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(c => c.GetApplicationDataPath()).Returns(_appDataPath);
        return new PublisherKeyStore(configuration.Object, _logger);
    }
}
