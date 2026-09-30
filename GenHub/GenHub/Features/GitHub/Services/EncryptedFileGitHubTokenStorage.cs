using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Models.Security;
using GenHub.Features.Workspace;
using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GitHub.Services;

/// <summary>
/// GitHub token storage that persists the token AES-GCM encrypted into a user-only file.
/// The encryption key is derived from a machine-bound secret, so a copied file alone is useless.
/// Serves as the shared base for the Linux and macOS implementations.
/// </summary>
public class EncryptedFileGitHubTokenStorage : IGitHubTokenStorage
{
    private readonly string _tokenFilePath;
    private readonly string? _fallbackTokenFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedFileGitHubTokenStorage"/> class.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider service.</param>
    public EncryptedFileGitHubTokenStorage(IConfigurationProviderService? configurationProvider = null)
    {
        var appData = configurationProvider?.GetApplicationDataPath()
            ?? AppDataPathHelper.GetDataRoot();
        Directory.CreateDirectory(appData);
        _tokenFilePath = GitHubTokenPathResolver.GetPrimaryTokenFilePath(appData);
        _fallbackTokenFilePath = GitHubTokenPathResolver.GetFallbackTokenFilePath(appData);
    }

    /// <inheritdoc />
    public async Task SaveTokenAsync(SecureString token)
    {
        if (token == null || token.Length == 0)
        {
            throw new ArgumentException("Token cannot be null or empty", nameof(token));
        }

        var plainBytes = Encoding.UTF8.GetBytes(SecureStringHelper.ToUnsecureString(token));
        var key = DeriveKey();
        try
        {
            var fileBytes = MachineBoundEncryption.Encrypt(plainBytes, key);
            await _fileLock.WaitAsync();
            try
            {
                // Write to a temp file and rename so a concurrent or crashing reader
                // never observes a truncated token file.
                var directory = Path.GetDirectoryName(_tokenFilePath)!;
                var tempPath = Path.Combine(directory, $"{AppConstants.TokenFileName}.{Guid.NewGuid():N}.tmp");
                await using (var stream = OpenRestrictedWriteStream(tempPath))
                {
                    await stream.WriteAsync(fileBytes);
                }

                var moved = false;
                try
                {
                    await FileOperationsService.MoveFileWithRetryAsync(tempPath, _tokenFilePath).ConfigureAwait(false);
                    moved = true;
                    RestrictFilePermissions(_tokenFilePath);
                    GitHubTokenPathResolver.DeleteFallbackCopyBestEffort(_fallbackTokenFilePath);
                }
                finally
                {
                    if (!moved)
                    {
                        FileOperationsService.DeleteFileIfExists(tempPath);
                    }
                }
            }
            finally
            {
                _fileLock.Release();
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <inheritdoc />
    public async Task<SecureString?> LoadTokenAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var (secret, fromPrimarySource) = ResolveMachineSecret();

            // Try the primary copy first, then the fallback copy, so a corrupt primary
            // does not hide a valid fallback until the next load.
            foreach (var candidate in GitHubTokenPathResolver.GetExistingTokenFilePaths(_tokenFilePath, _fallbackTokenFilePath))
            {
                var loaded = await TryLoadCandidateAsync(candidate, secret, fromPrimarySource);
                if (loaded != null)
                {
                    return loaded;
                }
            }

            return null;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task DeleteTokenAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            DeleteTokenFile(_tokenFilePath);
            if (_fallbackTokenFilePath != null)
            {
                DeleteTokenFile(_fallbackTokenFilePath);
            }
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public bool HasToken()
    {
        return GitHubTokenPathResolver.ResolveActiveTokenFilePath(_tokenFilePath, _fallbackTokenFilePath) != null;
    }

    /// <summary>
    /// Gets the fallback machine secret used when the primary platform source is unavailable.
    /// </summary>
    /// <returns>The machine and user name based fallback secret.</returns>
    internal static string GetFallbackMachineSecret()
    {
        return MachineBoundEncryption.GetFallbackMachineSecret();
    }

    /// <summary>
    /// Resolves the machine-bound secret used for key derivation.
    /// </summary>
    /// <returns>The machine secret and whether it came from the primary platform source.</returns>
    protected virtual (string Secret, bool FromPrimarySource) ResolveMachineSecret()
    {
        var machineSecret = MachineBoundEncryption.ResolveMachineSecret();
        return (machineSecret.Secret, machineSecret.FromPrimarySource);
    }

    private static void DeleteTokenFile(string tokenFilePath)
    {
        FileOperationsService.DeleteFileIfExists(tokenFilePath);
    }

    private static async Task<SecureString?> TryLoadCandidateAsync(string candidatePath, string secret, bool fromPrimarySource)
    {
        var fileBytes = await File.ReadAllBytesAsync(candidatePath);

        // A token saved while the primary source was unavailable used the fallback secret, so
        // the fallback is retried before the file is treated as corrupt.
        var decrypted = MachineBoundEncryption.TryDecryptWithSecret(
            fileBytes,
            new MachineSecret(secret, fromPrimarySource),
            GitHubConstants.TokenFileKeySalt,
            out var plainBytes);

        if (!decrypted || plainBytes == null)
        {
            // Only drop the file when the secret came from its primary source. A fallback
            // secret may indicate a transient lookup failure, in which case deleting would
            // destroy a healthy token and force an avoidable re-authentication.
            // The lock serializes this delete against concurrent saves, so a racing
            // truncate-then-write can never be mistaken for corruption.
            if (fromPrimarySource)
            {
                DeleteTokenFile(candidatePath);
            }

            return null;
        }

        try
        {
            return SecureStringHelper.ToSecureString(Encoding.UTF8.GetString(plainBytes));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    private static FileStream OpenRestrictedWriteStream(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        }

        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        };
        return new FileStream(path, options);
    }

    private static void RestrictFilePermissions(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private byte[] DeriveKey()
    {
        return MachineBoundEncryption.DeriveKey(ResolveMachineSecret().Secret, GitHubConstants.TokenFileKeySalt);
    }
}
