namespace GenHub.Core.Models.Tools.ReplayManager;

/// <summary>
/// Identifies the source of a replay URL.
/// </summary>
public enum ReplaySource
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
    /// Generals Online community platform.
    /// </summary>
    GeneralsOnline,

    /// <summary>
    /// GenTool community tool/website.
    /// </summary>
    GenTool,

    /// <summary>
    /// Direct link to a .rep or .zip file.
    /// </summary>
    DirectLink,
}
