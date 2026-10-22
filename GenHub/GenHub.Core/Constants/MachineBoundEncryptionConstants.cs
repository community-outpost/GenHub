namespace GenHub.Core.Constants;

/// <summary>
/// Constants for AES-GCM encryption with a key derived from a machine-bound secret.
/// Changing any format or derivation value makes existing encrypted files unreadable.
/// </summary>
public static class MachineBoundEncryptionConstants
{
    /// <summary>Encrypted file format version byte.</summary>
    public const byte FormatVersion = 1;

    /// <summary>PBKDF2-HMAC-SHA256 iterations for deriving the encryption key (OWASP guidance: 600,000).</summary>
    public const int KeyIterations = 600000;

    /// <summary>AES-256 key size, in bytes.</summary>
    public const int KeySizeBytes = 32;

    /// <summary>AES-GCM nonce size, in bytes.</summary>
    public const int NonceSizeBytes = 12;

    /// <summary>AES-GCM authentication tag size, in bytes.</summary>
    public const int TagSizeBytes = 16;

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
}
