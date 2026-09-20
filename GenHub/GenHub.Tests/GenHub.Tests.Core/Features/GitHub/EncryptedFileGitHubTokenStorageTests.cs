using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.GitHub.Services;
using GenHub.Tests.Core.Collections;
using Moq;
using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GitHub;

/// <summary>
/// Contains unit tests for <see cref="EncryptedFileGitHubTokenStorage"/>.
/// </summary>
[Collection(StorageMigrationStaticStateCollection.Name)]
public class EncryptedFileGitHubTokenStorageTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _defaultRootDir;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedFileGitHubTokenStorageTests"/> class.
    /// </summary>
    public EncryptedFileGitHubTokenStorageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubTokenStorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _defaultRootDir = Path.Combine(Path.GetTempPath(), "GenHubTokenStorageDefaultRootTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_defaultRootDir);
        StorageMigrationService.SetDefaultDataRootOverrideForTesting(_defaultRootDir);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StorageMigrationService.SetDefaultDataRootOverrideForTesting(null);
        DeleteDirectoryBestEffort(_tempDir);
        DeleteDirectoryBestEffort(_defaultRootDir);
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
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(TokenFilePath()));
        }

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
    /// Verifies that loading a file with a valid header but corrupt ciphertext drops the token.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithTamperedCiphertext_DeletesTokenAndReturnsNullAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-tampered");
        var headerLength = 1 + GitHubConstants.TokenFileNonceSizeBytes + GitHubConstants.TokenFileTagSizeBytes;
        var tampered = new byte[headerLength + 16];
        tampered[0] = GitHubConstants.TokenFileFormatVersion;
        RandomNumberGenerator.Fill(tampered.AsSpan(1));
        await File.WriteAllBytesAsync(TokenFilePath(), tampered);

        // Act
        var loaded = await storage.LoadTokenAsync();

        // Assert
        Assert.Null(loaded);
        Assert.False(storage.HasToken());
    }

    /// <summary>
    /// Verifies that loading preserves the token file when the machine secret came from a fallback source.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithFallbackSecret_PreservesTokenFileAsync()
    {
        // Arrange
        var storage = CreateStorage("fallback-secret", fromPrimarySource: false);
        await File.WriteAllBytesAsync(TokenFilePath(), [0x01, 0x02, 0x03]);

        // Act
        var loaded = await storage.LoadTokenAsync();

        // Assert
        Assert.Null(loaded);
        Assert.True(storage.HasToken());
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

    /// <summary>
    /// Verifies that a token saved under the fallback secret loads once the primary source resolves.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_SavedUnderFallbackSecret_LoadsWhenPrimaryResolvesAsync()
    {
        // Arrange
        var writer = CreateStorage(EncryptedFileGitHubTokenStorage.GetFallbackMachineSecret(), fromPrimarySource: false);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");
        await writer.SaveTokenAsync(token);
        var reader = CreateStorage("primary-machine-secret");

        // Act
        using var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
        Assert.True(reader.HasToken());
    }

    /// <summary>
    /// Verifies that a token stored only in the default data root is found from a relocated data directory.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithOnlyFallbackCopy_LoadsFromFallbackAsync()
    {
        // Arrange
        var writer = CreateStorage("machine-secret-fallback", appDataDir: _defaultRootDir);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");
        await writer.SaveTokenAsync(token);
        var reader = CreateStorage("machine-secret-fallback");

        // Act
        using var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.True(reader.HasToken());
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that the primary token copy wins when both the primary and fallback copies exist.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithBothCopies_PrefersPrimaryAsync()
    {
        // Arrange
        var fallbackWriter = CreateStorage("machine-secret-precedence", appDataDir: _defaultRootDir);
        using var fallbackToken = SecureStringHelper.ToSecureString("fallback-token-value");
        await fallbackWriter.SaveTokenAsync(fallbackToken);
        var primaryWriter = CreateStorage("machine-secret-precedence");
        using var primaryToken = SecureStringHelper.ToSecureString("primary-token-value");
        await primaryWriter.SaveTokenAsync(primaryToken);

        // Act
        using var loaded = await primaryWriter.LoadTokenAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("primary-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that deleting removes both the primary and fallback token copies.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteToken_WithBothCopies_RemovesBothAsync()
    {
        // Arrange
        var fallbackWriter = CreateStorage("machine-secret-dual-delete", appDataDir: _defaultRootDir);
        using var fallbackToken = SecureStringHelper.ToSecureString("fallback-token-value");
        await fallbackWriter.SaveTokenAsync(fallbackToken);
        var primaryWriter = CreateStorage("machine-secret-dual-delete");
        using var primaryToken = SecureStringHelper.ToSecureString("primary-token-value");
        await primaryWriter.SaveTokenAsync(primaryToken);

        // Act
        await primaryWriter.DeleteTokenAsync();

        // Assert
        Assert.False(primaryWriter.HasToken());
        Assert.False(File.Exists(TokenFilePath()));
        Assert.False(File.Exists(TokenFilePath(_defaultRootDir)));
    }

    private static void DeleteDirectoryBestEffort(string directory)
    {
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of the temp directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of the temp directory.
        }
    }

    private EncryptedFileGitHubTokenStorage CreateStorage(string machineSecret, bool fromPrimarySource = true, string? appDataDir = null)
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(x => x.GetApplicationDataPath()).Returns(appDataDir ?? _tempDir);
        return new TestStorage(configuration.Object, machineSecret, fromPrimarySource);
    }

    private string TokenFilePath(string? appDataDir = null)
    {
        return Path.Combine(appDataDir ?? _tempDir, AppConstants.TokenFileName);
    }

    private sealed class TestStorage(IConfigurationProviderService configurationProvider, string machineSecret, bool fromPrimarySource = true)
        : EncryptedFileGitHubTokenStorage(configurationProvider)
    {
        protected override (string Secret, bool FromPrimarySource) ResolveMachineSecret()
        {
            return (machineSecret, fromPrimarySource);
        }
    }
}
