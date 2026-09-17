using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Publishers;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.Services.Hosting;

/// <summary>
/// Securely stores and retrieves hosting provider credentials using platform-native protection.
/// Windows: uses DPAPI (Data Protection API) scoped to CurrentUser.
/// Unix/macOS: uses AES-256-GCM encryption with machine/user key material and POSIX 0600 file permissions.
/// </summary>
public class HostingCredentialStore(
    IConfigurationProviderService configurationProvider,
    ILogger<HostingCredentialStore> logger) : IHostingCredentialStore
{
    private static readonly byte[] Entropy = "GenHub.HostingCredentialStore.v1"u8.ToArray();

    /// <inheritdoc />
    public async Task SaveCredentialAsync(string providerId, string credential, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("Provider ID cannot be null or whitespace.", nameof(providerId));
        }

        if (string.IsNullOrWhiteSpace(credential))
        {
            await DeleteCredentialAsync(providerId, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var filePath = GetCredentialFilePath(providerId);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    catch (Exception ex)
                    {
                        logger.LogDebug(ex, "Failed to set Unix permissions on credentials directory");
                    }
                }
            }

            var plainBytes = Encoding.UTF8.GetBytes(credential);
            byte[] encryptedBytes;

            if (OperatingSystem.IsWindows())
            {
                encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                encryptedBytes = EncryptNonWindows(plainBytes);
            }

            await File.WriteAllBytesAsync(filePath, encryptedBytes, cancellationToken).ConfigureAwait(false);

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(filePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Failed to set Unix 0600 permissions on credential file {FilePath}", filePath);
                }
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to securely save credential for provider {providerId}", ex);
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetCredentialAsync(string providerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return null;
        }

        try
        {
            var filePath = GetCredentialFilePath(providerId);
            if (!File.Exists(filePath))
            {
                return null;
            }

            var encryptedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (encryptedBytes.Length == 0)
            {
                return null;
            }

            byte[] plainBytes;
            if (OperatingSystem.IsWindows())
            {
                plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                plainBytes = DecryptNonWindows(encryptedBytes);
            }

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to retrieve or decrypt credential for provider {ProviderId}", providerId);
            return null;
        }
    }

    /// <inheritdoc />
    public Task DeleteCredentialAsync(string providerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return Task.CompletedTask;
        }

        try
        {
            var filePath = GetCredentialFilePath(providerId);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete credential for provider {ProviderId}", providerId);
        }

        return Task.CompletedTask;
    }

    private static byte[] EncryptNonWindows(byte[] plainBytes)
    {
        var key = DeriveNonWindowsKey();
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        var tag = new byte[16];
        var ciphertext = new byte[plainBytes.Length];

        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Encrypt(nonce, plainBytes, ciphertext, tag);

        var result = new byte[12 + 16 + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, 12);
        Buffer.BlockCopy(tag, 0, result, 12, 16);
        Buffer.BlockCopy(ciphertext, 0, result, 28, ciphertext.Length);
        return result;
    }

    private static byte[] DecryptNonWindows(byte[] encryptedBytes)
    {
        if (encryptedBytes.Length < 28)
        {
            throw new InvalidOperationException("Encrypted credential data is invalid or corrupted.");
        }

        var key = DeriveNonWindowsKey();
        var nonce = new byte[12];
        var tag = new byte[16];
        var ciphertext = new byte[encryptedBytes.Length - 28];

        Buffer.BlockCopy(encryptedBytes, 0, nonce, 0, 12);
        Buffer.BlockCopy(encryptedBytes, 12, tag, 0, 16);
        Buffer.BlockCopy(encryptedBytes, 28, ciphertext, 0, ciphertext.Length);

        var plainBytes = new byte[ciphertext.Length];
        using var aesGcm = new AesGcm(key, 16);
        aesGcm.Decrypt(nonce, ciphertext, tag, plainBytes);

        return plainBytes;
    }

    private static byte[] DeriveNonWindowsKey()
    {
        var keyMaterial = $"{Environment.UserName}@{Environment.MachineName}:GenHub-CredentialStore-Salt-2026";
        return SHA256.HashData(Encoding.UTF8.GetBytes(keyMaterial));
    }

    private string GetCredentialFilePath(string providerId)
    {
        var safeFileName = Path.GetInvalidFileNameChars()
            .Aggregate(providerId, (current, c) => current.Replace(c, '_'));
        return Path.Combine(configurationProvider.GetApplicationDataPath(), "credentials", $"{safeFileName}.dat");
    }
}
