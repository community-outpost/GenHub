namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents the type of file section, distinguishing between main releases and addon files.
/// </summary>
public enum FileSectionType
{
    /// <summary>Files from the main releases/downloads section.</summary>
    Downloads,

    /// <summary>Files from the addons section.</summary>
    Addons,
}
