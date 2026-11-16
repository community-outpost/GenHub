namespace GenHub.Core.Models.Enums;

/// <summary>
/// Specifies the type of a content provider.
/// </summary>
public enum ContentProviderType
{
    /// <summary>
    /// Unspecified / unknown provider. This is the default value (0).
    /// </summary>
    Unknown,

    /// <summary>
    /// A provider that sources content from the local file system or network shares.
    /// </summary>
    FileSystem,

    /// <summary>
    /// A provider that sources content from an HTTP/HTTPS endpoint.
    /// </summary>
    Http,

    /// <summary>
    /// A provider that sources content from a Git repository.
    /// </summary>
    Git,

    /// <summary>
    /// A provider that sources content from a centralized community registry.
    /// </summary>
    Registry,

    /// <summary>
    /// A provider that sources content from the Steam Workshop.
    /// </summary>
    Steam,

    /// <summary>
    /// A provider that sources content from ModDB.
    /// </summary>
    ModDb,
}
