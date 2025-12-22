using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.GeneralsOnline;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Service for managing the credentials.json file for GeneralsOnline authentication.
/// Credentials are encrypted using Windows DPAPI for the current user.
/// </summary>
/// <param name="logger">The logger instance.</param>
public class CredentialsStorageService(ILogger<CredentialsStorageService> logger) : ICredentialsStorageService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <inheritdoc />
    public async Task<CredentialsModel?> LoadCredentialsAsync()
    {
        var path = GetCredentialsPath();

        try
        {
            if (!File.Exists(path))
            {
                logger.LogDebug("Credentials file not found at {Path}", path);
                return null;
            }

            // Read encrypted bytes
            var encryptedBytes = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
            if (encryptedBytes.Length == 0)
            {
                logger.LogWarning("Credentials file is empty at {Path}", path);
                return null;
            }

            // Decrypt using DPAPI
            byte[] decryptedBytes;
            try
            {
                decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            }
            catch (CryptographicException ex)
            {
                logger.LogError(ex, "Failed to decrypt credentials - may have been encrypted by different user");
                return null;
            }

            var json = Encoding.UTF8.GetString(decryptedBytes);
            var credentials = JsonSerializer.Deserialize<CredentialsModel>(json, JsonOptions);

            if (credentials == null)
            {
                logger.LogWarning("Failed to deserialize credentials from {Path}", path);
                return null;
            }

            if (!credentials.IsValid())
            {
                logger.LogWarning("Loaded credentials are invalid from {Path}", path);
                return null;
            }

            logger.LogInformation("Successfully loaded and decrypted credentials from {Path}", path);
            return credentials;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse credentials JSON from {Path}", path);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load credentials from {Path}", path);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task SaveCredentialsAsync(CredentialsModel credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var path = GetCredentialsPath();

        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                logger.LogInformation("Created GeneralsOnlineData directory at {Directory}", directory);
            }

            // Serialize to JSON
            var json = JsonSerializer.Serialize(credentials, JsonOptions);
            var jsonBytes = Encoding.UTF8.GetBytes(json);

            // Encrypt with DPAPI for current Windows user
            var encryptedBytes = ProtectedData.Protect(
                jsonBytes,
                optionalEntropy: null,
                scope: DataProtectionScope.CurrentUser);

            // Write encrypted bytes to file
            await File.WriteAllBytesAsync(path, encryptedBytes).ConfigureAwait(false);

            logger.LogInformation("Successfully encrypted and saved credentials to {Path}", path);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save credentials to {Path}", path);
            throw;
        }
    }

    /// <inheritdoc />
    public Task DeleteCredentialsAsync()
    {
        var path = GetCredentialsPath();

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                logger.LogInformation("Deleted credentials file at {Path}", path);
            }
            else
            {
                logger.LogDebug("No credentials file to delete at {Path}", path);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete credentials file at {Path}", path);
            throw;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public string GetCredentialsPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Command and Conquer Generals Zero Hour Data",
            GeneralsOnlineConstants.DataFolderName,
            GeneralsOnlineConstants.CredentialsFileName);
    }

    /// <inheritdoc />
    public bool CredentialsFileExists()
    {
        return File.Exists(GetCredentialsPath());
    }
}
