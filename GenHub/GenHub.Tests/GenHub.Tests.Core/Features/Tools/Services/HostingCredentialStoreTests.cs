using GenHub.Core.Interfaces.Publishers;
using GenHub.Features.Tools.Services.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Services;

/// <summary>
/// Unit tests for <see cref="HostingCredentialStore"/>.
/// </summary>
public sealed class HostingCredentialStoreTests : IDisposable
{
    private readonly string _testBaseDir;
    private readonly Mock<ILogger<HostingCredentialStore>> _loggerMock = new();
    private readonly HostingCredentialStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostingCredentialStoreTests"/> class.
    /// </summary>
    public HostingCredentialStoreTests()
    {
        _testBaseDir = Path.Combine(Path.GetTempPath(), "genhub_cred_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testBaseDir);
        _store = new HostingCredentialStore(_loggerMock.Object, _testBaseDir);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_testBaseDir))
        {
            try
            {
                Directory.Delete(_testBaseDir, true);
            }
            catch (IOException)
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Tests that saving and retrieving a credential round-trips correctly and returns the exact token.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveCredentialAsync_And_GetCredentialAsync_RoundTripsSecurely()
    {
        const string providerId = "github";
        const string secretToken = "ghp_secure_personal_access_token_1234567890";

        var saveResult = await _store.SaveCredentialAsync(providerId, secretToken);
        Assert.True(saveResult);

        var retrieved = await _store.GetCredentialAsync(providerId);
        Assert.Equal(secretToken, retrieved);
    }

    /// <summary>
    /// Tests that getting a non-existent credential returns null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCredentialAsync_WhenNotStored_ReturnsNull()
    {
        var retrieved = await _store.GetCredentialAsync("nonexistent_provider");
        Assert.Null(retrieved);
    }

    /// <summary>
    /// Tests that deleting a credential removes it so subsequent retrieval returns null.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteCredentialAsync_RemovesStoredToken()
    {
        const string providerId = "dropbox";
        const string token = "sl.dropbox_secret_token_abcdef";

        await _store.SaveCredentialAsync(providerId, token);
        var retrievedBefore = await _store.GetCredentialAsync(providerId);
        Assert.Equal(token, retrievedBefore);

        var deleteResult = await _store.DeleteCredentialAsync(providerId);
        Assert.True(deleteResult);

        var retrievedAfter = await _store.GetCredentialAsync(providerId);
        Assert.Null(retrievedAfter);
    }

    /// <summary>
    /// Tests that deleting a non-existent credential returns false without throwing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteCredentialAsync_WhenNotStored_ReturnsFalse()
    {
        var deleteResult = await _store.DeleteCredentialAsync("never_saved_provider");
        Assert.False(deleteResult);
    }

    /// <summary>
    /// Tests that invalid arguments throw an ArgumentException.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveCredentialAsync_WithInvalidArgs_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveCredentialAsync(string.Empty, "token"));
        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveCredentialAsync("provider", string.Empty));
    }
}
