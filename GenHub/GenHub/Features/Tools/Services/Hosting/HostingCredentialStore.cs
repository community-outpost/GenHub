using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Publishers;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
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

    /// <summary>
    /// Gets or sets an optional machine secret override for unit tests.
    /// </summary>
    internal static string? MachineSecretOverrideForTesting { get; set; }

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

        byte[]? plainBytes = null;
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

            plainBytes = Encoding.UTF8.GetBytes(credential);
            byte[] encryptedBytes;

            if (OperatingSystem.IsWindows())
            {
                encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            }
            else
            {
                encryptedBytes = EncryptNonWindows(plainBytes);
            }

            await WriteCredentialAtomicallyAsync(filePath, encryptedBytes, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to securely save credential for provider {providerId}", ex);
        }
        finally
        {
            if (plainBytes != null)
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
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

            if (!OperatingSystem.IsWindows())
            {
                VerifySecureUnixPermissions(filePath);
            }

            var encryptedBytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (encryptedBytes.Length == 0)
            {
                return null;
            }

            byte[]? plainBytes = null;
            try
            {
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
            finally
            {
                if (plainBytes != null)
                {
                    CryptographicOperations.ZeroMemory(plainBytes);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
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
        try
        {
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
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DecryptNonWindows(byte[] encryptedBytes)
    {
        if (encryptedBytes.Length < 28)
        {
            throw new InvalidOperationException("Encrypted credential data is invalid or corrupted.");
        }

        var key = DeriveNonWindowsKey();
        try
        {
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
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static byte[] DeriveNonWindowsKey()
    {
        var machineSecret = MachineSecretOverrideForTesting ?? GetMachineSecret();
        var keyMaterial = $"{Environment.UserName}@{machineSecret}:GenHub-CredentialStore-Salt-2026";
        var keyMaterialBytes = Encoding.UTF8.GetBytes(keyMaterial);
        try
        {
            return SHA256.HashData(keyMaterialBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(keyMaterialBytes);
        }
    }

    private static string GetMachineSecret()
    {
        var linuxId = TryReadLinuxMachineId();
        if (!string.IsNullOrEmpty(linuxId))
        {
            return linuxId;
        }

        if (OperatingSystem.IsMacOS())
        {
            var macId = TryReadMacOsMachineId();
            if (!string.IsNullOrEmpty(macId))
            {
                return macId;
            }
        }

        return Environment.MachineName;
    }

    private static string? TryReadLinuxMachineId()
    {
        string[] candidates = ["/etc/machine-id", "/var/lib/dbus/machine-id"];
        foreach (var candidatePath in candidates)
        {
            if (File.Exists(candidatePath))
            {
                try
                {
                    var id = File.ReadAllText(candidatePath).Trim();
                    if (!string.IsNullOrEmpty(id))
                    {
                        return id;
                    }
                }
                catch
                {
                    // Fallback to next candidate
                }
            }
        }

        return null;
    }

    private static string? TryReadMacOsMachineId()
    {
        try
        {
            var ioregPath = File.Exists("/usr/sbin/ioreg") ? "/usr/sbin/ioreg" : "ioreg";
            var startInfo = new ProcessStartInfo
            {
                FileName = ioregPath,
                Arguments = "-rd1 -c IOPlatformExpertDevice",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(1000);
                const string marker = ""IOPlatformUUID" = "";
                var idx = output.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    var start = idx + marker.Length;
                    var end = output.IndexOf('"', start);
                    if (end > start)
                    {
                        return output[start..end];
                    }
                }
            }
        }
        catch
        {
            // Fallback below
        }

        return null;
    }

    private static void VerifySecureUnixPermissions(string filePath)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            var mode = File.GetUnixFileMode(filePath);
            var insecureBits = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                               UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((mode & insecureBits) != 0)
            {
                throw new InvalidOperationException($"Credential file {filePath} has insecure file permissions ({mode}). Group and Other access must be denied.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception)
        {
            // On non-POSIX file systems (e.g. FAT/NTFS on Linux), GetUnixFileMode might fail or return default
        }
    }

    private string GetCredentialFilePath(string providerId)
    {
        var safeFileName = Path.GetInvalidFileNameChars()
            .Aggregate(providerId, (current, c) => current.Replace(c, '_'));
        return Path.Combine(configurationProvider.GetApplicationDataPath(), "credentials", $"{safeFileName}.dat");
    }

    private async Task WriteCredentialAtomicallyAsync(string filePath, byte[] encryptedBytes, CancellationToken cancellationToken)
    {
        var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.Create,
                Access = FileAccess.Write,
                Share = FileShare.None,
            };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            using (var tempStream = new FileStream(tempPath, options))
            {
                await tempStream.WriteAsync(encryptedBytes, cancellationToken).ConfigureAwait(false);
                await tempStream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to enforce Unix 0600 permissions on credential file {FilePath}", filePath);
                }
            }

            File.Move(tempPath, filePath, overwrite: true);
        }
        catch
        {
            DeleteTempFile(tempPath);
            throw;
        }
    }

    private void DeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (IOException ex)
        {
            logger.LogDebug(ex, "Failed to clean up temporary credential file {TempPath}", tempPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogDebug(ex, "Failed to clean up temporary credential file {TempPath}", tempPath);
        }
    }
}
