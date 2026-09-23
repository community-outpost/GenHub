namespace GenHub.Core.Models.Content;

/// <summary>
/// Represents the current phase of a content storage operation.
/// </summary>
public enum ContentStoragePhase
{
    /// <summary>
    /// Manifest files are being stored in CAS. This is the default phase.
    /// </summary>
    Storing,

    /// <summary>
    /// Files are being hashed while the content manifest is generated.
    /// </summary>
    Hashing,
}
