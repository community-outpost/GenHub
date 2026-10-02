using System;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// Base class for nodes of an interface document tree.
/// </summary>
public class RmlNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RmlNode"/> class.
    /// Restricted to node types so the shared identity implementation stays in one place.
    /// </summary>
    protected RmlNode()
    {
    }

    /// <summary>
    /// Gets the stable identity of this node for editor tracking.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();
}
