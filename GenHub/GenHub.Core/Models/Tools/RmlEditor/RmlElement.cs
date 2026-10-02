using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.RmlEditor;

/// <summary>
/// A single element of an interface (.rml) document.
/// </summary>
public sealed class RmlElement : RmlNode
{
    /// <summary>
    /// Gets or sets the tag name exactly as declared in the file.
    /// </summary>
    public string Tag { get; set; } = RmlConstants.Elements.Div;

    /// <summary>
    /// Gets the ordered attributes of this element.
    /// </summary>
    public List<RmlAttribute> Attributes { get; } = [];

    /// <summary>
    /// Gets the ordered child nodes of this element.
    /// </summary>
    public List<RmlNode> Children { get; } = [];

    /// <summary>
    /// Gets the value of the id attribute, or null when absent.
    /// </summary>
    public string? ElementId => GetAttribute(RmlConstants.Attributes.Id);

    /// <summary>
    /// Gets the class names of the class attribute.
    /// </summary>
    public IReadOnlyList<string> Classes => SplitClasses(GetAttribute(RmlConstants.Attributes.Class));

    /// <summary>
    /// Gets the child elements, skipping text and comment nodes.
    /// </summary>
    public IEnumerable<RmlElement> Elements => Children.OfType<RmlElement>();

    /// <summary>
    /// Gets the concatenated direct text content of this element.
    /// </summary>
    public string InnerText => string.Concat(Children.OfType<RmlText>().Select(t => t.Text));

    /// <summary>
    /// Gets a value indicating whether the element is a void element without children.
    /// </summary>
    public bool IsVoid => RmlConstants.Elements.Void.Contains(Tag, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Splits a class attribute value into individual class names.
    /// </summary>
    /// <param name="classValue">The raw class attribute value.</param>
    /// <returns>The individual class names.</returns>
    public static IReadOnlyList<string> SplitClasses(string? classValue)
    {
        if (string.IsNullOrWhiteSpace(classValue))
        {
            return [];
        }

        return classValue.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>
    /// Gets the value of an attribute by name, or null when absent.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>The attribute value or null.</returns>
    public string? GetAttribute(string name)
    {
        return Attributes.FirstOrDefault(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    /// <summary>
    /// Gets a value indicating whether an attribute is present.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>True when the attribute is present.</returns>
    public bool HasAttribute(string name)
    {
        return Attributes.Any(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Sets an attribute value, replacing the first existing entry or appending a new one.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The attribute value.</param>
    public void SetAttribute(string name, string value)
    {
        var index = Attributes.FindIndex(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            Attributes[index] = new RmlAttribute(Attributes[index].Name, value);
        }
        else
        {
            Attributes.Add(new RmlAttribute(name, value));
        }
    }

    /// <summary>
    /// Removes all attributes with the given name.
    /// </summary>
    /// <param name="name">The attribute name.</param>
    /// <returns>True when at least one attribute was removed.</returns>
    public bool RemoveAttribute(string name)
    {
        return Attributes.RemoveAll(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)) > 0;
    }

    /// <summary>
    /// Enumerates this element and all descendant elements in document order.
    /// </summary>
    /// <returns>The element subtree.</returns>
    public IEnumerable<RmlElement> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Elements)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }

    /// <summary>
    /// Creates a deep copy of this element with fresh node identities.
    /// </summary>
    /// <returns>The cloned element.</returns>
    public RmlElement Clone()
    {
        var clone = new RmlElement { Tag = Tag };
        clone.Attributes.AddRange(Attributes);
        foreach (var child in Children)
        {
            clone.Children.Add(CloneNode(child));
        }

        return clone;
    }

    private static RmlNode CloneNode(RmlNode node)
    {
        return node switch
        {
            RmlElement element => element.Clone(),
            RmlText text => new RmlText { Text = text.Text },
            RmlComment comment => new RmlComment { Text = comment.Text },
            _ => new RmlComment { Text = string.Empty },
        };
    }
}
