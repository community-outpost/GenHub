using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Stores trusted publisher public keys in <see cref="PublisherKeyConstants.StoreFileName"/> under the
/// application data directory, encrypted at rest.
/// <list type="bullet">
/// <item>On Windows the JSON document is protected with DPAPI for the current user, so another
/// account or machine cannot decrypt it, and renaming the PC or account does not lock it.</item>
/// <item>On Linux and macOS it is encrypted with AES-256-GCM under a key derived from the machine ID
/// (<see cref="MachineBoundEncryption"/>) and written readable by the owner only. A copied file does
/// not decrypt elsewhere, but a process running as the same user on the same machine can derive the
/// key, so this protects against copies and edits, not against that user's own processes.</item>
/// </list>
/// Writes are atomic. A store that cannot be decrypted or parsed is reported through the result and
/// never overwritten; <see cref="QuarantineAsync"/> moves it aside so a new store can start.
/// </summary>
public sealed class PublisherKeyStore : IPublisherKeyStore
{
    private const UnixFileMode OwnerOnlyFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes(PublisherKeyConstants.StoreKeySalt);

    private readonly ILogger<PublisherKeyStore> _logger;
    private readonly Func<MachineSecret> _resolveMachineSecret;
    private readonly string _storeFilePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherKeyStore"/> class.
    /// </summary>
    /// <param name="configurationProvider">Resolves the application data directory.</param>
    /// <param name="logger">The logger.</param>
    public PublisherKeyStore(IConfigurationProviderService configurationProvider, ILogger<PublisherKeyStore> logger)
        : this(configurationProvider, logger, MachineBoundEncryption.ResolveMachineSecret)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherKeyStore"/> class with a machine secret source.
    /// </summary>
    /// <param name="configurationProvider">Resolves the application data directory.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="resolveMachineSecret">Resolves the machine-bound secret for the encryption key.</param>
    internal PublisherKeyStore(
        IConfigurationProviderService configurationProvider,
        ILogger<PublisherKeyStore> logger,
        Func<MachineSecret> resolveMachineSecret)
    {
        ArgumentNullException.ThrowIfNull(configurationProvider);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resolveMachineSecret = resolveMachineSecret ?? throw new ArgumentNullException(nameof(resolveMachineSecret));
        _storeFilePath = Path.Combine(configurationProvider.GetApplicationDataPath(), PublisherKeyConstants.StoreFileName);
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<TrustedPublisherKey>>> GetKeysAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.Failed)
            {
                return OperationResult<IReadOnlyList<TrustedPublisherKey>>.CreateFailure(loaded);
            }

