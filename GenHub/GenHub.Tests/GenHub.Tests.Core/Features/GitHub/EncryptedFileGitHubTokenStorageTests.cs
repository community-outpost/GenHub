using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.GitHub.Services;
using Moq;
using System;
using System.IO;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GitHub;

/// <summary>
/// Contains unit tests for <see cref="EncryptedFileGitHubTokenStorage"/>.
/// </summary>
public class EncryptedFileGitHubTokenStorageTests : IDisposable
{
    private readonly string _tempDir;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedFileGitHubTokenStorageTests"/> class.
    /// </summary>
    public EncryptedFileGitHubTokenStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubTokenStorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp directory.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that a saved token round-trips through the encrypted file.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveAndLoadToken_RoundTripsSecretAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-a");
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");

        // Act
        await storage.SaveTokenAsync(token);

        // Assert
        Assert.True(storage.HasToken());
        using var loaded = await storage.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that the token file does not contain the plain text token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_DoesNotStorePlainTextAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-b");
        using var token = SecureStringHelper.ToSecureString("super-secret-token");

        // Act
        await storage.SaveTokenAsync(token);

        // Assert
        var fileBytes = await File.ReadAllBytesAsync(TokenFilePath());
        var plainToken = Encoding.UTF8.GetBytes("super-secret-token");
        Assert.True(fileBytes.AsSpan().IndexOf(plainToken) < 0);
        Assert.Equal(GitHubConstants.TokenFileFormatVersion, fileBytes[0]);
    }

    /// <summary>
    /// Verifies that loading with a different machine secret drops the token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithDifferentMachineSecret_DeletesTokenAndReturnsNullAsync()
    {
        // Arrange
        var writer = CreateStorage("machine-secret-c");
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");
        await writer.SaveTokenAsync(token);
        var reader = CreateStorage("other-machine-secret");

        // Act
        var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.Null(loaded);
        Assert.False(reader.HasToken());
    }

    /// <summary>
    /// Verifies that loading a corrupt file drops the token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithCorruptFile_DeletesTokenAndReturnsNullAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-d");
        await File.WriteAllBytesAsync(TokenFilePath(), [0x01, 0x02, 0x03]);

        // Act
        var loaded = await storage.LoadTokenAsync();

        // Assert
        Assert.Null(loaded);
        Assert.False(storage.HasToken());
    }

    /// <summary>
    /// Verifies that loading without a stored token returns null.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithoutStoredToken_ReturnsNullAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-e");

        // Act & Assert
        Assert.False(storage.HasToken());
        Assert.Null(await storage.LoadTokenAsync());
    }

    /// <summary>
    /// Verifies that deleting removes the stored token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteToken_RemovesStoredTokenAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-f");
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");
        await storage.SaveTokenAsync(token);

        // Act
        await storage.DeleteTokenAsync();

        // Assert
        Assert.False(storage.HasToken());
        Assert.Null(await storage.LoadTokenAsync());
    }

    /// <summary>
    /// Verifies that saving a null or empty token throws.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_WithNullOrEmpty_ThrowsAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-g");
        using var empty = new SecureString();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveTokenAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveTokenAsync(empty));
    }

    private EncryptedFileGitHubTokenStorage CreateStorage(string machineSecret)
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(x => x.GetApplicationDataPath()).Returns(_tempDir);
        return new TestStorage(configuration.Object, machineSecret);
    }

    private string TokenFilePath()
    {
        return Path.Combine(_tempDir, AppConstants.TokenFileName);
    }

    private sealed class TestStorage(IConfigurationProviderService configurationProvider, string machineSecret)
        : EncryptedFileGitHubTokenStorage(configurationProvider)
    {
        protected override string ResolveMachineSecret()
        {
            return machineSecret;
        }
    }
}
