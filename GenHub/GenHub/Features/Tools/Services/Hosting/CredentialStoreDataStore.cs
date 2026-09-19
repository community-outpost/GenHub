using GenHub.Core.Interfaces.Publishers;
using Google.Apis.Util.Store;
using System.Text.Json;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Google <see cref="IDataStore"/> backed by the encrypted <see cref="IHostingCredentialStore"/>.
/// Persists OAuth token responses as protected JSON instead of plaintext files.
/// </summary>
/// <param name="credentialStore">The encrypted credential store.</param>
/// <param name="keyPrefix">Prefix isolating Google token keys from provider PAT entries.</param>
public class CredentialStoreDataStore(
    IHostingCredentialStore credentialStore,
    string keyPrefix = "GoogleDrive.") : IDataStore
{
    private const string BrokerUserKey = "user";

    /// <inheritdoc />
    public async Task StoreAsync<T>(string key, T value)
    {
        var json = JsonSerializer.Serialize(value);
        await credentialStore.SaveCredentialAsync(keyPrefix + key, json).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeleteAsync<T>(string key)
    {
        await credentialStore.DeleteCredentialAsync(keyPrefix + key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<T> GetAsync<T>(string key)
    {
        var json = await credentialStore.GetCredentialAsync(keyPrefix + key).ConfigureAwait(false);
        if (string.IsNullOrEmpty(json))
        {
            return default!;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json)!;
        }
        catch (JsonException)
        {
            return default!;
        }
    }

    /// <inheritdoc />
    public Task ClearAsync()
    {
        // The Drive provider persists a single broker user key.
        return DeleteAsync<object?>(BrokerUserKey);
    }
}
