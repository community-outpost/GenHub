using GenHub.Core.Constants;
using GenHub.Core.Models.Security;
using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// AES-256-GCM encryption with a key derived from a machine-bound secret, so a file copied
/// to another machine cannot be decrypted and any modified byte fails authentication.
/// Encrypted bytes are laid out as version, nonce, tag, then ciphertext.
/// </summary>
public static class MachineBoundEncryption
{
    private const int HeaderLength = 1 + MachineBoundEncryptionConstants.NonceSizeBytes + MachineBoundEncryptionConstants.TagSizeBytes;

    /// <summary>
    /// Resolves the machine-bound secret: the Linux machine ID or macOS platform UUID when
    /// available, otherwise the machine and user name fallback.
    /// </summary>
    /// <returns>The machine secret.</returns>
    public static MachineSecret ResolveMachineSecret()
    {
        if (OperatingSystem.IsLinux())
        {
            var machineId = ReadMachineIdFile(MachineBoundEncryptionConstants.LinuxMachineIdPath)
                ?? ReadMachineIdFile(MachineBoundEncryptionConstants.LinuxMachineIdFallbackPath);
            if (!string.IsNullOrEmpty(machineId))
            {
                return new MachineSecret(machineId, true);
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            var platformUuid = TryGetMacOsPlatformUuid();
            if (!string.IsNullOrEmpty(platformUuid))
            {
                return new MachineSecret(platformUuid, true);
            }
        }

        return new MachineSecret(GetFallbackMachineSecret(), false);
    }

    /// <summary>
    /// Gets the fallback machine secret used when the primary platform source is unavailable.
    /// </summary>
    /// <returns>The machine and user name based fallback secret.</returns>
    public static string GetFallbackMachineSecret()
    {
        return $"{Environment.MachineName}:{Environment.UserName}";
    }

    /// <summary>
    /// Derives an AES-256 key from a secret with PBKDF2-HMAC-SHA256. The caller owns the
    /// returned key and should zero it after use.
    /// </summary>
    /// <param name="secret">The machine secret.</param>
    /// <param name="salt">The domain separation salt for the data being protected.</param>
    /// <returns>The derived key.</returns>
    public static byte[] DeriveKey(string secret, string salt)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentException.ThrowIfNullOrEmpty(salt);

        var saltBytes = Encoding.UTF8.GetBytes(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(secret, saltBytes, MachineBoundEncryptionConstants.KeyIterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(MachineBoundEncryptionConstants.KeySizeBytes);
    }

    /// <summary>
    /// Encrypts bytes with a fresh random nonce.
    /// </summary>
    /// <param name="plainBytes">The bytes to encrypt.</param>
    /// <param name="key">The AES-256 key.</param>
    /// <returns>The version, nonce, tag, and ciphertext.</returns>
    public static byte[] Encrypt(byte[] plainBytes, byte[] key)
    {
        ArgumentNullException.ThrowIfNull(plainBytes);
        ArgumentNullException.ThrowIfNull(key);

        var nonce = RandomNumberGenerator.GetBytes(MachineBoundEncryptionConstants.NonceSizeBytes);
        var cipherBytes = new byte[plainBytes.Length];
        var tag = new byte[MachineBoundEncryptionConstants.TagSizeBytes];
        using (var aes = new AesGcm(key, MachineBoundEncryptionConstants.TagSizeBytes))
        {
            aes.Encrypt(nonce, plainBytes, cipherBytes, tag);
        }

        var encrypted = new byte[HeaderLength + cipherBytes.Length];
        encrypted[0] = MachineBoundEncryptionConstants.FormatVersion;
        nonce.CopyTo(encrypted.AsSpan(1));
        tag.CopyTo(encrypted.AsSpan(1 + nonce.Length));
        cipherBytes.CopyTo(encrypted.AsSpan(HeaderLength));
        return encrypted;
    }

    /// <summary>
    /// Encrypts bytes with a key derived from the secret, zeroing the key afterwards.
    /// </summary>
    /// <param name="plainBytes">The bytes to encrypt.</param>
    /// <param name="secret">The machine secret.</param>
    /// <param name="salt">The domain separation salt.</param>
    /// <returns>The version, nonce, tag, and ciphertext.</returns>
    public static byte[] EncryptWithSecret(byte[] plainBytes, string secret, string salt)
    {
        var key = DeriveKey(secret, salt);
        try
        {
            return Encrypt(plainBytes, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>
    /// Decrypts bytes produced by <see cref="Encrypt"/>. Fails for a wrong key, an unknown
    /// version, truncated input, or any modified byte.
    /// </summary>
    /// <param name="encryptedBytes">The encrypted bytes.</param>
    /// <param name="key">The AES-256 key.</param>
    /// <param name="plainBytes">The decrypted bytes, or null on failure.</param>
    /// <returns>True when decryption and authentication succeeded.</returns>
    public static bool TryDecrypt(byte[] encryptedBytes, byte[] key, out byte[]? plainBytes)
    {
        ArgumentNullException.ThrowIfNull(encryptedBytes);
        ArgumentNullException.ThrowIfNull(key);

        plainBytes = null;
        if (encryptedBytes.Length < HeaderLength || encryptedBytes[0] != MachineBoundEncryptionConstants.FormatVersion)
        {
            return false;
        }

        try
        {
            var nonce = encryptedBytes.AsSpan(1, MachineBoundEncryptionConstants.NonceSizeBytes);
            var tag = encryptedBytes.AsSpan(1 + MachineBoundEncryptionConstants.NonceSizeBytes, MachineBoundEncryptionConstants.TagSizeBytes);
            var cipherBytes = encryptedBytes.AsSpan(HeaderLength);
            var decrypted = new byte[cipherBytes.Length];
            using (var aes = new AesGcm(key, MachineBoundEncryptionConstants.TagSizeBytes))
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

    /// <summary>
    /// Decrypts bytes with a key derived from the secret. When the secret came from the
    /// primary platform source, the fallback secret is tried too, because the data may have
    /// been saved while the platform machine ID was unavailable.
    /// </summary>
    /// <param name="encryptedBytes">The encrypted bytes.</param>
    /// <param name="secret">The machine secret.</param>
    /// <param name="salt">The domain separation salt.</param>
    /// <param name="plainBytes">The decrypted bytes, or null on failure.</param>
    /// <returns>True when decryption and authentication succeeded.</returns>
    public static bool TryDecryptWithSecret(byte[] encryptedBytes, MachineSecret secret, string salt, out byte[]? plainBytes)
    {
        if (TryDecryptWithDerivedKey(encryptedBytes, secret.Secret, salt, out plainBytes))
        {
            return true;
        }

        return secret.FromPrimarySource
            && TryDecryptWithDerivedKey(encryptedBytes, GetFallbackMachineSecret(), salt, out plainBytes);
    }

    private static bool TryDecryptWithDerivedKey(byte[] encryptedBytes, string secret, string salt, out byte[]? plainBytes)
    {
        var key = DeriveKey(secret, salt);
        try
        {
            return TryDecrypt(encryptedBytes, key, out plainBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
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
                FileName = MachineBoundEncryptionConstants.MacOsIoRegCommand,
                Arguments = MachineBoundEncryptionConstants.MacOsIoRegArguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process == null)
            {
                return null;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(MachineBoundEncryptionConstants.MacOsIoRegTimeoutSeconds)))
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
        var keyToken = $"\"{MachineBoundEncryptionConstants.MacOsIoRegUuidKey}\"";
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
}
