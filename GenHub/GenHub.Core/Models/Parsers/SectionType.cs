namespace GenHub.Core.Models.Parsers;

/// <summary>
/// Represents the type of content section extracted from a web page.
/// </summary>
public enum SectionType
{
    /// <summary>News article.</summary>
    Article,

    /// <summary>Embedded video.</summary>
    Video,

    /// <summary>Gallery image.</summary>
    Image,

    /// <summary>Downloadable file.</summary>
    File,

    /// <summary>User review.</summary>
    Review,

    /// <summary>Page comment.</summary>
    Comment,
}
