using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Workspace;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Common.Services.SecureStorage;

/// <summary>
/// Single-token storage that persists the token AES-GCM encrypted into a user-only file.
/// The encryption key is derived from a machine-bound secret, so a copied file alone is useless.
/// When no platform secret exists, a per-install random key file strengthens the guessable
/// machine-name fallback so a lone copied token file stays useless there as well.
/// Serves as the shared base for the Linux and macOS token store implementations.
/// </summary>
public abstract class EncryptedFileTokenStorageBase
{
    private readonly string _tokenFilePath;
    private readonly string? _fallbackTokenFilePath;
    private readonly string? _fallbackTokenKeyFilePath;
    private readonly string _fallbackKeyFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedFileTokenStorageBase"/> class.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider service.</param>
    protected EncryptedFileTokenStorageBase(IConfigurationProviderService? configurationProvider = null)
    {
        var appData = configurationProvider?.GetApplicationDataPath()
            ?? AppDataPathHelper.GetDataRoot();
        Directory.CreateDirectory(appData);
        _tokenFilePath = FileTokenPathResolver.GetPrimaryTokenFilePath(appData, TokenFileName);
        _fallbackTokenFilePath = FileTokenPathResolver.GetFallbackTokenFilePath(appData, TokenFileName);
        _fallbackTokenKeyFilePath = _fallbackTokenFilePath is null
            ? null
            : Path.Combine(Path.GetDirectoryName(_fallbackTokenFilePath)!, TokenFileName + SecureTokenFileConstants.TokenFileKeySuffix);
        _fallbackKeyFilePath = Path.Combine(appData, TokenFileName + SecureTokenFileConstants.TokenFileKeySuffix);
    }

    /// <summary>
    /// Gets the token file name persisted inside the application data directory.
    /// </summary>
    protected abstract string TokenFileName { get; }

    /// <summary>
    /// Gets the domain separation salt for the token file key derivation.
    /// </summary>
    protected abstract string KeySalt { get; }

    /// <summary>
    /// Saves a token securely.
    /// </summary>
    /// <param name="token">The secure token to save.</param>
    /// <returns>A task representing the save operation.</returns>
    public async Task SaveTokenAsync(SecureString token)
    {
        if (token == null || token.Length == 0)
        {
            throw new ArgumentException("Token cannot be null or empty", nameof(token));
        }

        var plainBytes = Encoding.UTF8.GetBytes(SecureStringHelper.ToUnsecureString(token));
        await _fileLock.WaitAsync();
        try
        {
            // Derived under the lock: fallback saves create the per-install key
            // file, and concurrent saves must not mint competing keys.
            var key = await DeriveSaveKeyAsync();
            try
            {
                var fileBytes = EncryptToFileBytes(plainBytes, key);

                // Write to a temp file and rename so a concurrent or crashing reader
                // never observes a truncated token file.
                var directory = Path.GetDirectoryName(_tokenFilePath)!;
                var tempPath = Path.Combine(directory, $"{TokenFileName}.{Guid.NewGuid():N}.tmp");
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
                    FileTokenPathResolver.DeleteFallbackCopyBestEffort(_fallbackTokenFilePath);
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
                CryptographicOperations.ZeroMemory(key);
            }
        }
        finally
        {
            _fileLock.Release();
            CryptographicOperations.ZeroMemory(plainBytes);
        }
    }

