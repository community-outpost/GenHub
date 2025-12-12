namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Defines the type of conflict between content items.
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// Hard conflict - content cannot coexist at all.
    /// </summary>
    HardConflict = 0,

    /// <summary>
    /// Version conflict - specific versions are incompatible.
    /// </summary>
    VersionConflict = 1,

    /// <summary>
    /// File conflict - content modifies the same files.
    /// </summary>
    FileConflict = 2,

    /// <summary>
    /// Publisher conflict - content from certain publishers is incompatible.
    /// </summary>
    PublisherConflict = 3,

    /// <summary>
    /// Feature conflict - content provides overlapping features.
    /// </summary>
    FeatureConflict = 4,
}
