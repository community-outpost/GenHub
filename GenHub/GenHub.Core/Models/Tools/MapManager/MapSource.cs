namespace GenHub.Core.Models.Tools.MapManager;

/// <summary>
/// Identifies the source of a map URL.
/// </summary>
public enum MapSource
{
    /// <summary>
    /// Unknown source.
    /// </summary>
    Unknown,

    /// <summary>
    /// UploadThing file hosting.
    /// </summary>
    UploadThing,

    /// <summary>
    /// Direct link to a .map or .zip file.
    /// </summary>
    DirectLink,
}