            return OperationResult<IReadOnlyList<TrustedPublisherKey>>.CreateSuccess(loaded.Data!.ToList());
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<TrustedPublisherKey?>> GetKeyAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.Failed)
            {
                return OperationResult<TrustedPublisherKey?>.CreateFailure(loaded);
            }

            return OperationResult<TrustedPublisherKey?>.CreateSuccess(
                loaded.Data!.FirstOrDefault(k => IsPublisher(k, publisherId)));
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult> SaveKeyAsync(TrustedPublisherKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.PublisherId, nameof(key));
        if (!IsWellFormed(key))
        {
            throw new ArgumentException("The trusted key is missing its algorithm, key data, or fingerprint.", nameof(key));
        }

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.Failed)
            {
                return OperationResult.CreateFailure(loaded.Errors);
            }

            var keys = loaded.Data!;
            var existing = keys.Find(k => IsPublisher(k, key.PublisherId));
            keys.RemoveAll(k => IsPublisher(k, key.PublisherId));
            keys.Add(IsSameKey(existing, key) ? key with { TrustedAt = existing!.TrustedAt } : key);

            var written = await WriteAsync(keys, cancellationToken).ConfigureAwait(false);
            if (written.Success)
            {
                _logger.LogInformation(
                    "Saved trusted {Algorithm} key for publisher {PublisherId}",
                    key.PublicKey.Algorithm,
                    key.PublisherId);
            }

            return written;
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> RemoveKeyAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);

        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await LoadAsync(cancellationToken).ConfigureAwait(false);
            if (loaded.Failed)
            {
                return OperationResult<bool>.CreateFailure(loaded);
            }

            var keys = loaded.Data!;
            if (keys.RemoveAll(k => IsPublisher(k, publisherId)) == 0)
            {
                return OperationResult<bool>.CreateSuccess(false);
            }

            var written = await WriteAsync(keys, cancellationToken).ConfigureAwait(false);
            if (written.Failed)
            {
                return OperationResult<bool>.CreateFailure(written);
            }

            _logger.LogInformation("Removed trusted key for publisher {PublisherId}", publisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<string?>> QuarantineAsync(CancellationToken cancellationToken = default)
    {
        await _fileLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // A temporarily unavailable encryption provider is not evidence of corruption.
            // Recheck on each explicit quarantine request rather than relying on a prior read.
            var encryptedBytes = await File.ReadAllBytesAsync(_storeFilePath, cancellationToken).ConfigureAwait(false);
            var decrypted = await DecryptAsync(encryptedBytes, cancellationToken).ConfigureAwait(false);
            if (decrypted.Failed)
            {
                return OperationResult<string?>.CreateFailure(decrypted);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var quarantinePath = CreateQuarantinePath();
            File.Move(_storeFilePath, quarantinePath, overwrite: false);
            _logger.LogWarning(
                "Moved publisher key store {StoreFilePath} aside to {QuarantinePath}",
                _storeFilePath,
                quarantinePath);
            return OperationResult<string?>.CreateSuccess(quarantinePath);
        }
        catch (FileNotFoundException)
        {
            return OperationResult<string?>.CreateSuccess(null);
        }
        catch (DirectoryNotFoundException)
        {
            return OperationResult<string?>.CreateSuccess(null);
        }
        catch (IOException ex)
        {
            return QuarantineFailed(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return QuarantineFailed(ex);
        }
        catch (CryptographicException ex)
        {
            return QuarantineFailed(ex);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    /// <summary>
    /// Encrypts store content for this platform: DPAPI for the current user on Windows, machine-bound
    /// AES-GCM elsewhere.
    /// </summary>
    /// <param name="plainBytes">The serialized store document.</param>
    /// <returns>The protected bytes.</returns>
    internal byte[] Protect(byte[] plainBytes)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProtectedData.Protect(plainBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        }

        return MachineBoundEncryption.EncryptWithSecret(plainBytes, _resolveMachineSecret().Secret, PublisherKeyConstants.StoreKeySalt);
    }

    /// <summary>
    /// Decrypts store content written by <see cref="Protect"/>.
    /// </summary>
    /// <param name="protectedBytes">The bytes read from the store file.</param>
    /// <returns>The serialized store document, or null when the bytes do not decrypt or were modified.</returns>
    internal byte[]? Unprotect(byte[] protectedBytes) => Unprotect(protectedBytes, out _);

    [SupportedOSPlatform("windows")]
    private static byte[]? UnprotectWithDpapi(byte[] protectedBytes)
    {
        try
        {
            return ProtectedData.Unprotect(protectedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool IsPublisher(TrustedPublisherKey key, string publisherId)
    {
        return string.Equals(key.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameKey(TrustedPublisherKey? existing, TrustedPublisherKey incoming)
    {
        return existing is not null
            && existing.PublicKey.Algorithm == incoming.PublicKey.Algorithm
            && string.Equals(existing.PublicKey.Fingerprint, incoming.PublicKey.Fingerprint, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWellFormed(TrustedPublisherKey? key)
    {
        return key is not null
            && !string.IsNullOrWhiteSpace(key.PublisherId)
            && key.PublicKey is not null
            && Enum.IsDefined(key.PublicKey.Algorithm)
            && !string.IsNullOrWhiteSpace(key.PublicKey.SubjectPublicKeyInfo)
            && !string.IsNullOrWhiteSpace(key.PublicKey.Fingerprint);
    }

    private static string? FindProblem(PublisherKeyStoreDocument? document)
    {
        if (document is null)
        {
            return "the file is empty";
        }

        if (document.SchemaVersion != PublisherKeyConstants.StoreSchemaVersion)
        {
            return $"schema version {document.SchemaVersion} is not supported";
        }

        if (document.Keys is null)
        {
            return "the key list is missing";
        }

        HashSet<string> publisherIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (var key in document.Keys)
        {
            if (!IsWellFormed(key))
            {
                return "an entry is malformed";
            }

            if (!publisherIds.Add(key.PublisherId))
            {
                return $"publisher {key.PublisherId} has more than one entry";
            }
        }

        return null;
    }

    private byte[]? Unprotect(byte[] protectedBytes, out bool machineIdUnavailable)
    {
        machineIdUnavailable = false;
        if (OperatingSystem.IsWindows())
        {
            return UnprotectWithDpapi(protectedBytes);
        }

        var secret = _resolveMachineSecret();
        if (MachineBoundEncryption.TryDecryptWithSecret(protectedBytes, secret, PublisherKeyConstants.StoreKeySalt, out var plainBytes))
        {
            return plainBytes;
        }

        machineIdUnavailable = !secret.FromPrimarySource;
        return null;
    }

    private async Task<OperationResult<List<TrustedPublisherKey>>> LoadAsync(CancellationToken cancellationToken)
    {
        // The file is opened directly instead of probed with File.Exists, which also returns false
        // when the file cannot be accessed. Only a genuinely absent file counts as an empty store.
        byte[] encryptedBytes = [];
        try
        {
            encryptedBytes = await File.ReadAllBytesAsync(_storeFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return OperationResult<List<TrustedPublisherKey>>.CreateSuccess([]);
        }
        catch (DirectoryNotFoundException)
        {
            return OperationResult<List<TrustedPublisherKey>>.CreateSuccess([]);
        }
        catch (IOException ex)
        {
            return ReadFailed(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ReadFailed(ex);
        }

        var decrypted = await DecryptAsync(encryptedBytes, cancellationToken).ConfigureAwait(false);
        if (decrypted.Failed)
        {
            return OperationResult<List<TrustedPublisherKey>>.CreateFailure(decrypted);
        }

        if (decrypted.Data is null)
        {
            return Corrupt("the file cannot be decrypted on this machine or was modified");
        }

        PublisherKeyStoreDocument? document = null;
        try
        {
            document = JsonSerializer.Deserialize<PublisherKeyStoreDocument>(decrypted.Data, JsonOptions);
        }
        catch (JsonException)
        {
            return Corrupt("the decrypted content is not valid JSON");
        }

        var problem = FindProblem(document);
        if (problem is not null)
        {
            return Corrupt(problem);
        }

        return OperationResult<List<TrustedPublisherKey>>.CreateSuccess(document!.Keys!);
    }

    private async Task<OperationResult<byte[]?>> DecryptAsync(byte[] encryptedBytes, CancellationToken cancellationToken)
    {
        try
        {
            // Resolving the secret can spawn a process and key derivation is deliberately slow, so both run off the caller's thread.
            return await Task.Run(
                () =>
                {
                    var plainBytes = Unprotect(encryptedBytes, out var machineIdUnavailable);
                    return machineIdUnavailable
                        ? OperationResult<byte[]?>.CreateFailure(
                            "The machine ID is unavailable, so the publisher key store may be temporarily unreadable. Retry later; the store was left unchanged and must not be quarantined now.")
                        : OperationResult<byte[]?>.CreateSuccess(plainBytes);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (CryptographicException ex)
        {
            return OperationResult<byte[]?>.CreateFailure(EncryptionUnavailable(ex));
        }
        catch (PlatformNotSupportedException ex)
        {
            return OperationResult<byte[]?>.CreateFailure(EncryptionUnavailable(ex));
        }
    }

    private async Task<OperationResult> WriteAsync(List<TrustedPublisherKey> keys, CancellationToken cancellationToken)
    {
        var document = new PublisherKeyStoreDocument
        {
            SchemaVersion = PublisherKeyConstants.StoreSchemaVersion,
            Keys = keys,
        };

        try
        {
            var directory = Path.GetDirectoryName(_storeFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var plainBytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
            var encryptedBytes = await Task.Run(() => Protect(plainBytes), cancellationToken).ConfigureAwait(false);
            await AtomicFile.WriteAllBytesAsync(_storeFilePath, encryptedBytes, OwnerOnlyFileMode, cancellationToken).ConfigureAwait(false);
            return OperationResult.CreateSuccess();
        }
        catch (CryptographicException ex)
        {
            return OperationResult.CreateFailure(EncryptionUnavailable(ex));
        }
        catch (PlatformNotSupportedException ex)
        {
            return OperationResult.CreateFailure(EncryptionUnavailable(ex));
        }
        catch (IOException ex)
        {
            return WriteFailed(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return WriteFailed(ex);
        }
    }

    private OperationResult<List<TrustedPublisherKey>> Corrupt(string problem)
    {
        _logger.LogWarning("Publisher key store {StoreFilePath} is unreadable: {Problem}", _storeFilePath, problem);
        return OperationResult<List<TrustedPublisherKey>>.CreateFailure(
            $"The publisher key store is unreadable: {problem}. It was left unchanged.");
    }

    private string CreateQuarantinePath()
    {
        var timestamp = DateTime.UtcNow.ToString(PublisherKeyConstants.QuarantineTimestampFormat, CultureInfo.InvariantCulture);
        var suffix = Convert.ToHexString(RandomNumberGenerator.GetBytes(PublisherKeyConstants.QuarantineSuffixLength / 2)).ToLowerInvariant();
        return $"{_storeFilePath}.{timestamp}-{suffix}{PublisherKeyConstants.QuarantinedFileExtension}";
    }

    private OperationResult<string?> QuarantineFailed(Exception ex)
    {
        _logger.LogError(ex, "Failed to move publisher key store {StoreFilePath} aside", _storeFilePath);
        return OperationResult<string?>.CreateFailure($"Failed to move the publisher key store aside: {ex.Message}");
    }

    private string EncryptionUnavailable(Exception ex)
    {
        _logger.LogError(ex, "Publisher key store encryption failed for {StoreFilePath}", _storeFilePath);
        return "The publisher key store encryption is unavailable on this system. The store was left unchanged.";
    }

    private OperationResult<List<TrustedPublisherKey>> ReadFailed(Exception ex)
    {
        _logger.LogError(ex, "Failed to read publisher key store {StoreFilePath}", _storeFilePath);
        return OperationResult<List<TrustedPublisherKey>>.CreateFailure($"Failed to read the publisher key store: {ex.Message}");
    }

    private OperationResult WriteFailed(Exception ex)
    {
        _logger.LogError(ex, "Failed to write publisher key store {StoreFilePath}", _storeFilePath);
        return OperationResult.CreateFailure($"Failed to write the publisher key store: {ex.Message}");
    }
}
