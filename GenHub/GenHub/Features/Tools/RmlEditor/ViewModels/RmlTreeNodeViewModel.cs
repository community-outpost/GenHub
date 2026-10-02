using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.RmlEditor;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace GenHub.Features.Tools.RmlEditor.ViewModels;

/// <summary>
/// Tree node wrapping an interface document node.
/// </summary>
public sealed partial class RmlTreeNodeViewModel : ObservableObject
{
    private const int MaxTextPreviewLength = 40;

    private readonly Func<RmlNode, string>? _detailProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="RmlTreeNodeViewModel"/> class.
    /// </summary>
    /// <param name="node">The wrapped document node.</param>
    /// <param name="parent">The parent node, or null for document roots.</param>
    /// <param name="detailProvider">The optional localized detail text provider.</param>
    public RmlTreeNodeViewModel(RmlNode node, RmlTreeNodeViewModel? parent, Func<RmlNode, string>? detailProvider = null)
    {
        Node = node;
        Parent = parent;
        _detailProvider = detailProvider;
        _isExpanded = true;
    }

    /// <summary>
    /// Gets the wrapped document node.
    /// </summary>
    public RmlNode Node { get; }

    /// <summary>
    /// Gets the parent node, or null for document roots.
    /// </summary>
    public RmlTreeNodeViewModel? Parent { get; }

    /// <summary>
    /// Gets the child nodes.
    /// </summary>
    public ObservableCollection<RmlTreeNodeViewModel> Children { get; } = [];

    /// <summary>
    /// Gets the display name of the node.
    /// </summary>
    public string DisplayName => Node switch
    {
        RmlElement element => BuildElementDisplayName(element),
        RmlText text => BuildTextDisplayName(text.Text),
        RmlComment comment => BuildCommentDisplayName(comment.Text),
        _ => "?",
    };

    /// <summary>
    /// Gets the detail text shown beside the display name.
    /// </summary>
    public string Detail => _detailProvider?.Invoke(Node) ?? string.Empty;

    /// <summary>
    /// Gets the icon kind for the tree row.
    /// </summary>
    public string IconKind => Node switch
    {
        RmlElement => "CodeTags",
        RmlText => "TextShort",
        RmlComment => "CommentOutline",
        _ => "HelpCircleOutline",
    };

    /// <summary>
    /// Gets a value indicating whether the node wraps an element.
    /// </summary>
    public bool IsElement => Node is RmlElement;

    /// <summary>
    /// Gets or sets whether the node is expanded in the tree.
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// Refreshes display text after the wrapped node changed.
    /// </summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Detail));
    }

    private static string BuildElementDisplayName(RmlElement element)
    {
        var name = "<" + element.Tag + ">";
        var id = element.ElementId;
        if (!string.IsNullOrEmpty(id))
        {
            name += " #" + id;
        }

        var classes = element.Classes;
        if (classes.Count > 0)
        {
            name += " ." + string.Join('.', classes.Take(3));
        }

        return name;
    }

    private static string BuildTextDisplayName(string text)
    {
        return "\"" + CollapsePreview(text) + "\"";
    }

    private static string BuildCommentDisplayName(string text)
    {
        return "<!-- " + CollapsePreview(text) + " -->";
    }

    private static string CollapsePreview(string text)
    {
        var collapsed = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (collapsed.Length > MaxTextPreviewLength)
        {
            collapsed = collapsed[..MaxTextPreviewLength] + "...";
        }

        return collapsed;
    }
}
