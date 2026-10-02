using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Tools.RmlEditor;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace GenHub.Features.Tools.RmlEditor.ViewModels;

/// <summary>
/// Property editors for the selected interface node.
/// </summary>
public sealed partial class RmlElementPropertiesViewModel : ObservableObject
{
    private readonly RmlEditorViewModel _host;
    private bool _suppressCommit;

    /// <summary>
    /// Initializes a new instance of the <see cref="RmlElementPropertiesViewModel"/> class.
    /// </summary>
    /// <param name="host">The editor hosting this panel.</param>
    /// <param name="node">The selected document node.</param>
    /// <param name="headerText">The localized panel header text.</param>
    public RmlElementPropertiesViewModel(RmlEditorViewModel host, RmlNode node, string headerText)
    {
        _host = host;
        Node = node;
        HeaderText = headerText;

        if (node is RmlElement element)
        {
            _tag = element.Tag;
            _textContent = element.InnerText.Trim();
            _isTextEditable = element.Children.OfType<RmlText>().Count() <= 1;
        }
        else if (node is RmlText text)
        {
            _textContent = text.Text.Trim();
            _isTextEditable = true;
        }
        else if (node is RmlComment comment)
        {
            _textContent = comment.Text.Trim();
            _isTextEditable = true;
        }

        RebuildRows();
    }

    /// <summary>
    /// Gets the selected document node.
    /// </summary>
    public RmlNode Node { get; }

    /// <summary>
    /// Gets the localized panel header text.
    /// </summary>
    public string HeaderText { get; }

    /// <summary>
    /// Gets a value indicating whether the node is an element.
    /// </summary>
    public bool IsElement => Node is RmlElement;

    /// <summary>
    /// Gets a value indicating whether the node is a text run.
    /// </summary>
    public bool IsText => Node is RmlText;

    /// <summary>
    /// Gets a value indicating whether the node is a comment.
    /// </summary>
    public bool IsComment => Node is RmlComment;

    /// <summary>
    /// Gets the attribute rows of the selected element.
    /// </summary>
    public ObservableCollection<RmlPropertyRowViewModel> Attributes { get; } = [];

    /// <summary>
    /// Gets the inline style rows of the selected element.
    /// </summary>
    public ObservableCollection<RmlPropertyRowViewModel> InlineStyles { get; } = [];

    /// <summary>
    /// Gets the computed style rows of the selected element.
    /// </summary>
    public ObservableCollection<RmlPropertyRowViewModel> ComputedStyles { get; } = [];

    /// <summary>
    /// Gets the matched rule descriptions of the selected element.
    /// </summary>
    public ObservableCollection<string> MatchedRules { get; } = [];

    /// <summary>
    /// Gets or sets the element tag name.
    /// </summary>
    [ObservableProperty]
    private string _tag = string.Empty;

    /// <summary>
    /// Gets or sets the editable text content.
    /// </summary>
    [ObservableProperty]
    private string _textContent = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the text content can be edited directly.
    /// </summary>
    [ObservableProperty]
    private bool _isTextEditable;

    /// <summary>
    /// Gets or sets the new attribute name.
    /// </summary>
    [ObservableProperty]
    private string _newAttributeName = string.Empty;

    /// <summary>
    /// Gets or sets the new attribute value.
    /// </summary>
    [ObservableProperty]
    private string _newAttributeValue = string.Empty;

    /// <summary>
    /// Gets or sets the new inline style property.
    /// </summary>
    [ObservableProperty]
    private string _newStyleProperty = string.Empty;

    /// <summary>
    /// Gets or sets the new inline style value.
    /// </summary>
    [ObservableProperty]
    private string _newStyleValue = string.Empty;

    /// <summary>
    /// Refreshes every row from the node without committing edits.
    /// </summary>
    public void RefreshFromNode()
    {
        _suppressCommit = true;
        try
        {
            if (Node is RmlElement element)
            {
                Tag = element.Tag;
                if (IsTextEditable)
                {
                    TextContent = element.InnerText.Trim();
                }
            }
            else if (Node is RmlText text)
            {
                TextContent = text.Text.Trim();
            }
            else if (Node is RmlComment comment)
            {
                TextContent = comment.Text.Trim();
            }

            RebuildRows();
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    /// <summary>
    /// Adds the staged attribute to the selected element.
    /// </summary>
    [RelayCommand]
    private void AddAttribute()
    {
        if (Node is RmlElement)
        {
            _host.AddAttribute(Node.Id, NewAttributeName, NewAttributeValue);
        }
    }

    /// <summary>
    /// Adds the staged inline style declaration to the selected element.
    /// </summary>
    [RelayCommand]
    private void AddInlineStyle()
    {
        if (Node is RmlElement)
        {
            _host.AddInlineStyle(Node.Id, NewStyleProperty, NewStyleValue);
        }
    }

    partial void OnTagChanged(string value)
    {
        if (!_suppressCommit && Node is RmlElement)
        {
            _host.CommitNodeTag(Node.Id, value);
        }
    }

    partial void OnTextContentChanged(string value)
    {
        if (_suppressCommit || !IsTextEditable)
        {
            return;
        }

        if (Node is RmlElement)
        {
            _host.CommitElementText(Node.Id, value);
        }
        else
        {
            _host.CommitNodeText(Node.Id, value);
        }
    }

    private void RebuildRows()
    {
        Attributes.Clear();
        InlineStyles.Clear();
        if (Node is not RmlElement element)
        {
            return;
        }

        var nodeId = element.Id;
        foreach (var attribute in element.Attributes)
        {
            Attributes.Add(new RmlPropertyRowViewModel(
                attribute.Name,
                attribute.Value,
                (name, value) => _host.CommitAttribute(nodeId, name, value),
                name => _host.RemoveAttribute(nodeId, name)));
        }

        foreach (var declaration in _host.GetInlineDeclarations(element))
        {
            var label = declaration.Property + (declaration.Important ? " !important" : string.Empty);
            InlineStyles.Add(new RmlPropertyRowViewModel(
                label,
                declaration.Value,
                (name, value) => _host.CommitInlineStyle(nodeId, name, value),
                name => _host.RemoveInlineStyle(nodeId, name)));
        }
    }
}
