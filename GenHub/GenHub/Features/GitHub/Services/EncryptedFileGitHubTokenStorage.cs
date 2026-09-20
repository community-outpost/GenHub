using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Features.Workspace;
using System;
using System.ComponentModel;
using System.Diagnostics;
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
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.AppName);
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
            var fileBytes = EncryptToFileBytes(plainBytes, key);
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
                    File.Move(tempPath, _tokenFilePath, overwrite: true);
                    moved = true;
                    RestrictFilePermissions(_tokenFilePath);
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
            var activePath = GitHubTokenPathResolver.ResolveActiveTokenFilePath(_tokenFilePath, _fallbackTokenFilePath);
            if (activePath == null)
            {
                return null;
            }

            var fileBytes = await File.ReadAllBytesAsync(activePath);
            var (secret, fromPrimarySource) = ResolveMachineSecret();
            byte[]? plainBytes = null;
            var decrypted = TryDecryptWithSecret(fileBytes, secret, out plainBytes);
            if (!decrypted && fromPrimarySource)
            {
                // The token may have been saved while the primary source was unavailable and the
                // fallback secret was used instead. Retry with the fallback secret before treating
                // the file as corrupt, so a transient save-time lookup failure cannot destroy it.
                decrypted = TryDecryptWithSecret(fileBytes, GetFallbackMachineSecret(), out plainBytes);
            }

            if (!decrypted || plainBytes == null)
            {
                // Only drop the file when the secret came from its primary source. A fallback
                // secret may indicate a transient lookup failure, in which case deleting would
                // destroy a healthy token and force an avoidable re-authentication.
                // The lock serializes this delete against concurrent saves, so a racing
                // truncate-then-write can never be mistaken for corruption.
                if (fromPrimarySource)
                {
                    DeleteTokenFile(activePath);
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
        return $"{Environment.MachineName}:{Environment.UserName}";
    }

    /// <summary>
    /// Resolves the machine-bound secret used for key derivation.
    /// </summary>
    /// <returns>The machine secret and whether it came from the primary platform source.</returns>
    protected virtual (string Secret, bool FromPrimarySource) ResolveMachineSecret()
    {
        if (OperatingSystem.IsLinux())
        {
            var machineId = ReadMachineIdFile(GitHubConstants.LinuxMachineIdPath)
                ?? ReadMachineIdFile(GitHubConstants.LinuxMachineIdFallbackPath);
            if (!string.IsNullOrEmpty(machineId))
            {
                return (machineId, true);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var platformUuid = TryGetMacOsPlatformUuid();
            if (!string.IsNullOrEmpty(platformUuid))
            {
                return (platformUuid, true);
            }
        }

        return (GetFallbackMachineSecret(), false);
    }

    private static void DeleteTokenFile(string tokenFilePath)
    {
        FileOperationsService.DeleteFileIfExists(tokenFilePath);
    }

    private static byte[] EncryptToFileBytes(byte[] plainBytes, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(GitHubConstants.TokenFileNonceSizeBytes);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[GitHubConstants.TokenFileTagSizeBytes];
        using (var aes = new AesGcm(key, GitHubConstants.TokenFileTagSizeBytes))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var headerLength = 1 + nonce.Length + tag.Length;
        var fileBytes = new byte[headerLength + cipherBytes.Length];
        fileBytes[0] = GitHubConstants.TokenFileFormatVersion;
        Buffer.BlockCopy(nonce, 0, fileBytes, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, fileBytes, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, fileBytes, headerLength, cipherBytes.Length);
        return fileBytes;
    }

    private static bool TryDecryptWithSecret(byte[] fileBytes, string secret, out byte[]? plainBytes)
    {
        var key = DeriveKeyFromSecret(secret);
        try
        {
            return TryDecryptFileBytes(fileBytes, key, out plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static bool TryDecryptFileBytes(byte[] fileBytes, byte[] key, out byte[]? plainBytes)
    {
        plainBytes = null;
        var headerLength = 1 + GitHubConstants.TokenFileNonceSizeBytes + GitHubConstants.TokenFileTagSizeBytes;
        if (fileBytes.Length <= headerLength || fileBytes[0] != GitHubConstants.TokenFileFormatVersion)
        {
            return false;
        }

        try
        {
            var nonce = fileBytes.AsSpan(1, GitHubConstants.TokenFileNonceSizeBytes);
            var tag = fileBytes.AsSpan(1 + GitHubConstants.TokenFileNonceSizeBytes, GitHubConstants.TokenFileTagSizeBytes);
            var cipherBytes = fileBytes.AsSpan(headerLength);
            var decrypted = new byte[cipherBytes.Length];
            using (var aes = new AesGcm(key, GitHubConstants.TokenFileTagSizeBytes))
            {
                aes.Decrypt(nonce, cipherBytes, tag, decrypted);
            }

            plainBytes = decrypted;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
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

    private static string? ReadMachineIdFile(string path)
    {
        try
        {
            var contents = File.ReadAllText(path).Trim();
            return string.IsNullOrEmpty(contents) ? null : contents;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (SecurityException)
        {
            return null;
        }
    }

    private static string? TryGetMacOsPlatformUuid()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = GitHubConstants.MacOsIoRegCommand,
                Arguments = GitHubConstants.MacOsIoRegArguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null)
            {
                return null;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(GitHubConstants.MacOsIoRegTimeoutSeconds)))
            {
                KillProcessBestEffort(process);
                return null;
            }

            // The process has exited, so the remaining buffered output can be
            // drained without blocking on a full pipe.
            return ParseIoRegUuid(process.StandardOutput.ReadToEnd());
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void KillProcessBestEffort(Process process)
    {
        try
        {
            process.Kill();
        }
        catch (InvalidOperationException)
        {
            // The process already exited between the timeout and the kill.
        }
        catch (Win32Exception)
        {
            // Best effort cleanup of the timed-out child process.
        }
    }

    private static string? ParseIoRegUuid(string output)
    {
        var keyToken = $"\"{GitHubConstants.MacOsIoRegUuidKey}\"";
        var keyIndex = output.IndexOf(keyToken, StringComparison.Ordinal);
        if (keyIndex < 0)
        {
            return null;
        }

        var openQuote = output.IndexOf('"', keyIndex + keyToken.Length);
        if (openQuote < 0)
        {
            return null;
        }

        var closeQuote = output.IndexOf('"', openQuote + 1);
        if (closeQuote < 0)
        {
            return null;
        }

        var uuid = output.Substring(openQuote + 1, closeQuote - openQuote - 1).Trim();
        return string.IsNullOrEmpty(uuid) ? null : uuid;
    }

    private static byte[] DeriveKeyFromSecret(string secret)
    {
        var salt = Encoding.UTF8.GetBytes(GitHubConstants.TokenFileKeySalt);
        using var pbkdf2 = new Rfc2898DeriveBytes(secret, salt, GitHubConstants.TokenFileKeyIterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(GitHubConstants.TokenFileKeySizeBytes);
    }

    private byte[] DeriveKey()
    {
        return DeriveKeyFromSecret(ResolveMachineSecret().Secret);
    }
}
