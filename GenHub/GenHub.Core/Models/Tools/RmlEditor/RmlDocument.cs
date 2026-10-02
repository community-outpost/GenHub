using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// An in-memory interface (.rml) document.
/// </summary>
public sealed class RmlDocument
{
    /// <summary>
    /// Gets or sets the head element holding links, titles, and embedded styles.
    /// </summary>
    public RmlElement Head { get; set; } = new() { Tag = Constants.RmlConstants.Document.Head };

    /// <summary>
    /// Gets or sets the body element holding the visible interface.
    /// </summary>
    public RmlElement Body { get; set; } = new() { Tag = Constants.RmlConstants.Document.Body };

    /// <summary>
    /// Gets or sets the source path this document was loaded from, when known.
    /// </summary>
    public string? SourcePath { get; set; }

    /// <summary>
    /// Gets the comments placed before the root element, preserved for round-tripping.
    /// </summary>
    public List<RmlComment> LeadingComments { get; } = [];

    /// <summary>
    /// Enumerates every element of the head and body in document order.
    /// </summary>
    /// <returns>All document elements.</returns>
    public IEnumerable<RmlElement> AllElements()
    {
        return Head.DescendantsAndSelf().Concat(Body.DescendantsAndSelf());
    }

    /// <summary>
    /// Finds the first element with the given id attribute.
    /// </summary>
    /// <param name="id">The id attribute value.</param>
    /// <returns>The matching element or null.</returns>
    public RmlElement? FindById(string id)
    {
        return AllElements().FirstOrDefault(e => string.Equals(e.ElementId, id, System.StringComparison.Ordinal));
    }
}