    /// <summary>
    /// Loads the saved token.
    /// </summary>
    /// <returns>The secure token, or null if none saved.</returns>
    public async Task<SecureString?> LoadTokenAsync()
    {
        await _fileLock.WaitAsync();
        try
        {
            var (secret, fromPrimarySource) = ResolveMachineSecret();
            var secrets = ResolveDecryptionSecrets(secret, fromPrimarySource);

            // Try the primary copy first, then the fallback copy, so a corrupt primary
            // does not hide a valid fallback until the next load.
            foreach (var candidate in FileTokenPathResolver.GetExistingTokenFilePaths(_tokenFilePath, _fallbackTokenFilePath))
            {
                var loaded = await TryLoadCandidateAsync(candidate, secrets, fromPrimarySource, KeySalt);
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

    /// <summary>
    /// Deletes the saved token.
    /// </summary>
    /// <returns>A task representing the delete operation.</returns>
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

    /// <summary>
    /// Checks if a token is stored.
    /// </summary>
    /// <returns>True if a token is stored, false otherwise.</returns>
    public bool HasToken()
    {
        return FileTokenPathResolver.ResolveActiveTokenFilePath(_tokenFilePath, _fallbackTokenFilePath) != null;
    }

    /// <summary>
    /// Gets the fallback machine secret used when the primary platform source is unavailable.
    /// New saves strengthen this guessable value with the per-install key file; the raw
    /// value only decrypts legacy tokens saved before that key existed.
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
            var machineId = ReadMachineIdFile(SecureTokenFileConstants.LinuxMachineIdPath)
                ?? ReadMachineIdFile(SecureTokenFileConstants.LinuxMachineIdFallbackPath);
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

    private static async Task<SecureString?> TryLoadCandidateAsync(
        string candidatePath,
        IReadOnlyList<string> secrets,
        bool fromPrimarySource,
        string keySalt)
    {
        var fileBytes = await TryReadBoundedFileAsync(candidatePath);
        if (fileBytes is null)
        {
            return null;
        }

        // The token may have been saved under a different secret than the one
        // currently resolved: a transient primary outage at save time, or a
        // legacy fallback derivation from before the per-install key existed.
        // Every candidate is tried before the file counts as corrupt.
        foreach (var secret in secrets)
        {
            if (TryDecryptWithSecret(fileBytes, secret, keySalt, out var plainBytes) && plainBytes is not null)
            {
                try
                {
                    return SecureStringHelper.ToSecureString(Encoding.UTF8.GetString(plainBytes));
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
        }

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

    private static byte[] EncryptToFileBytes(byte[] plainBytes, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(SecureTokenFileConstants.TokenFileNonceSizeBytes);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[SecureTokenFileConstants.TokenFileTagSizeBytes];
        using (var aes = new AesGcm(key, SecureTokenFileConstants.TokenFileTagSizeBytes))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var headerLength = 1 + nonce.Length + tag.Length;
        var fileBytes = new byte[headerLength + cipherBytes.Length];
        fileBytes[0] = SecureTokenFileConstants.TokenFileFormatVersion;
        Buffer.BlockCopy(nonce, 0, fileBytes, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, fileBytes, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(cipherBytes, 0, fileBytes, headerLength, cipherBytes.Length);
        return fileBytes;
    }

    private static bool TryDecryptWithSecret(byte[] fileBytes, string secret, string keySalt, out byte[]? plainBytes)
    {
        var key = DeriveKeyFromSecret(secret, keySalt);
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
        var headerLength = 1 + SecureTokenFileConstants.TokenFileNonceSizeBytes + SecureTokenFileConstants.TokenFileTagSizeBytes;
        if (fileBytes.Length <= headerLength || fileBytes[0] != SecureTokenFileConstants.TokenFileFormatVersion)
        {
            return false;
        }

        try
        {
            var nonce = fileBytes.AsSpan(1, SecureTokenFileConstants.TokenFileNonceSizeBytes);
            var tag = fileBytes.AsSpan(1 + SecureTokenFileConstants.TokenFileNonceSizeBytes, SecureTokenFileConstants.TokenFileTagSizeBytes);
            var cipherBytes = fileBytes.AsSpan(headerLength);
            var decrypted = new byte[cipherBytes.Length];
            using (var aes = new AesGcm(key, SecureTokenFileConstants.TokenFileTagSizeBytes))
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

    private static FileStream OpenRestrictedWriteStream(string path, FileMode mode = FileMode.Create)
    {
        if (OperatingSystem.IsWindows())
        {
            return new FileStream(path, mode, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        }

        var options = new FileStreamOptions
        {
            Mode = mode,
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
                FileName = SecureTokenFileConstants.MacOsIoRegCommand,
                Arguments = SecureTokenFileConstants.MacOsIoRegArguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null)
            {
                return null;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(SecureTokenFileConstants.MacOsIoRegTimeoutSeconds)))
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
        var keyToken = $"\"{SecureTokenFileConstants.MacOsIoRegUuidKey}\"";
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

    private static byte[] DeriveKeyFromSecret(string secret, string keySalt)
    {
        var salt = Encoding.UTF8.GetBytes(keySalt);
        using var pbkdf2 = new Rfc2898DeriveBytes(secret, salt, SecureTokenFileConstants.TokenFileKeyIterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(SecureTokenFileConstants.TokenFileKeySizeBytes);
    }

    private static void AddUniqueSecret(List<string> secrets, string secret)
    {
        if (!secrets.Contains(secret))
        {
            secrets.Add(secret);
        }
    }

    private static string CombineFallbackSecret(string secret, byte[] keyFileBytes)
    {
        return secret + ":" + Convert.ToBase64String(keyFileBytes);
    }

    private static async Task<byte[]?> TryReadBoundedFileAsync(string candidatePath)
    {
        // Bound the read itself: a pre-read length check alone races with a
        // file that grows between the check and the read.
        try
        {
            await using var stream = new FileStream(
                candidatePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 4096, useAsync: true);
            using var content = new MemoryStream();
            var chunk = new byte[4096];
            while (true)
            {
                var read = await stream.ReadAsync(chunk).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (content.Length + read > SecureTokenFileConstants.MaxTokenFileBytes)
                {
                    return null;
                }

                await content.WriteAsync(chunk.AsMemory(0, read)).ConfigureAwait(false);
            }

            return content.ToArray();
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

    private static byte[]? TryReadKeyFile(string keyFilePath)
    {
        try
        {
            var info = new FileInfo(keyFilePath);
            if (!info.Exists || info.Length != SecureTokenFileConstants.TokenFileKeySizeBytes)
            {
                return null;
            }

            using var stream = new FileStream(
                keyFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[SecureTokenFileConstants.TokenFileKeySizeBytes];
            var success = false;
            try
            {
                var bytesRead = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
                if (bytesRead == SecureTokenFileConstants.TokenFileKeySizeBytes && stream.ReadByte() == -1)
                {
                    success = true;
                    return buffer;
                }

                return null;
            }
            finally
            {
                if (!success)
                {
                    CryptographicOperations.ZeroMemory(buffer);
                }
            }
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

    private async Task<byte[]> DeriveSaveKeyAsync()
    {
        var (secret, fromPrimarySource) = ResolveMachineSecret();
        if (fromPrimarySource)
        {
            return DeriveKeyFromSecret(secret, KeySalt);
        }

        var keyFileBytes = await EnsureFallbackKeyFileAsync();
        try
        {
            return DeriveKeyFromSecret(CombineFallbackSecret(secret, keyFileBytes), KeySalt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyFileBytes);
        }
    }

    private IReadOnlyList<string> ResolveDecryptionSecrets(string secret, bool fromPrimarySource)
    {
        var secrets = new List<string>(4);
        var keyFileBytes = TryReadFallbackKeyFile();
        if (!fromPrimarySource && keyFileBytes is not null)
        {
            AddUniqueSecret(secrets, CombineFallbackSecret(secret, keyFileBytes));
        }

        AddUniqueSecret(secrets, secret);

        if (keyFileBytes is not null)
        {
            // A token saved during a transient primary outage stays readable
            // once the primary source resolves.
            AddUniqueSecret(secrets, CombineFallbackSecret(GetFallbackMachineSecret(), keyFileBytes));
            CryptographicOperations.ZeroMemory(keyFileBytes);
        }

        var relocatedKeyBytes = _fallbackTokenKeyFilePath is null ? null : TryReadKeyFile(_fallbackTokenKeyFilePath);
        if (relocatedKeyBytes is not null)
        {
            // A data directory change leaves the previous install's token
            // beside its own key file; that copy stays readable after
            // relocation instead of silently signing the user out.
            AddUniqueSecret(secrets, CombineFallbackSecret(GetFallbackMachineSecret(), relocatedKeyBytes));
            CryptographicOperations.ZeroMemory(relocatedKeyBytes);
        }

        // Tokens saved before the per-install key existed.
        AddUniqueSecret(secrets, GetFallbackMachineSecret());
        return secrets;
    }

    private async Task<byte[]> EnsureFallbackKeyFileAsync()
    {
        var existing = TryReadFallbackKeyFile();
        if (existing is not null)
        {
            return existing;
        }

        if (IsTruncatedKeyFile())
        {
            // A crash between create and write left a wrong-length file that
            // can never decrypt anything; replace it instead of polling it.
            return await ReplaceTruncatedKeyFileAsync();
        }

        var fresh = RandomNumberGenerator.GetBytes(SecureTokenFileConstants.TokenFileKeySizeBytes);
        try
        {
            await using (var stream = OpenRestrictedWriteStream(_fallbackKeyFilePath, FileMode.CreateNew))
            {
                await stream.WriteAsync(fresh);
            }

            RestrictFilePermissions(_fallbackKeyFilePath);
            return fresh;
        }
        catch (IOException)
        {
            CryptographicOperations.ZeroMemory(fresh);
            if (!File.Exists(_fallbackKeyFilePath))
            {
                throw;
            }

            return await ReadWinnerKeyAsync();
        }
        catch (UnauthorizedAccessException)
        {
            CryptographicOperations.ZeroMemory(fresh);
            throw;
        }
    }

    private bool IsTruncatedKeyFile()
    {
        try
        {
            var info = new FileInfo(_fallbackKeyFilePath);
            return info.Exists && info.Length != SecureTokenFileConstants.TokenFileKeySizeBytes;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (SecurityException)
        {
            return false;
        }
    }

    private async Task<byte[]> ReplaceTruncatedKeyFileAsync()
    {
        var fresh = RandomNumberGenerator.GetBytes(SecureTokenFileConstants.TokenFileKeySizeBytes);
        var directory = Path.GetDirectoryName(_fallbackKeyFilePath)!;
        var tempPath = Path.Combine(directory, $"{TokenFileName}.{Guid.NewGuid():N}.tmp");
        var moved = false;
        try
        {
            await using (var stream = OpenRestrictedWriteStream(tempPath))
            {
                await stream.WriteAsync(fresh);
            }

            await FileOperationsService.MoveFileWithRetryAsync(tempPath, _fallbackKeyFilePath).ConfigureAwait(false);
            moved = true;
            try
            {
                RestrictFilePermissions(_fallbackKeyFilePath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                CryptographicOperations.ZeroMemory(fresh);
                throw;
            }

            // Another process may have replaced the file concurrently; adopt
            // whatever landed so the save stays decryptable.
            var landed = TryReadFallbackKeyFile();
            if (landed is not null && !CryptographicOperations.FixedTimeEquals(landed, fresh))
            {
                CryptographicOperations.ZeroMemory(fresh);
                return landed;
            }

            if (landed is not null)
            {
                CryptographicOperations.ZeroMemory(landed);
            }

            return fresh;
        }
        finally
        {
            if (!moved)
            {
                CryptographicOperations.ZeroMemory(fresh);
                FileOperationsService.DeleteFileIfExists(tempPath);
            }
        }
    }

    private async Task<byte[]> ReadWinnerKeyAsync()
    {
        const int MaxAttempts = 10;
        const int RetryDelayMs = 20;

        // Another process won the create race and may still be writing under
        // an exclusive lock; poll briefly for the complete key file.
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var winner = TryReadFallbackKeyFile();
            if (winner is not null)
            {
                return winner;
            }

            await Task.Delay(RetryDelayMs);
        }

        throw new IOException($"The fallback key file '{_fallbackKeyFilePath}' could not be read.");
    }

    private byte[]? TryReadFallbackKeyFile()
    {
        return TryReadKeyFile(_fallbackKeyFilePath);
    }
}
