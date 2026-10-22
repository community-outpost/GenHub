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
    /// Verifies that a token file written by the token storage before its crypto moved into
    /// MachineBoundEncryption still loads. The blob was produced by the pre-refactor
    /// EncryptToFileBytes and DeriveKeyFromSecret code from development.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_FileFromPreRefactorFormat_LoadsAsync()
    {
        // Arrange
        const string preRefactorTokenFile = "AZVez7DY44IGJz677HRWxqoat7QbXw3CSVCJlF/1Ew2HokaI6mh5IW4TJOijp2A1ml8uO3VJ2DGohIA=";
        await File.WriteAllBytesAsync(TokenFilePath(), Convert.FromBase64String(preRefactorTokenFile));
        var storage = CreateStorage("known-answer-machine-secret");

        // Act
        using var loaded = await storage.LoadTokenAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("ghp_KnownAnswerToken0123456789", SecureStringHelper.ToUnsecureString(loaded));
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
    /// Verifies that an oversized token file is rejected without loading it.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithOversizedTokenFile_ReturnsNullAsync()
    {
        // Arrange
        var storage = CreateStorage("machine-secret-oversized", fromPrimarySource: false);
        await File.WriteAllBytesAsync(TokenFilePath(), new byte[SecureTokenFileConstants.MaxTokenFileBytes + 1]);

        // Act
        using var loaded = await storage.LoadTokenAsync();

        // Assert
        Assert.Null(loaded);
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
    /// Verifies that a fallback copy stays readable after the data directory moves.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_AfterDataRelocation_LoadsFallbackCopyWithItsOwnKeyAsync()
    {
        // Arrange: the token was saved in the default root, with its key file
        // beside it, before the user relocated the data directory. The real
        // fallback secret is used so the save matches production derivation.
        var fallbackSecret = EncryptedFileGitHubTokenStorage.GetFallbackMachineSecret();
        var writer = CreateStorage(fallbackSecret, fromPrimarySource: false, appDataDir: _defaultRootDir);
        using var token = SecureStringHelper.ToSecureString("relocated-token-value");
        await writer.SaveTokenAsync(token);
        var relocatedDir = Path.Combine(_tempDir, "relocated");
        Directory.CreateDirectory(relocatedDir);
        var reader = CreateStorage(fallbackSecret, fromPrimarySource: false, appDataDir: relocatedDir);

        // Act
        using var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("relocated-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that saving a primary token deletes an obsolete fallback token copy.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_WithFallbackCopy_DeletesFallbackCopyAsync()
    {
        // Arrange
        var fallbackWriter = CreateStorage("machine-secret-cleanup", appDataDir: _defaultRootDir);
        using var fallbackToken = SecureStringHelper.ToSecureString("fallback-token-value");
        await fallbackWriter.SaveTokenAsync(fallbackToken);
        Assert.True(File.Exists(TokenFilePath(_defaultRootDir)));

        var primaryWriter = CreateStorage("machine-secret-cleanup");
        using var primaryToken = SecureStringHelper.ToSecureString("primary-token-value");

        // Act
        await primaryWriter.SaveTokenAsync(primaryToken);

        // Assert
        Assert.True(File.Exists(TokenFilePath()));
        Assert.False(File.Exists(TokenFilePath(_defaultRootDir)));
        using var loaded = await primaryWriter.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("primary-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that the primary token copy wins when both the primary and fallback copies exist.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WithBothCopies_PrefersPrimaryAsync()
    {
        // Arrange
        var primaryWriter = CreateStorage("machine-secret-precedence");
        using var primaryToken = SecureStringHelper.ToSecureString("primary-token-value");
        await primaryWriter.SaveTokenAsync(primaryToken);
        var fallbackWriter = CreateStorage("machine-secret-precedence", appDataDir: _defaultRootDir);
        using var fallbackToken = SecureStringHelper.ToSecureString("fallback-token-value");
        await fallbackWriter.SaveTokenAsync(fallbackToken);

        // Act
        using var loaded = await primaryWriter.LoadTokenAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("primary-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that a corrupt primary copy is dropped while the valid fallback copy is recovered in the same load.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WhenPrimaryCorrupt_RecoversFallbackAsync()
    {
        // Arrange
        var fallbackWriter = CreateStorage("machine-secret-recovery", appDataDir: _defaultRootDir);
        using var fallbackToken = SecureStringHelper.ToSecureString("fallback-token-value");
        await fallbackWriter.SaveTokenAsync(fallbackToken);
        await File.WriteAllBytesAsync(TokenFilePath(), [0x01, 0x02, 0x03]);
        var reader = CreateStorage("machine-secret-recovery");

        // Act
        using var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("fallback-token-value", SecureStringHelper.ToUnsecureString(loaded));
        Assert.False(File.Exists(TokenFilePath()));
        Assert.True(File.Exists(TokenFilePath(_defaultRootDir)));
    }

    /// <summary>
    /// Verifies that corrupt primary and fallback copies are both dropped and the load returns null.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_WhenBothCopiesCorrupt_DeletesBothAndReturnsNullAsync()
    {
        // Arrange
        await File.WriteAllBytesAsync(TokenFilePath(), [0x01, 0x02, 0x03]);
        await File.WriteAllBytesAsync(TokenFilePath(_defaultRootDir), [0x04, 0x05, 0x06]);
        var reader = CreateStorage("machine-secret-dual-corrupt");

        // Act
        var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.Null(loaded);
        Assert.False(File.Exists(TokenFilePath()));
        Assert.False(File.Exists(TokenFilePath(_defaultRootDir)));
        Assert.False(reader.HasToken());
    }

    /// <summary>
    /// Verifies that deleting removes both the primary and fallback token copies.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task DeleteToken_WithBothCopies_RemovesBothAsync()
    {
        // Arrange
        var primaryWriter = CreateStorage("machine-secret-dual-delete");
        using var primaryToken = SecureStringHelper.ToSecureString("primary-token-value");
        await primaryWriter.SaveTokenAsync(primaryToken);
        var fallbackWriter = CreateStorage("machine-secret-dual-delete", appDataDir: _defaultRootDir);
        using var fallbackToken = SecureStringHelper.ToSecureString("fallback-token-value");
        await fallbackWriter.SaveTokenAsync(fallbackToken);

        // Act
        await primaryWriter.DeleteTokenAsync();

        // Assert
        Assert.False(primaryWriter.HasToken());
        Assert.False(File.Exists(TokenFilePath()));
        Assert.False(File.Exists(TokenFilePath(_defaultRootDir)));
    }

    /// <summary>
    /// Verifies that saving succeeds when the stale fallback copy is locked, leaving the primary token persisted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveTokenAsync_WhenFallbackLocked_SucceedsAndKeepsPrimaryAsync()
    {
        if (!OperatingSystem.IsWindows())
        {
            // Deleting an open file succeeds on Unix; only Windows denies it with a sharing violation.
            return;
        }

        // Arrange
        var fallbackWriter = CreateStorage("machine-secret-locked", appDataDir: _defaultRootDir);
        using var staleToken = SecureStringHelper.ToSecureString("stale-token-value");
        await fallbackWriter.SaveTokenAsync(staleToken);
        var storage = CreateStorage("machine-secret-locked");
        using var primaryToken = SecureStringHelper.ToSecureString("primary-token-value");
        using var lockStream = new FileStream(TokenFilePath(_defaultRootDir), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None);
        Assert.NotNull(lockStream);

        // Act
        await storage.SaveTokenAsync(primaryToken);

        // Assert
        Assert.True(File.Exists(TokenFilePath()));
        Assert.True(File.Exists(TokenFilePath(_defaultRootDir)));
        using var loaded = await storage.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("primary-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that a token saved under the fallback secret round-trips through the key-strengthened file.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveAndLoadToken_WithFallbackSecret_RoundTripsAsync()
    {
        // Arrange
        var storage = CreateStorage("fallback-secret-roundtrip", fromPrimarySource: false);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");

        // Act
        await storage.SaveTokenAsync(token);

        // Assert
        Assert.True(File.Exists(KeyFilePath()));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(KeyFilePath()));
        }

        using var loaded = await storage.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that a token file copied without its key file cannot be decrypted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_CopiedWithoutKeyFile_ReturnsNullAsync()
    {
        // Arrange
        var writer = CreateStorage("fallback-secret-copy", fromPrimarySource: false);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");
        await writer.SaveTokenAsync(token);
        var copiedDir = Path.Combine(_tempDir, "copied");
        Directory.CreateDirectory(copiedDir);
        File.Copy(TokenFilePath(), Path.Combine(copiedDir, AppConstants.TokenFileName));
        var reader = CreateStorage("fallback-secret-copy", fromPrimarySource: false, appDataDir: copiedDir);

        // Act
        var loaded = await reader.LoadTokenAsync();

        // Assert
        Assert.Null(loaded);
        Assert.True(File.Exists(Path.Combine(copiedDir, AppConstants.TokenFileName)));
    }

    /// <summary>
    /// Verifies that a token saved with the legacy raw fallback derivation still loads.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task LoadToken_SavedUnderLegacyFallbackSecret_LoadsAsync()
    {
        // Arrange
        await File.WriteAllBytesAsync(TokenFilePath(), EncryptLegacyToken("oauth-token-value"));
        var storage = CreateStorage("fallback-secret-legacy", fromPrimarySource: false);

        // Act
        using var loaded = await storage.LoadTokenAsync();

        // Assert
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that saving reuses a pre-existing key file instead of overwriting it.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_WithExistingKeyFile_ReusesKeyWithoutOverwriteAsync()
    {
        // Arrange
        var keyBytes = new byte[GitHubConstants.TokenFileKeySizeBytes];
        RandomNumberGenerator.Fill(keyBytes);
        await File.WriteAllBytesAsync(KeyFilePath(), keyBytes);
        var storage = CreateStorage("fallback-secret-reuse", fromPrimarySource: false);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");

        // Act
        await storage.SaveTokenAsync(token);

        // Assert
        Assert.Equal(keyBytes, await File.ReadAllBytesAsync(KeyFilePath()));
        using var loaded = await storage.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that a truncated key file is replaced instead of breaking saves forever.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_WithTruncatedKeyFile_ReplacesKeyAndRoundTripsAsync()
    {
        // Arrange
        await File.WriteAllBytesAsync(KeyFilePath(), [0x01, 0x02, 0x03, 0x04, 0x05]);
        var storage = CreateStorage("fallback-secret-truncated", fromPrimarySource: false);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");

        // Act
        await storage.SaveTokenAsync(token);

        // Assert
        Assert.Equal(GitHubConstants.TokenFileKeySizeBytes, new FileInfo(KeyFilePath()).Length);
        using var loaded = await storage.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
    }

    /// <summary>
    /// Verifies that an oversized key file is replaced instead of breaking saves forever.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SaveToken_WithOversizedKeyFile_ReplacesKeyAndRoundTripsAsync()
    {
        // Arrange
        var oversized = new byte[GitHubConstants.TokenFileKeySizeBytes + 16];
        RandomNumberGenerator.Fill(oversized);
        await File.WriteAllBytesAsync(KeyFilePath(), oversized);
        var storage = CreateStorage("fallback-secret-oversized", fromPrimarySource: false);
        using var token = SecureStringHelper.ToSecureString("oauth-token-value");

        // Act
        await storage.SaveTokenAsync(token);

        // Assert
        Assert.Equal(GitHubConstants.TokenFileKeySizeBytes, new FileInfo(KeyFilePath()).Length);
        using var loaded = await storage.LoadTokenAsync();
        Assert.NotNull(loaded);
        Assert.Equal("oauth-token-value", SecureStringHelper.ToUnsecureString(loaded));
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

    private static byte[] EncryptLegacyToken(string value)
    {
        var salt = Encoding.UTF8.GetBytes(GitHubConstants.TokenFileKeySalt);
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(EncryptedFileGitHubTokenStorage.GetFallbackMachineSecret()),
            salt,
            GitHubConstants.TokenFileKeyIterations,
            HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(GitHubConstants.TokenFileKeySizeBytes);
        var nonce = RandomNumberGenerator.GetBytes(GitHubConstants.TokenFileNonceSizeBytes);
        var plain = Encoding.UTF8.GetBytes(value);
        var cipher = new byte[plain.Length];
        var tag = new byte[GitHubConstants.TokenFileTagSizeBytes];
        using (var aes = new AesGcm(key, GitHubConstants.TokenFileTagSizeBytes))
        {
            aes.Encrypt(nonce, plain, cipher, tag);
        }

        var headerLength = 1 + nonce.Length + tag.Length;
        var fileBytes = new byte[headerLength + cipher.Length];
        fileBytes[0] = GitHubConstants.TokenFileFormatVersion;
        Buffer.BlockCopy(nonce, 0, fileBytes, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, fileBytes, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(cipher, 0, fileBytes, headerLength, cipher.Length);
        return fileBytes;
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

    private string KeyFilePath()
    {
        return Path.Combine(_tempDir, AppConstants.TokenFileName + SecureTokenFileConstants.TokenFileKeySuffix);
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
