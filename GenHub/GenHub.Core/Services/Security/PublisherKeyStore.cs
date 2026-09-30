using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Stores trusted publisher public keys as JSON in <see cref="PublisherKeyConstants.StoreFileName"/>
/// under the application data directory. Writes are atomic, and a store that cannot be read is
/// reported through the result and never overwritten, so a corrupt file cannot silently drop trust.
/// </summary>
/// <param name="configurationProvider">Resolves the application data directory.</param>
/// <param name="logger">The logger.</param>
public sealed class PublisherKeyStore(
    IConfigurationProviderService configurationProvider,
    ILogger<PublisherKeyStore> logger) : IPublisherKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly string _storeFilePath = Path.Combine(
        configurationProvider.GetApplicationDataPath(),
        PublisherKeyConstants.StoreFileName);

    private readonly SemaphoreSlim _fileLock = new(1, 1);

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
            keys.RemoveAll(k => IsPublisher(k, key.PublisherId));
            keys.Add(key);

            var written = await WriteAsync(keys, cancellationToken).ConfigureAwait(false);
            if (written.Success)
            {
                logger.LogInformation(
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

            logger.LogInformation("Removed trusted key for publisher {PublisherId}", publisherId);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            _fileLock.Release();
        }
    }

    private static bool IsPublisher(TrustedPublisherKey key, string publisherId)
    {
        return string.Equals(key.PublisherId, publisherId, StringComparison.OrdinalIgnoreCase);
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

    private async Task<OperationResult<List<TrustedPublisherKey>>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_storeFilePath))
        {
            return OperationResult<List<TrustedPublisherKey>>.CreateSuccess([]);
        }

        PublisherKeyStoreDocument? document;
        try
        {
            await using var stream = new FileStream(
                _storeFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                IoConstants.DefaultFileBufferSize,
                FileOptions.Asynchronous);
            document = await JsonSerializer
                .DeserializeAsync<PublisherKeyStoreDocument>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Corrupt("the file is not valid JSON");
        }
        catch (IOException ex)
        {
            return ReadFailed(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            return ReadFailed(ex);
        }

        var problem = FindProblem(document);
        if (problem is not null)
        {
            return Corrupt(problem);
        }

        return OperationResult<List<TrustedPublisherKey>>.CreateSuccess(document!.Keys!);
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

            var json = JsonSerializer.Serialize(document, JsonOptions);
            await AtomicFile.WriteAllTextAsync(_storeFilePath, json, cancellationToken).ConfigureAwait(false);
            return OperationResult.CreateSuccess();
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
        logger.LogWarning("Publisher key store {StoreFilePath} is unreadable: {Problem}", _storeFilePath, problem);
        return OperationResult<List<TrustedPublisherKey>>.CreateFailure(
            $"The publisher key store is unreadable: {problem}. It was left unchanged.");
    }

    private OperationResult<List<TrustedPublisherKey>> ReadFailed(Exception ex)
    {
        logger.LogError(ex, "Failed to read publisher key store {StoreFilePath}", _storeFilePath);
        return OperationResult<List<TrustedPublisherKey>>.CreateFailure($"Failed to read the publisher key store: {ex.Message}");
    }

    private OperationResult WriteFailed(Exception ex)
    {
        logger.LogError(ex, "Failed to write publisher key store {StoreFilePath}", _storeFilePath);
        return OperationResult.CreateFailure($"Failed to write the publisher key store: {ex.Message}");
    }
}
