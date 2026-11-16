using GenHub.Core.Interfaces.Publishers;
using GenHub.Features.Tools.Services.Hosting;
using Moq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="CredentialStoreDataStore"/>.
/// </summary>
public sealed class CredentialStoreDataStoreTests
{
    private readonly Mock<IHostingCredentialStore> _credentialStoreMock = new();
    private readonly CredentialStoreDataStore _dataStore;

    /// <summary>
    /// Initializes a new instance of the <see cref="CredentialStoreDataStoreTests"/> class.
    /// </summary>
    public CredentialStoreDataStoreTests()
    {
        _dataStore = new CredentialStoreDataStore(_credentialStoreMock.Object, "GoogleDrive.");
    }

    /// <summary>
    /// Tests that StoreAsync serializes the value to JSON and saves via IHostingCredentialStore with prefix.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task StoreAsync_SerializesAndSavesWithKeyPrefix()
    {
        const string key = "user";
        var sampleToken = new TestToken { AccessToken = "abc123xyz", TokenType = "Bearer" };

        await _dataStore.StoreAsync(key, sampleToken);

        _credentialStoreMock.Verify(
            s => s.SaveCredentialAsync(
                "GoogleDrive.user",
                It.Is<string>(json => json.Contains("abc123xyz")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that GetAsync retrieves the credential and deserializes JSON to target type.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetAsync_WhenCredentialExists_DeserializesJson()
    {
        const string json = "{\"AccessToken\":\"test_token\",\"TokenType\":\"Bearer\"}";
        _credentialStoreMock
            .Setup(s => s.GetCredentialAsync("GoogleDrive.user", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        var result = await _dataStore.GetAsync<TestToken>("user");

        Assert.NotNull(result);
        Assert.Equal("test_token", result.AccessToken);
        Assert.Equal("Bearer", result.TokenType);
    }

    /// <summary>
    /// Tests that GetAsync returns default when no credential is stored or empty string is returned.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetAsync_WhenNotFoundOrEmpty_ReturnsDefault()
    {
        _credentialStoreMock
            .Setup(s => s.GetCredentialAsync("GoogleDrive.nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var result = await _dataStore.GetAsync<TestToken>("nonexistent");

        Assert.Null(result);
    }

    /// <summary>
    /// Tests that DeleteAsync invokes DeleteCredentialAsync on the underlying store with key prefix.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteAsync_InvokesDeleteCredentialWithPrefix()
    {
        await _dataStore.DeleteAsync<TestToken>("user");

        _credentialStoreMock.Verify(
            s => s.DeleteCredentialAsync("GoogleDrive.user", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that ClearAsync deletes the broker user key from the credential store.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ClearAsync_DeletesBrokerUserKey()
    {
        await _dataStore.ClearAsync();

        _credentialStoreMock.Verify(
            s => s.DeleteCredentialAsync("GoogleDrive.user", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class TestToken
    {
        public string? AccessToken { get; set; }

        public string? TokenType { get; set; }
    }
}
