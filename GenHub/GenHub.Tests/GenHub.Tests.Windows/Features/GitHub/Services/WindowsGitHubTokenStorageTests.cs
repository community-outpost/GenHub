using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Tests.Shared;
using GenHub.Tests.Windows.Infrastructure.DependencyInjection;
using GenHub.Windows.Features.GitHub.Services;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Windows.Features.GitHub.Services;

/// <summary>
/// Contains unit tests for <see cref="WindowsGitHubTokenStorage"/>.
/// </summary>
[Collection(ApplicationCompositionCollection.Name)]
public class WindowsGitHubTokenStorageTests : IDisposable
{
    private readonly TemporaryApplicationEnvironment _environment;
    private readonly string _tempDir;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsGitHubTokenStorageTests"/> class.
    /// </summary>
    public WindowsGitHubTokenStorageTests()
    {
        _environment = new TemporaryApplicationEnvironment();
        _tempDir = Path.Combine(Path.GetTempPath(), "GenHubWindowsTokenStorageTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        // Pin the default data root explicitly: composition tests in this collection build the
        // full provider, which installs a resolver closure that would otherwise win over the
        // isolated environment above depending on test order.
        StorageMigrationService.SetConfiguredDataPathResolver(() => _environment.AppDataPath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StorageMigrationService.SetConfiguredDataPathResolver(null);
        DeleteDirectoryBestEffort(_tempDir);
        ((IDisposable)_environment).Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Verifies that a saved token round-trips through DPAPI protected storage.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveAndLoadToken_RoundTripsSecretAsync()
    {
        // Arrange
        var storage = CreateStorage(_tempDir);
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
    /// Verifies that saving leaves no temporary files behind after the atomic rename.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_LeavesNoTempFilesAsync()
    {
        // Arrange
        var storage = CreateStorage(_tempDir);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");

        // Act
        await storage.SaveTokenAsync(token);
        using var overwrite = SecureStringHelper.ToSecureString("oauth-token-value-2");
        await storage.SaveTokenAsync(overwrite);

        // Assert
        var files = Directory.GetFiles(_tempDir).Select(Path.GetFileName).ToList();
        Assert.Equal([AppConstants.TokenFileName], files);
        using var loaded = await storage.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value-2", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that loading without a stored token returns null.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithoutStoredToken_ReturnsNullAsync()
    {
        // Arrange
        var storage = CreateStorage(_tempDir);

        // Act & Assert
        Assert.False(storage.HasToken());
        Assert.Null(await storage.LoadTokenAsync());
    }

    /// <summary>
    /// Verifies that a token stored only in the default data root is found from a relocated data directory.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithOnlyFallbackCopy_LoadsFromFallbackAsync()
    {
        // Arrange
        var writer = CreateStorage(_environment.AppDataPath);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");
        await writer.SaveTokenAsync(token);
        var reader = CreateStorage(_tempDir);

        // Act
        using var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.True(reader.HasToken());
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that deleting removes both the primary and fallback token copies.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteToken_WithBothCopies_RemovesBothAsync()
    {
        // Arrange
        var fallbackWriter = CreateStorage(_environment.AppDataPath);
        using var fallbackToken = SecureStringHelper.ToSecureString("fallback-token-value");
        await fallbackWriter.SaveTokenAsync(fallbackToken);
        var storage = CreateStorage(_tempDir);
        using var primaryToken = SecureStringHelper.ToSecureString("primary-token-value");
        await storage.SaveTokenAsync(primaryToken);

        // Act
        await storage.DeleteTokenAsync();

        // Assert
        Assert.False(storage.HasToken());
        Assert.False(File.Exists(TokenFilePath(_tempDir)));
        Assert.False(File.Exists(TokenFilePath(_environment.AppDataPath)));
    }

    /// <summary>
    /// Verifies that deleting without a stored token succeeds.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteToken_WithoutStoredToken_SucceedsAsync()
    {
        // Arrange
        var storage = CreateStorage(_tempDir);

        // Act
        await storage.DeleteTokenAsync();

        // Assert
        Assert.False(storage.HasToken());
    }

    /// <summary>
    /// Verifies that saving a null or empty token throws.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_WithNullOrEmpty_ThrowsAsync()
    {
        // Arrange
        var storage = CreateStorage(_tempDir);
        using var empty = new SecureString();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveTokenAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => storage.SaveTokenAsync(empty));
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

    private static WindowsGitHubTokenStorage CreateStorage(string appDataDir)
    {
        var configuration = new Mock<IConfigurationProviderService>();
        configuration.Setup(x => x.GetApplicationDataPath()).Returns(appDataDir);
        return new WindowsGitHubTokenStorage(configuration.Object);
    }

    private static string TokenFilePath(string appDataDir)
    {
        return Path.Combine(appDataDir, AppConstants.TokenFileName);
    }
}
