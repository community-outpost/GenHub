namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents the type of web page being parsed.
/// </summary>
public enum PageType
{
    /// <summary>Unknown page type.</summary>
    Unknown,

    /// <summary>List view (e.g., addons/images listing).</summary>
    List,

    /// <summary>Summary or news feed page.</summary>
    Summary,

    /// <summary>Single mod/addon detail page.</summary>
    Detail,

    /// <summary>Specific file download page.</summary>
    FileDetail,
}
