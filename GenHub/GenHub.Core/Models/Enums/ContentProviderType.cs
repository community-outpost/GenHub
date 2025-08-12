namespace GenHub.Core.Models.Enums;

/// <summary>
/// Specifies the type of a content provider.
/// </summary>
public enum ContentProviderType
{
    /// <summary>
    /// Unspecified / unknown provider. This is the default value (0).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A provider that sources content from the local file system or network shares.
    /// </summary>
    FileSystem = 1,

    /// <summary>
    /// A provider that sources content from an HTTP/HTTPS endpoint.
    /// </summary>
    Http = 2,

    /// <summary>
    /// A provider that sources content from a Git repository.
    /// </summary>
    Git = 3,

    /// <summary>
    /// A provider that sources content from a centralized community registry.
    /// </summary>
    Registry = 4,

    /// <summary>
    /// A provider that sources content from the Steam Workshop.
    /// </summary>
    Steam = 5,

    /// <summary>
    /// A provider that sources content from ModDB.
    /// </summary>
    ModDb = 6,
}
