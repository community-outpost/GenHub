namespace GenHub.Core.Constants;

/// <summary>
/// Shared file format for AES-GCM encrypted single-token stores
/// (GitHub tokens, Generals Online refresh tokens).
/// </summary>
public static class SecureTokenFileConstants
{
    /// <summary>Encrypted token file format version byte.</summary>
    public const byte TokenFileFormatVersion = 1;

    /// <summary>PBKDF2-HMAC-SHA256 iterations for deriving the token file encryption key (OWASP guidance: 600,000).</summary>
    public const int TokenFileKeyIterations = 600000;

    /// <summary>AES-256 key size for the token file encryption key, in bytes.</summary>
    public const int TokenFileKeySizeBytes = 32;

    /// <summary>AES-GCM nonce size for token file encryption, in bytes.</summary>
    public const int TokenFileNonceSizeBytes = 12;

    /// <summary>AES-GCM authentication tag size for token file encryption, in bytes.</summary>
    public const int TokenFileTagSizeBytes = 16;

    /// <summary>Linux machine identity file used as key material.</summary>
    public const string LinuxMachineIdPath = "/etc/machine-id";

    /// <summary>Fallback Linux machine identity file used as key material.</summary>
    public const string LinuxMachineIdFallbackPath = "/var/lib/dbus/machine-id";

    /// <summary>Absolute path of the macOS command used to read the platform UUID for key material.</summary>
    public const string MacOsIoRegCommand = "/usr/sbin/ioreg";

    /// <summary>Arguments listing the macOS platform expert device for UUID lookup.</summary>
    public const string MacOsIoRegArguments = "-rd1 -c IOPlatformExpertDevice";

    /// <summary>Property key holding the platform UUID in ioreg output.</summary>
    public const string MacOsIoRegUuidKey = "IOPlatformUUID";

    /// <summary>Timeout for the macOS platform UUID lookup, in seconds.</summary>
    public const int MacOsIoRegTimeoutSeconds = 5;

    /// <summary>Suffix of the per-install random key file strengthening fallback-secret derivation.</summary>
    public const string TokenFileKeySuffix = ".key";

    /// <summary>Maximum accepted encrypted token file size, in bytes. Real tokens are far smaller.</summary>
    public const int MaxTokenFileBytes = 65536;
}
