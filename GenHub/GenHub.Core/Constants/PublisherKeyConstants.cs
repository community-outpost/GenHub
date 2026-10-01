namespace GenHub.Core.Constants;

/// <summary>
/// Constants for publisher public key import, signature verification, and the trusted key store.
/// </summary>
public static class PublisherKeyConstants
{
    /// <summary>
    /// File name of the encrypted trusted publisher key store under the application data directory.
    /// </summary>
    public const string StoreFileName = "publisher-keys.dat";

    /// <summary>
    /// Domain separation for the key store encryption: the PBKDF2 salt on Linux and macOS, and the
    /// DPAPI optional entropy on Windows.
    /// </summary>
    public const string StoreKeySalt = "GenHub.PublisherKeyStore.v1";

    /// <summary>
    /// Extension appended to a key store file that was moved aside by quarantine.
    /// </summary>
    public const string QuarantinedFileExtension = ".quarantined";

    /// <summary>
    /// UTC timestamp format used in quarantined key store file names.
    /// </summary>
    public const string QuarantineTimestampFormat = "yyyyMMdd'T'HHmmss'Z'";

    /// <summary>
    /// Number of random hex characters that keep quarantined file names unique within one second.
    /// </summary>
    public const int QuarantineSuffixLength = 8;

    /// <summary>
    /// Current schema version of the trusted publisher key store file.
    /// </summary>
    public const int StoreSchemaVersion = 1;

    /// <summary>
    /// PEM label of an X.509 SubjectPublicKeyInfo public key.
    /// </summary>
    public const string SubjectPublicKeyInfoPemLabel = "PUBLIC KEY";

    /// <summary>
    /// PEM label of a PKCS#1 RSA public key.
    /// </summary>
    public const string RsaPublicKeyPemLabel = "RSA PUBLIC KEY";

    /// <summary>
    /// Text every private key PEM label contains, covering PKCS#8, encrypted PKCS#8, PKCS#1, and SEC1 keys.
    /// </summary>
    public const string PrivateKeyPemLabelMarker = "PRIVATE KEY";

    /// <summary>
    /// Smallest accepted RSA modulus in bits.
    /// </summary>
    public const int MinimumRsaKeySizeBits = 2048;

    /// <summary>
    /// Smallest accepted ECDSA curve size in bits.
    /// </summary>
    public const int MinimumEcdsaKeySizeBits = 256;

    /// <summary>
    /// OID of the id-ecPublicKey algorithm in an EC SubjectPublicKeyInfo.
    /// </summary>
    public const string EcPublicKeyOid = "1.2.840.10045.2.1";

    /// <summary>
    /// OID of the NIST P-256 (secp256r1) named curve.
    /// </summary>
    public const string NistP256Oid = "1.2.840.10045.3.1.7";

    /// <summary>
    /// OID of the NIST P-384 (secp384r1) named curve.
    /// </summary>
    public const string NistP384Oid = "1.3.132.0.34";

    /// <summary>
    /// OID of the NIST P-521 (secp521r1) named curve.
    /// </summary>
    public const string NistP521Oid = "1.3.132.0.35";
}
