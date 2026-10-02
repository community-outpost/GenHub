using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Editors;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Interfaces.Tools.RmlEditor;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Features.Tools.RmlEditor.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.RmlEditor.ViewModels;

/// <summary>
/// ViewModel for the RML editor tool. Edits interface documents with undo support.
/// </summary>
[method: SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for RML editor tool orchestrator.")]
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for RML editor tool orchestrator.")]
public sealed partial class RmlEditorViewModel(
    IRmlDocumentService rmlDocumentService,
    IRcssDocumentService rcssDocumentService,
    RmlPreviewBuilder previewBuilder,
    RmlImageResolver imageResolver,
    INotificationService notificationService,
    ILocalizationService localizationService,
    IDialogService dialogService,
    ILogger<RmlEditorViewModel> logger,
    ITelemetryService? telemetryService = null) : EditorToolViewModelBase(notificationService, localizationService, dialogService)
{
    private sealed record StyleSheetEntry(RcssDocument Document, string SourceLabel, string? FilePath, bool IsEmbedded);

    private readonly Stack<RmlEditAction> _undoStack = new();
    private readonly Stack<RmlEditAction> _redoStack = new();
    private readonly List<StyleSheetEntry> _sheets = [];
    private readonly Dictionary<Guid, RmlNode> _nodeIndex = [];
    private readonly Dictionary<Guid, RmlElement?> _nodeParents = [];
    private FileExplorerViewModel? _fileExplorer;
    private RmlDocument? _document;
    private RmlNode? _copiedNode;
    private bool _isCut;
    private int _historyVersion;
    private int _savedHistoryVersion;
    private int _previewGeneration;
    private CancellationTokenSource? _previewCts;
    private RmlPreviewResult? _lastPreview;
    private bool _suppressSelectionRefresh;
    private bool _hasDocument;

    /// <summary>
    /// Gets the root tree nodes of the edited document.
    /// </summary>
    public ObservableCollection<RmlTreeNodeViewModel> RootNodes { get; } = [];

    /// <summary>
    /// Gets the style rules of the loaded style sheets.
    /// </summary>
    public ObservableCollection<RmlStyleRuleViewModel> StyleRules { get; } = [];

    /// <summary>
    /// Gets the declarations of the selected style rule.
    /// </summary>
    public ObservableCollection<RmlPropertyRowViewModel> RuleDeclarations { get; } = [];

    /// <summary>
    /// Gets the shared file explorer listing interface files.
    /// </summary>
    public FileExplorerViewModel FileExplorer => _fileExplorer ??= CreateFileExplorer();

    /// <summary>
    /// Gets a value indicating whether a style rule is currently selected.
    /// </summary>
    public bool HasSelectedStyleRule => SelectedStyleRule is not null;

    /// <summary>
    /// Gets a value indicating whether the selected style rule can be edited.
    /// </summary>
    public bool CanEditSelectedRule => SelectedStyleRule?.IsEditable == true;

    /// <summary>
    /// Gets or sets the selected tree node.
    /// </summary>
    [ObservableProperty]
    private RmlTreeNodeViewModel? _selectedNode;

    /// <summary>
    /// Gets or sets the element tree filter text.
    /// </summary>
    [ObservableProperty]
    private string _elementsFilter = string.Empty;

    /// <summary>
    /// Gets or sets the active tab in the left sidebar (0 = Elements, 1 = Files, 2 = Styles).
    /// </summary>
    [ObservableProperty]
    private int _leftSidebarTabIndex;

    /// <summary>
    /// Gets or sets the preview content control.
    /// </summary>
    [ObservableProperty]
    private Control? _previewContent;

    /// <summary>
    /// Gets or sets a value indicating whether the center pane shows source instead of preview.
    /// </summary>
    [ObservableProperty]
    private bool _isSourceView;

    /// <summary>
    /// Gets or sets the source text shown in the source view.
    /// </summary>
    [ObservableProperty]
    private string _sourceText = string.Empty;

    /// <summary>
    /// Gets or sets the source error text, or null when the source parses.
    /// </summary>
    [ObservableProperty]
    private string? _sourceError;

    /// <summary>
    /// Gets or sets the selected style rule.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedStyleRule))]
    [NotifyPropertyChangedFor(nameof(CanEditSelectedRule))]
    private RmlStyleRuleViewModel? _selectedStyleRule;

    /// <summary>
    /// Gets or sets the staged selectors for a new style rule.
    /// </summary>
    [ObservableProperty]
    private string _newRuleSelectors = string.Empty;

    /// <summary>
    /// Gets or sets the staged property for a new rule declaration.
    /// </summary>
    [ObservableProperty]
    private string _newRuleProperty = string.Empty;

    /// <summary>
    /// Gets or sets the staged value for a new rule declaration.
    /// </summary>
    [ObservableProperty]
    private string _newRuleValue = string.Empty;

    /// <summary>
    /// Gets or sets the target sheet label for new style rules.
    /// </summary>
    [ObservableProperty]
    private string _targetSheetLabel = string.Empty;

    /// <summary>
    /// Gets or sets the asset preview status text.
    /// </summary>
    [ObservableProperty]
    private string _assetStatusText = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether any referenced image is missing art.
    /// </summary>
    [ObservableProperty]
    private bool _hasMissingImages;

    /// <summary>
    /// Gets or sets an optional user-linked folder to load loose images from.
    /// </summary>
    [ObservableProperty]
    private string? _linkedAssetsFolder;

    /// <summary>
    /// Gets or sets whether the pan tool is active instead of element selection.
    /// </summary>
    [ObservableProperty]
    private bool _isPanMode;

    /// <summary>
    /// Gets or sets whether hidden elements render as placeholders.
    /// </summary>
    [ObservableProperty]
    private bool _showHidden;

    /// <summary>
    /// Gets or sets whether the document has unsaved changes.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    private bool _isModified;

    /// <summary>
    /// Gets or sets the path of the open file, or null for untitled documents.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    [NotifyPropertyChangedFor(nameof(HasDocument))]
    private string? _filePath;

    /// <summary>
    /// Gets or sets the property editors for the selected node.
    /// </summary>
    [ObservableProperty]
    private RmlElementPropertiesViewModel? _selectedProperties;

    /// <summary>
    /// Gets the document title with a modification marker.
    /// </summary>
    public override string DocumentTitle
    {
        get
        {
            var name = FilePath == null
                ? Localization.GetString("Tools.RmlEditor.Document.Untitled")
                : Path.GetFileName(FilePath);
            return IsModified ? $"*{name}" : name;
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether a document is open.
    /// </summary>
    public override bool HasDocument
    {
        get => _hasDocument;
        protected set => SetProperty(ref _hasDocument, value);
    }

    /// <inheritdoc />
    public override double ZoomMax => RmlConstants.Editor.MaxZoom;

    /// <summary>
    /// Gets a value indicating whether undo is available.
    /// </summary>
    public override bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether redo is available.
    /// </summary>
    public override bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the selected node can be copied.
    /// </summary>
    public override bool CanCopy => _document is not null && SelectedNode is not null;

    /// <summary>
    /// Gets a value indicating whether the selected node can be cut.
    /// </summary>
    public override bool CanCut => _document is not null && SelectedNode is not null && !IsRootNode(SelectedNode.Node);

    /// <summary>
    /// Gets a value indicating whether the clipboard can be pasted.
    /// </summary>
    public override bool CanPaste => _document is not null && _copiedNode is not null;

    /// <summary>
    /// Gets a value indicating whether the selected node can be duplicated.
    /// </summary>
    public override bool CanDuplicate => _document is not null && SelectedNode is not null && !IsRootNode(SelectedNode.Node);

    /// <summary>
    /// Gets a value indicating whether the selected node can be deleted.
    /// </summary>
    public override bool CanDelete => _document is not null && SelectedNode is not null && !IsRootNode(SelectedNode.Node);

    /// <summary>
    /// Gets a value indicating whether the document can be saved.
    /// </summary>
    public override bool CanSave => HasDocument;

    /// <summary>
    /// Gets a value indicating whether the document can be saved under a new path.
    /// </summary>
    public override bool CanSaveAs => HasDocument;

    /// <summary>
    /// Opens an interface file, asking to discard unsaved changes first.
    /// </summary>
    /// <param name="filePath">The full path of the file to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the file was opened successfully.</returns>
    public async Task<bool> OpenFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var result = await rmlDocumentService.ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            Notifications.ShowError(
                Localization.GetString("Tools.RmlEditor.Open.FailureTitle"),
                Localization.GetString("Tools.RmlEditor.Open.FailureMessage", result.FirstError ?? filePath),
                NotificationDurations.Long);
            return false;
        }

        await InvokeOnUIThreadAsync(() => AdoptDocument(result.Data, filePath)).ConfigureAwait(false);
        telemetryService?.TrackEvent(TelemetryConstants.Events.RmlDocumentOpened, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.FilePath] = TelemetryConstants.RmlEditor.AnonymousDocumentName,
            [TelemetryConstants.Properties.ElementCount] = result.Data.AllElements().Count(),
        });
        logger.LogInformation("Opened interface file {Path}", filePath);
        return true;
    }

    /// <summary>
    /// Opens a folder in the file explorer and optionally loads the first interface file.
    /// </summary>
    /// <param name="folderPath">The path of the directory to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the folder was opened.</returns>
    public async Task<bool> OpenFolderAsync(string folderPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
        {
            return false;
        }

        var uiFolder = Path.Combine(folderPath, RmlConstants.File.UiDirectoryName);
        if (Directory.Exists(uiFolder))
        {
            folderPath = uiFolder;
        }

        FileExplorer.Directory = folderPath;
        return await AdoptExplorerDirectoryAsync(folderPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Selects the node with the given identity, expanding its ancestors.
    /// </summary>
    /// <param name="nodeId">The node identity, or null to clear the selection.</param>
    public void SelectNode(Guid? nodeId)
    {
        if (nodeId is null)
        {
            SelectedNode = null;
            return;
        }

        var target = FindTreeNode(RootNodes, nodeId.Value);
        if (target is null)
        {
            return;
        }

        var parent = target.Parent;
        while (parent is not null)
        {
            parent.IsExpanded = true;
            parent = parent.Parent;
        }

        SelectedNode = target;
    }

    /// <summary>
    /// Gets the parsed inline style declarations of an element.
    /// </summary>
    /// <param name="element">The element to inspect.</param>
    /// <returns>The parsed declarations, or empty when absent or malformed.</returns>
    public IReadOnlyList<RcssDeclaration> GetInlineDeclarations(RmlElement element)
    {
        var style = element.GetAttribute(RmlConstants.Attributes.Style);
        if (string.IsNullOrWhiteSpace(style))
        {
            return [];
        }

        var parsed = rcssDocumentService.ParseInlineStyle(style);
        return parsed.Success && parsed.Data is not null ? parsed.Data : [];
    }

    /// <summary>
    /// Commits a tag name change for an element.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="tag">The new tag name.</param>
    public void CommitNodeTag(Guid nodeId, string tag)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element || IsRootElement(element))
        {
            return;
        }

        tag = tag.Trim().ToLowerInvariant();
        if (!IsValidTagName(tag))
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.InvalidTag", tag));
            SelectedProperties?.RefreshFromNode();
            return;
        }

        if (string.Equals(element.Tag, tag, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var before = element.Tag;
        element.Tag = tag;
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.RenameTag", before, tag),
            () =>
            {
                element.Tag = tag;
                RebuildAll();
            },
            () =>
            {
                element.Tag = before;
                RebuildAll();
            }));
        RebuildAll();
    }

    /// <summary>
    /// Commits the single text child of an element.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="text">The new text.</param>
    public void CommitElementText(Guid nodeId, string text)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element)
        {
            return;
        }

        var child = element.Children.OfType<RmlText>().SingleOrDefault();
        if (child is null)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            child = new RmlText { Text = text };
            element.Children.Add(child);
            PushUndo(new RmlEditAction(
                Localization.GetString("Tools.RmlEditor.History.EditText", DescribeNode(element)),
                () =>
                {
                    element.Children.Add(child);
                    RebuildAll();
                },
                () =>
                {
                    element.Children.Remove(child);
                    RebuildAll();
                }));
            RebuildAll();
            return;
        }

        CommitNodeText(child.Id, text);
    }

    /// <summary>
    /// Commits a text or comment content change.
    /// </summary>
    /// <param name="nodeId">The node identity.</param>
    /// <param name="text">The new content.</param>
    public void CommitNodeText(Guid nodeId, string text)
    {
        if (_document is null)
        {
            return;
        }

        var node = _nodeIndex.GetValueOrDefault(nodeId);
        if (node is RmlText textNode)
        {
            if (string.Equals(textNode.Text.Trim(), text.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            var before = textNode.Text;
            textNode.Text = text;
            PushUndo(new RmlEditAction(
                Localization.GetString("Tools.RmlEditor.History.EditText", DescribeNode(node)),
                () =>
                {
                    textNode.Text = text;
                    RebuildAll();
                },
                () =>
                {
                    textNode.Text = before;
                    RebuildAll();
                }));
            RebuildAll();
        }
        else if (node is RmlComment comment)
        {
            if (string.Equals(comment.Text.Trim(), text.Trim(), StringComparison.Ordinal))
            {
                return;
            }

            var before = comment.Text;
            comment.Text = text;
            PushUndo(new RmlEditAction(
                Localization.GetString("Tools.RmlEditor.History.EditComment", DescribeNode(node)),
                () =>
                {
                    comment.Text = text;
                    RebuildAll();
                },
                () =>
                {
                    comment.Text = before;
                    RebuildAll();
                }));
            RebuildAll();
        }
    }

    /// <summary>
    /// Commits an attribute value change.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The new value.</param>
    public void CommitAttribute(Guid nodeId, string name, string value)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element)
        {
            return;
        }

        var before = element.GetAttribute(name);
        if (string.Equals(before, value, StringComparison.Ordinal))
        {
            return;
        }

        element.SetAttribute(name, value);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.EditAttribute", name),
            () =>
            {
                element.SetAttribute(name, value);
                RebuildAll();
                SelectNode(nodeId);
            },
            () =>
            {
                RestoreAttribute(element, name, before);
                RebuildAll();
                SelectNode(nodeId);
            }));
        RebuildAll();
    }

    /// <summary>
    /// Removes an attribute from an element.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="name">The attribute name.</param>
    public void RemoveAttribute(Guid nodeId, string name)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element)
        {
            return;
        }

        var before = element.GetAttribute(name);
        if (before is null)
        {
            return;
        }

        element.RemoveAttribute(name);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.RemoveAttribute", name),
            () =>
            {
                element.RemoveAttribute(name);
                RebuildAll();
                SelectNode(nodeId);
            },
            () =>
            {
                element.SetAttribute(name, before);
                RebuildAll();
                SelectNode(nodeId);
            }));
        RebuildAll();
    }

    /// <summary>
    /// Adds an attribute to an element.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="value">The attribute value.</param>
    public void AddAttribute(Guid nodeId, string name, string value)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element)
        {
            return;
        }

        name = name.Trim();
        if (!IsValidAttributeName(name))
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.InvalidAttributeName", name));
            return;
        }

        if (element.HasAttribute(name))
        {
            CommitAttribute(nodeId, name, value);
            return;
        }

        element.SetAttribute(name, value);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.AddAttribute", name),
            () =>
            {
                element.SetAttribute(name, value);
                RebuildAll();
                SelectNode(nodeId);
            },
            () =>
            {
                element.RemoveAttribute(name);
                RebuildAll();
                SelectNode(nodeId);
            }));
        RebuildAll();
        if (SelectedProperties is not null)
        {
            SelectedProperties.NewAttributeName = string.Empty;
            SelectedProperties.NewAttributeValue = string.Empty;
        }
    }

    /// <summary>
    /// Commits an inline style declaration change.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="label">The declaration label, with an optional important suffix.</param>
    /// <param name="value">The new value.</param>
    public void CommitInlineStyle(Guid nodeId, string label, string value)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element)
        {
            return;
        }

        var (property, important) = SplitImportant(label);
        var declarations = TryGetInlineDeclarations(element);
        if (declarations is null)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.UnparsableInlineStyle"));
            SelectedProperties?.RefreshFromNode();
            return;
        }

        var before = rcssDocumentService.WriteInlineStyle(declarations);
        MutateDeclarations(declarations, property, value, important);
        ApplyInlineStyle(element, declarations, before, Localization.GetString("Tools.RmlEditor.History.EditInlineStyle", property));
    }

    /// <summary>
    /// Removes an inline style declaration from an element.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="label">The declaration label, with an optional important suffix.</param>
    public void RemoveInlineStyle(Guid nodeId, string label)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element)
        {
            return;
        }

        var (property, _) = SplitImportant(label);
        var declarations = TryGetInlineDeclarations(element);
        if (declarations is null)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.UnparsableInlineStyle"));
            return;
        }

        var before = rcssDocumentService.WriteInlineStyle(declarations);
        declarations.RemoveAll(d => string.Equals(d.Property, property, StringComparison.OrdinalIgnoreCase));
        ApplyInlineStyle(element, declarations, before, Localization.GetString("Tools.RmlEditor.History.RemoveInlineStyle", property));
    }

    /// <summary>
    /// Adds an inline style declaration to an element.
    /// </summary>
    /// <param name="nodeId">The element identity.</param>
    /// <param name="property">The property name.</param>
    /// <param name="value">The property value.</param>
    public void AddInlineStyle(Guid nodeId, string property, string value)
    {
        if (_document is null || _nodeIndex.GetValueOrDefault(nodeId) is not RmlElement element)
        {
            return;
        }

        property = property.Trim().ToLowerInvariant();
        if (!IsValidStyleProperty(property) || string.IsNullOrWhiteSpace(value))
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.InvalidStyleDeclaration", property));
            return;
        }

        var declarations = TryGetInlineDeclarations(element);
        if (declarations is null)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.UnparsableInlineStyle"));
            return;
        }

        var before = rcssDocumentService.WriteInlineStyle(declarations);
        MutateDeclarations(declarations, property, value.Trim(), important: false);
        ApplyInlineStyle(element, declarations, before, Localization.GetString("Tools.RmlEditor.History.AddInlineStyle", property));
        if (SelectedProperties is not null)
        {
            SelectedProperties.NewStyleProperty = string.Empty;
            SelectedProperties.NewStyleValue = string.Empty;
        }
    }

    /// <summary>
    /// Commits a style rule selector change.
    /// </summary>
    /// <param name="sheetIndex">The style sheet index.</param>
    /// <param name="ruleIndex">The rule index within its sheet.</param>
    /// <param name="selectors">The new selector text.</param>
    public void CommitRuleSelectors(int sheetIndex, int ruleIndex, string selectors)
    {
        var rule = GetEditableRule(sheetIndex, ruleIndex);
        if (rule is null)
        {
            RebuildStyleRules();
            return;
        }

        var parsed = SplitSelectors(selectors);
        if (parsed.Count == 0)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.InvalidSelectors"));
            RebuildStyleRules();
            return;
        }

        var before = rule.Selectors.ToList();
        rule.Selectors.Clear();
        rule.Selectors.AddRange(parsed);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.EditSelectors", parsed[0]),
            () =>
            {
                rule.Selectors.Clear();
                rule.Selectors.AddRange(parsed);
                RebuildAll();
            },
            () =>
            {
                rule.Selectors.Clear();
                rule.Selectors.AddRange(before);
                RebuildAll();
            }));
        RebuildAll();
    }

    /// <summary>
    /// Commits a style rule declaration change.
    /// </summary>
    /// <param name="sheetIndex">The style sheet index.</param>
    /// <param name="ruleIndex">The rule index within its sheet.</param>
    /// <param name="label">The declaration label, with an optional important suffix.</param>
    /// <param name="value">The new value.</param>
    public void CommitRuleDeclaration(int sheetIndex, int ruleIndex, string label, string value)
    {
        var rule = GetEditableRule(sheetIndex, ruleIndex);
        if (rule is null)
        {
            return;
        }

        var (property, important) = SplitImportant(label);
        var declarations = rule.Declarations.ToList();
        var before = rcssDocumentService.WriteInlineStyle(declarations);
        MutateDeclarations(declarations, property, value, important);
        ApplyRuleDeclarations(rule, declarations, before, Localization.GetString("Tools.RmlEditor.History.EditDeclaration", property));
    }

    /// <summary>
    /// Removes a style rule declaration.
    /// </summary>
    /// <param name="sheetIndex">The style sheet index.</param>
    /// <param name="ruleIndex">The rule index within its sheet.</param>
    /// <param name="label">The declaration label, with an optional important suffix.</param>
    public void RemoveRuleDeclaration(int sheetIndex, int ruleIndex, string label)
    {
        var rule = GetEditableRule(sheetIndex, ruleIndex);
        if (rule is null)
        {
            return;
        }

        var (property, _) = SplitImportant(label);
        var declarations = rule.Declarations.ToList();
        var before = rcssDocumentService.WriteInlineStyle(declarations);
        declarations.RemoveAll(d => string.Equals(d.Property, property, StringComparison.OrdinalIgnoreCase));
        ApplyRuleDeclarations(rule, declarations, before, Localization.GetString("Tools.RmlEditor.History.RemoveDeclaration", property));
    }

    /// <summary>
    /// Adds a style rule declaration.
    /// </summary>
    /// <param name="sheetIndex">The style sheet index.</param>
    /// <param name="ruleIndex">The rule index within its sheet.</param>
    /// <param name="property">The property name.</param>
    /// <param name="value">The property value.</param>
    public void AddRuleDeclaration(int sheetIndex, int ruleIndex, string property, string value)
    {
        var rule = GetEditableRule(sheetIndex, ruleIndex);
        if (rule is null)
        {
            return;
        }

        property = property.Trim().ToLowerInvariant();
        if (!IsValidStyleProperty(property) || string.IsNullOrWhiteSpace(value))
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.InvalidStyleDeclaration", property));
            return;
        }

        var declarations = rule.Declarations.ToList();
        var before = rcssDocumentService.WriteInlineStyle(declarations);
        MutateDeclarations(declarations, property, value.Trim(), important: false);
        ApplyRuleDeclarations(rule, declarations, before, Localization.GetString("Tools.RmlEditor.History.AddDeclaration", property));
    }

    /// <summary>
    /// Adds a style rule to the target style sheet.
    /// </summary>
    /// <param name="selectors">The selector text.</param>
    public void AddRule(string selectors)
    {
        var entry = FindRuleTargetSheet();
        if (entry is null)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.NoEditableSheet"));
            return;
        }

        var parsed = SplitSelectors(selectors);
        if (parsed.Count == 0)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.InvalidSelectors"));
            return;
        }

        var rule = new RcssRule();
        rule.Selectors.AddRange(parsed);
        entry.Document.Rules.Add(rule);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.AddRule", parsed[0]),
            () =>
            {
                entry.Document.Rules.Add(rule);
                RebuildAll();
            },
            () =>
            {
                entry.Document.Rules.Remove(rule);
                RebuildAll();
            }));
        RebuildAll();
        NewRuleSelectors = string.Empty;
        SelectRule(_sheets.IndexOf(entry), entry.Document.Rules.Count - 1);
    }

    /// <summary>
    /// Deletes the selected style rule.
    /// </summary>
    public void DeleteSelectedRule()
    {
        if (SelectedStyleRule is null)
        {
            return;
        }

        var entry = GetSheetEntry(SelectedStyleRule.SheetIndex);
        if (entry is null || entry.IsEmbedded || entry.FilePath is null)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.CannotEditEmbedded"));
            return;
        }

        if (SelectedStyleRule.RuleIndex < 0 || SelectedStyleRule.RuleIndex >= entry.Document.Rules.Count)
        {
            return;
        }

        var rule = entry.Document.Rules[SelectedStyleRule.RuleIndex];
        var index = SelectedStyleRule.RuleIndex;
        entry.Document.Rules.RemoveAt(index);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.DeleteRule", rule.Selectors.FirstOrDefault() ?? string.Empty),
            () =>
            {
                entry.Document.Rules.Remove(rule);
                RebuildAll();
            },
            () =>
            {
                entry.Document.Rules.Insert(Math.Clamp(index, 0, entry.Document.Rules.Count), rule);
                RebuildAll();
            }));
        RebuildAll();
    }

    /// <inheritdoc />
    protected override bool HasUnsavedChanges => IsModified;

    /// <inheritdoc />
    protected override async Task OnNewDocumentAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var document = CreateDefaultDocument();
        await InvokeOnUIThreadAsync(() =>
        {
            AdoptDocument(document, null);
            IsModified = true;
        }).ConfigureAwait(false);
        logger.LogInformation("Created new interface document");
    }

    /// <inheritdoc />
    protected override async Task OnOpenFolderAsync(CancellationToken cancellationToken)
    {
        var localPath = await PickFolderAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(localPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await OpenFolderAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override async Task OnOpenFileAsync(CancellationToken cancellationToken)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localization.GetString("Tools.RmlEditor.FileDialog.OpenTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localization.GetString("Tools.RmlEditor.FileDialog.FilterName"))
                {
                    Patterns = [RmlConstants.File.Pattern],
                },
            ],
        });
        if (files.Count > 0)
        {
            var localPath = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                await OpenFileAsync(localPath, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnSaveAsync(CancellationToken cancellationToken)
    {
        if (_document is null)
        {
            return;
        }

        if (string.IsNullOrEmpty(FilePath))
        {
            await OnSaveAsAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await WriteDocumentToFileAsync(FilePath, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnSaveAsAsync(CancellationToken cancellationToken)
    {
        if (_document is null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localization.GetString("Tools.RmlEditor.FileDialog.SaveTitle"),
            SuggestedFileName = FilePath is null ? null : Path.GetFileName(FilePath),
            FileTypeChoices =
            [
                new FilePickerFileType(Localization.GetString("Tools.RmlEditor.FileDialog.FilterName"))
                {
                    Patterns = [RmlConstants.File.Pattern],
                },
            ],
        });
        var localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            var saved = await WriteDocumentToFileAsync(localPath, cancellationToken).ConfigureAwait(false);
            if (saved)
            {
                FilePath = localPath;
                SyncFilesDirectory(localPath);
            }
        }
    }

    /// <inheritdoc />
    protected override void OnUndo()
    {
        if (_undoStack.Count == 0 || IsTextInputFocused())
        {
            return;
        }

        var action = _undoStack.Pop();
        _redoStack.Push(action);
        _historyVersion--;
        action.Undo();
        SyncModifiedFromHistory();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnRedo()
    {
        if (_redoStack.Count == 0 || IsTextInputFocused())
        {
            return;
        }

        var action = _redoStack.Pop();
        _undoStack.Push(action);
        _historyVersion++;
        action.Redo();
        SyncModifiedFromHistory();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnCopy()
    {
        if (_document is null || SelectedNode is null || IsTextInputFocused())
        {
            return;
        }

        _copiedNode = CloneNode(SelectedNode.Node);
        _isCut = false;
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnCut()
    {
        if (_document is null || SelectedNode is null || IsTextInputFocused() || IsRootNode(SelectedNode.Node))
        {
            return;
        }

        var node = SelectedNode.Node;
        var parent = _nodeParents.GetValueOrDefault(node.Id);
        var siblings = parent?.Children ?? [];
        var index = siblings.IndexOf(node);
        _copiedNode = CloneNode(node);
        _isCut = true;
        siblings.Remove(node);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.CutNode", DescribeNode(node)),
            () =>
            {
                siblings.Remove(node);
                RebuildAll();
            },
            () =>
            {
                siblings.Insert(Math.Clamp(index, 0, siblings.Count), node);
                RebuildAll();
                SelectNode(node.Id);
            }));
        RebuildAll();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override Task OnPasteAsync(CancellationToken cancellationToken)
    {
        if (_document is null || _copiedNode is null || IsTextInputFocused())
        {
            return Task.CompletedTask;
        }

        var target = PasteTarget();
        if (target is null)
        {
            return Task.CompletedTask;
        }

        var node = _isCut ? _copiedNode : CloneNode(_copiedNode);
        var wasCut = _isCut;
        _isCut = false;
        target.Children.Add(node);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.PasteNode", DescribeNode(node)),
            () =>
            {
                target.Children.Add(node);
                RebuildAll();
                SelectNode(node.Id);
            },
            () =>
            {
                target.Children.Remove(node);
                RebuildAll();
            }));
        RebuildAll();
        SelectNode(node.Id);
        RefreshEditorCommands();
        logger.LogDebug("Pasted interface node as child of {Tag} (cut: {WasCut})", target.Tag, wasCut);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void OnDuplicate()
    {
        if (_document is null || SelectedNode is null || IsTextInputFocused() || IsRootNode(SelectedNode.Node))
        {
            return;
        }

        var node = SelectedNode.Node;
        var parent = _nodeParents.GetValueOrDefault(node.Id);
        var siblings = parent?.Children;
        if (siblings is null)
        {
            return;
        }

        var clone = CloneNode(node);
        var index = siblings.IndexOf(node);
        siblings.Insert(index + 1, clone);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.DuplicateNode", DescribeNode(node)),
            () =>
            {
                var at = siblings.IndexOf(node);
                siblings.Insert(at + 1, clone);
                RebuildAll();
                SelectNode(clone.Id);
            },
            () =>
            {
                siblings.Remove(clone);
                RebuildAll();
                SelectNode(node.Id);
            }));
        RebuildAll();
        SelectNode(clone.Id);
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnDelete()
    {
        if (_document is null || SelectedNode is null || IsTextInputFocused() || IsRootNode(SelectedNode.Node))
        {
            return;
        }

        if (SelectedStyleRule is not null && LeftSidebarTabIndex == 2 && StyleRules.Contains(SelectedStyleRule))
        {
            DeleteSelectedRule();
            return;
        }

        var node = SelectedNode.Node;
        var parent = _nodeParents.GetValueOrDefault(node.Id);
        var siblings = parent?.Children;
        if (siblings is null)
        {
            return;
        }

        var index = siblings.IndexOf(node);
        siblings.Remove(node);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.DeleteNode", DescribeNode(node)),
            () =>
            {
                siblings.Remove(node);
                RebuildAll();
            },
            () =>
            {
                siblings.Insert(Math.Clamp(index, 0, siblings.Count), node);
                RebuildAll();
                SelectNode(node.Id);
            }));
        RebuildAll();
        if (parent is not null)
        {
            SelectNode(parent.Id);
        }

        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
        {
            return;
        }

        if (disposing)
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
            _previewCts = null;
            imageResolver.ClearCache();
            if (_fileExplorer is not null)
            {
                _fileExplorer.FileActivated -= OnExplorerFileActivated;
            }
        }

        base.Dispose(disposing);
    }

    private static RmlDocument CreateDefaultDocument()
    {
        // Seed texts below are intentional sample document content, not UI chrome.
        var document = new RmlDocument();
        document.Head.Children.Add(new RmlElement { Tag = RmlConstants.Document.Title });
        var title = (RmlElement)document.Head.Children[0];
        title.Children.Add(new RmlText { Text = "Untitled screen" });
        var panel = new RmlElement { Tag = RmlConstants.Elements.Div };
        panel.SetAttribute(RmlConstants.Attributes.Class, RmlConstants.CssClasses.Screen);
        var heading = new RmlElement { Tag = RmlConstants.Elements.Heading1 };
        heading.Children.Add(new RmlText { Text = "New screen" });
        panel.Children.Add(heading);
        document.Body.Children.Add(panel);
        return document;
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

    private static string DescribeNode(RmlNode node)
    {
        return node switch
        {
            RmlElement element => string.IsNullOrEmpty(element.ElementId) ? $"<{element.Tag}>" : $"<{element.Tag} id='{element.ElementId}'>",
            RmlText text => "\"" + string.Join(' ', text.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)) + "\"",
            RmlComment => "<!-- -->",
            _ => "?",
        };
    }

    private static bool IsValidTagName(string tag)
    {
        if (string.IsNullOrEmpty(tag) || !char.IsLetter(tag[0]))
        {
            return false;
        }

        return tag.All(c => char.IsLetterOrDigit(c) || c == '-');
    }

    private static bool IsValidAttributeName(string name)
    {
        if (string.IsNullOrEmpty(name) || (!char.IsLetter(name[0]) && name[0] != '_' && name[0] != ':'))
        {
            return false;
        }

        return name.All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ':' || c == '.');
    }

    private static bool IsValidStyleProperty(string property)
    {
        if (string.IsNullOrEmpty(property) || (!char.IsLetter(property[0]) && property[0] != '-'))
        {
            return false;
        }

        return property.All(c => char.IsLetterOrDigit(c) || c == '-');
    }

    private static (string Property, bool Important) SplitImportant(string label)
    {
        const string suffix = " !important";
        if (label.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return (label.Substring(0, label.Length - suffix.Length).Trim(), true);
        }

        return (label.Trim(), false);
    }

    private static List<string> SplitSelectors(string selectors)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var depth = 0;
        char quote = '\0';
        foreach (var c in selectors)
        {
            if (quote != '\0')
            {
                current.Append(c);
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                quote = c;
                current.Append(c);
            }
            else if (c == '(' || c == '[')
            {
                depth++;
                current.Append(c);
            }
            else if ((c == ')' || c == ']') && depth > 0)
            {
                depth--;
                current.Append(c);
            }
            else if (c == ',' && depth == 0)
            {
                AddSelectorPart(result, current);
            }
            else
            {
                current.Append(c);
            }
        }

        AddSelectorPart(result, current);
        return result;
    }

    private static void AddSelectorPart(List<string> result, System.Text.StringBuilder current)
    {
        var selector = current.ToString().Trim();
        current.Clear();
        if (selector.Length > 0)
        {
            result.Add(selector);
        }
    }

    private static void RestoreAttribute(RmlElement element, string name, string? before)
    {
        if (before is null)
        {
            element.RemoveAttribute(name);
        }
        else
        {
            element.SetAttribute(name, before);
        }
    }

    private static void MutateDeclarations(List<RcssDeclaration> declarations, string property, string value, bool important)
    {
        var index = declarations.FindIndex(d => string.Equals(d.Property, property, StringComparison.OrdinalIgnoreCase));
        var declaration = new RcssDeclaration(property, value, important);
        if (index >= 0)
        {
            declarations[index] = declaration;
        }
        else
        {
            declarations.Add(declaration);
        }
    }

    private static List<string> CollectImageReferences(RmlDocument document, IReadOnlyList<RcssDocument> sheets)
    {
        var references = new List<string>();
        foreach (var element in document.AllElements())
        {
            var src = element.GetAttribute(RmlConstants.Attributes.Src);
            if (!string.IsNullOrWhiteSpace(src))
            {
                references.Add(src.Trim());
            }

            ExtractUrlReferences(element.GetAttribute(RmlConstants.Attributes.Style), references);
        }

        foreach (var sheet in sheets)
        {
            foreach (var rule in sheet.Rules)
            {
                foreach (var declaration in rule.Declarations)
                {
                    ExtractUrlReferences(declaration.Value, references);
                }
            }
        }

        return references.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ExtractUrlReferences(string? value, List<string> references)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        var index = 0;
        while (true)
        {
            var start = value.IndexOf("url(", index, StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return;
            }

            var end = value.IndexOf(')', start);
            if (end < 0)
            {
                return;
            }

            var reference = value.Substring(start + 4, end - start - 4).Trim().Trim('"', '\'');
            if (reference.Length > 0)
            {
                references.Add(reference);
            }

            index = end + 1;
        }
    }

    private static bool MatchesFilter(RmlNode node, string filter)
    {
        return node switch
        {
            RmlElement element => element.Tag.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (element.ElementId?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
                || element.Classes.Any(c => c.Contains(filter, StringComparison.OrdinalIgnoreCase)),
            RmlText text => text.Text.Contains(filter, StringComparison.OrdinalIgnoreCase),
            RmlComment comment => comment.Text.Contains(filter, StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    private static bool SubtreeMatches(RmlNode node, string filter)
    {
        if (MatchesFilter(node, filter))
        {
            return true;
        }

        return node is RmlElement element && element.Children.Any(child => SubtreeMatches(child, filter));
    }

    private static bool IsSubPathOf(string path, string basePath)
    {
        return PathHelper.IsPathWithinDirectory(basePath, path);
    }

    private static RmlTreeNodeViewModel? FindTreeNode(IEnumerable<RmlTreeNodeViewModel> nodes, Guid nodeId)
    {
        foreach (var node in nodes)
        {
            if (node.Node.Id == nodeId)
            {
                return node;
            }

            var found = FindTreeNode(node.Children, nodeId);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static async Task InvokeOnUIThreadAsync(Action action)
    {
        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            await Task.CompletedTask.ConfigureAwait(false);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    private FileExplorerViewModel CreateFileExplorer()
    {
        var explorer = new FileExplorerViewModel(logger)
        {
            FilePatterns = [RmlConstants.File.Pattern, RmlConstants.File.RcssPattern],
            ShowFileExtensions = true,
            BrowseFolderAsync = PickFolderAsync,
            DirectoryAdoptedAsync = (folder, cancellationToken) => AdoptExplorerDirectoryAsync(folder, cancellationToken),
        };
        explorer.FileActivated += OnExplorerFileActivated;
        return explorer;
    }

    private void AdoptDocument(RmlDocument document, string? filePath)
    {
        _document = document;
        FilePath = filePath;
        HasDocument = true;
        IsModified = false;
        IsSourceView = false;
        SourceText = string.Empty;
        SourceError = null;
        _historyVersion = 0;
        _savedHistoryVersion = 0;
        _undoStack.Clear();
        _redoStack.Clear();
        _copiedNode = null;
        _isCut = false;
        _sheets.Clear();
        if (!string.IsNullOrEmpty(filePath))
        {
            document.SourcePath = filePath;
            SyncFilesDirectory(filePath);
            FileExplorer.CurrentPath = filePath;
        }

        LoadStyleSheets();
        RebuildIndex();
        RebuildTree();
        RebuildStyleRules();
        RebuildProperties();
        RequestPreviewRefresh();
        RefreshEditorCommands();
    }

    private void LoadStyleSheets()
    {
        _sheets.Clear();
        if (_document is null)
        {
            UpdateTargetSheetLabel();
            return;
        }

        var directory = GetDocumentDirectory();
        if (directory is not null)
        {
            var shared = Path.Combine(directory, RmlConstants.File.SharedStyleSheetFileName);
            if (File.Exists(shared))
            {
                TryAddStyleSheetFile(shared, RmlConstants.File.SharedStyleSheetFileName);
            }
        }

        foreach (var link in _document.Head.Elements.Where(e => string.Equals(e.Tag, RmlConstants.Document.Link, StringComparison.OrdinalIgnoreCase)))
        {
            if (!string.Equals(link.GetAttribute(RmlConstants.Attributes.Type), RmlConstants.File.StyleSheetLinkType, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var href = link.GetAttribute(RmlConstants.Attributes.Href);
            if (string.IsNullOrWhiteSpace(href) || IsExternalReference(href))
            {
                continue;
            }

            if (directory is null)
            {
                continue;
            }

            string path = string.Empty;
            try
            {
                path = Path.Combine(directory, href.Trim());
            }
            catch (ArgumentException ex)
            {
                logger.LogDebug(ex, "Ignoring style sheet reference with unusable path: {Href}", href.Trim());
                continue;
            }

            if (!PathHelper.IsPathWithinDirectory(directory, path))
            {
                logger.LogDebug("Ignoring style sheet reference escaping the document folder: {Href}", href.Trim());
                continue;
            }

            if (File.Exists(path) && _sheets.All(s => !string.Equals(s.FilePath, path, StringComparison.OrdinalIgnoreCase)))
            {
                TryAddStyleSheetFile(path, href.Trim());
            }
        }

        var embedded = new RcssDocument();
        foreach (var style in _document.Head.Elements.Where(e => string.Equals(e.Tag, RmlConstants.Document.Style, StringComparison.OrdinalIgnoreCase)))
        {
            var parsed = rcssDocumentService.ParseText(style.InnerText);
            if (parsed.Success && parsed.Data is not null)
            {
                embedded.Rules.AddRange(parsed.Data.Rules);
                embedded.AtRules.AddRange(parsed.Data.AtRules);
            }
        }

        if (embedded.Rules.Count > 0)
        {
            _sheets.Add(new StyleSheetEntry(embedded, Localization.GetString("Tools.RmlEditor.Styles.EmbeddedLabel"), null, true));
        }

        UpdateTargetSheetLabel();
    }

    private void TryAddStyleSheetFile(string path, string label)
    {
        try
        {
            var content = File.ReadAllText(path);
            var parsed = rcssDocumentService.ParseText(content, path);
            if (parsed.Success && parsed.Data is not null)
            {
                _sheets.Add(new StyleSheetEntry(parsed.Data, label, path, false));
            }
            else
            {
                Notifications.ShowWarning(
                    Localization.GetString("Tools.RmlEditor.Styles.ParseWarningTitle"),
                    Localization.GetString("Tools.RmlEditor.Styles.ParseWarningMessage", label, parsed.FirstError ?? string.Empty),
                    NotificationDurations.Long);
                logger.LogWarning("Failed to parse style sheet {Path}: {Error}", path, parsed.FirstError);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to read style sheet {Path}", path);
        }
    }

    private void RebuildIndex()
    {
        _nodeIndex.Clear();
        _nodeParents.Clear();
        if (_document is null)
        {
            return;
        }

        IndexSubtree(_document.Head, null);
        IndexSubtree(_document.Body, null);
    }

    private void IndexSubtree(RmlElement element, RmlElement? parent)
    {
        _nodeIndex[element.Id] = element;
        _nodeParents[element.Id] = parent;
        foreach (var child in element.Children)
        {
            _nodeIndex[child.Id] = child;
            _nodeParents[child.Id] = element;
            if (child is RmlElement nested)
            {
                IndexNested(nested, element);
            }
        }
    }

    private void IndexNested(RmlElement element, RmlElement parent)
    {
        _nodeParents[element.Id] = parent;
        foreach (var child in element.Children)
        {
            _nodeIndex[child.Id] = child;
            _nodeParents[child.Id] = element;
            if (child is RmlElement nested)
            {
                IndexNested(nested, element);
            }
        }
    }

    private void RebuildTree()
    {
        var expanded = new HashSet<Guid>();
        CollectExpanded(RootNodes, expanded);
        var selectedId = SelectedNode?.Node.Id;
        _suppressSelectionRefresh = true;
        try
        {
            RootNodes.Clear();
            if (_document is null)
            {
                SelectedNode = null;
                return;
            }

            var filter = ElementsFilter.Trim();
            foreach (var root in new[] { _document.Head, _document.Body })
            {
                if (!string.IsNullOrEmpty(filter) && !SubtreeMatches(root, filter))
                {
                    continue;
                }

                var node = new RmlTreeNodeViewModel(root, null, NodeDetail)
                {
                    IsExpanded = expanded.Count == 0 || expanded.Contains(root.Id) || !string.IsNullOrEmpty(filter),
                };
                BuildTreeChildren(node, root, filter, expanded);
                RootNodes.Add(node);
            }

            SelectedNode = selectedId.HasValue ? FindTreeNode(RootNodes, selectedId.Value) : null;
        }
        finally
        {
            _suppressSelectionRefresh = false;
        }

        RebuildProperties();
    }

    private void BuildTreeChildren(RmlTreeNodeViewModel parent, RmlElement element, string filter, HashSet<Guid> expanded)
    {
        foreach (var child in element.Children)
        {
            if (!string.IsNullOrEmpty(filter) && !SubtreeMatches(child, filter))
            {
                continue;
            }

            if (child is RmlText text && string.IsNullOrWhiteSpace(text.Text))
            {
                continue;
            }

            var node = new RmlTreeNodeViewModel(child, parent, NodeDetail)
            {
                IsExpanded = expanded.Count == 0 || expanded.Contains(child.Id) || !string.IsNullOrEmpty(filter),
            };
            if (child is RmlElement nested)
            {
                BuildTreeChildren(node, nested, filter, expanded);
            }

            parent.Children.Add(node);
        }
    }

    private void RebuildProperties()
    {
        if (_document is null || SelectedNode is null)
        {
            SelectedProperties = null;
            return;
        }

        var node = SelectedNode.Node;
        if (!_nodeIndex.ContainsKey(node.Id))
        {
            SelectedProperties = null;
            return;
        }

        var header = node switch
        {
            RmlElement element => Localization.GetString("Tools.RmlEditor.Properties.ElementHeader", $"<{element.Tag}>"),
            RmlText => Localization.GetString("Tools.RmlEditor.Properties.TextHeader"),
            RmlComment => Localization.GetString("Tools.RmlEditor.Properties.CommentHeader"),
            _ => string.Empty,
        };
        SelectedProperties = new RmlElementPropertiesViewModel(this, node, header);
        UpdateComputedPanel();
    }

    private void RebuildStyleRules()
    {
        (int SheetIndex, int RuleIndex)? selected = SelectedStyleRule is null ? null : (SelectedStyleRule.SheetIndex, SelectedStyleRule.RuleIndex);
        StyleRules.Clear();
        for (var sheetIndex = 0; sheetIndex < _sheets.Count; sheetIndex++)
        {
            var entry = _sheets[sheetIndex];
            for (var ruleIndex = 0; ruleIndex < entry.Document.Rules.Count; ruleIndex++)
            {
                var rule = entry.Document.Rules[ruleIndex];
                var viewModel = new RmlStyleRuleViewModel(
                    sheetIndex,
                    ruleIndex,
                    string.Join(", ", rule.Selectors),
                    Localization.GetString("Tools.RmlEditor.Styles.RuleDetail", entry.SourceLabel, rule.Declarations.Count),
                    !entry.IsEmbedded && entry.FilePath is not null);
                viewModel.SelectorsCommitted += OnRuleSelectorsCommitted;
                StyleRules.Add(viewModel);
            }
        }

        SelectedStyleRule = selected.HasValue
            ? StyleRules.FirstOrDefault(r => r.SheetIndex == selected.Value.SheetIndex && r.RuleIndex == selected.Value.RuleIndex)
            : null;
        RebuildRuleDeclarations();
    }

    private void RebuildRuleDeclarations()
    {
        RuleDeclarations.Clear();
        var selected = SelectedStyleRule;
        if (selected is null)
        {
            return;
        }

        var entry = GetSheetEntry(selected.SheetIndex);
        if (entry is null || selected.RuleIndex < 0 || selected.RuleIndex >= entry.Document.Rules.Count)
        {
            return;
        }

        var editable = !entry.IsEmbedded && entry.FilePath is not null;
        var rule = entry.Document.Rules[selected.RuleIndex];
        foreach (var declaration in rule.Declarations)
        {
            var label = declaration.Property + (declaration.Important ? " !important" : string.Empty);
            RuleDeclarations.Add(new RmlPropertyRowViewModel(
                label,
                declaration.Value,
                (name, value) => CommitRuleDeclaration(selected.SheetIndex, selected.RuleIndex, name, value),
                editable ? name => RemoveRuleDeclaration(selected.SheetIndex, selected.RuleIndex, name) : null,
                !editable));
        }
    }

    private void UpdateComputedPanel()
    {
        var properties = SelectedProperties;
        if (properties is null || properties.Node is not RmlElement)
        {
            return;
        }

        properties.ComputedStyles.Clear();
        properties.MatchedRules.Clear();
        if (_lastPreview is null)
        {
            return;
        }

        if (!_lastPreview.ComputedStyles.TryGetValue(properties.Node.Id, out var computed))
        {
            return;
        }

        foreach (var pair in computed.Properties.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            properties.ComputedStyles.Add(new RmlPropertyRowViewModel(pair.Key, pair.Value, (_, _) => { }, null, true));
        }

        foreach (var match in computed.MatchedRules)
        {
            var source = string.IsNullOrEmpty(match.Source) ? Localization.GetString("Tools.RmlEditor.Styles.EmbeddedLabel") : Path.GetFileName(match.Source);
            properties.MatchedRules.Add($"{match.Selector}  ({source})");
        }
    }

    private void UpdateTargetSheetLabel()
    {
        var entry = FindRuleTargetSheet();
        TargetSheetLabel = entry is null
            ? Localization.GetString("Tools.RmlEditor.Styles.NoTargetSheet")
            : Localization.GetString("Tools.RmlEditor.Styles.TargetSheet", entry.SourceLabel);
    }

    private void RebuildAll()
    {
        RebuildIndex();
        RebuildTree();
        RebuildStyleRules();
        UpdateTargetSheetLabel();
        RefreshSourceText();
        RequestPreviewRefresh();
        RefreshEditorCommands();
    }

    private void PushUndo(RmlEditAction action)
    {
        _undoStack.Push(action);
        while (_undoStack.Count > RmlConstants.Editor.MaxUndoHistory)
        {
            var kept = _undoStack.Reverse().Skip(1).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var item in kept)
            {
                _undoStack.Push(item);
            }
        }

        _redoStack.Clear();
        _historyVersion++;
        SyncModifiedFromHistory();
    }

    private void SyncModifiedFromHistory()
    {
        IsModified = _historyVersion != _savedHistoryVersion;
    }

    private string? GetDocumentDirectory()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            return null;
        }

        try
        {
            return Path.GetDirectoryName(FilePath);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private bool IsRootNode(RmlNode node)
    {
        return _document is not null && (ReferenceEquals(node, _document.Head) || ReferenceEquals(node, _document.Body));
    }

    private bool IsRootElement(RmlElement element)
    {
        return _document is not null && (ReferenceEquals(element, _document.Head) || ReferenceEquals(element, _document.Body));
    }

    private string NodeDetail(RmlNode node)
    {
        return node switch
        {
            RmlElement element => Localization.GetString("Tools.RmlEditor.Tree.ElementDetail", element.Attributes.Count, element.Children.Count),
            RmlText text => Localization.GetString("Tools.RmlEditor.Tree.TextDetail", text.Text.Trim().Length),
            _ => string.Empty,
        };
    }

    private void ApplyInlineStyle(RmlElement element, List<RcssDeclaration> declarations, string before, string description)
    {
        var nodeId = element.Id;
        var after = rcssDocumentService.WriteInlineStyle(declarations);
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        SetInlineStyleText(element, after);
        PushUndo(new RmlEditAction(
            description,
            () =>
            {
                SetInlineStyleText(element, after);
                RebuildAll();
                SelectNode(nodeId);
            },
            () =>
            {
                SetInlineStyleText(element, before);
                RebuildAll();
                SelectNode(nodeId);
            }));
        RebuildAll();
    }

    private void ApplyRuleDeclarations(RcssRule rule, List<RcssDeclaration> declarations, string before, string description)
    {
        var after = rcssDocumentService.WriteInlineStyle(declarations);
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        (int SheetIndex, int RuleIndex)? selected = SelectedStyleRule is null ? null : (SelectedStyleRule.SheetIndex, SelectedStyleRule.RuleIndex);
        SetRuleDeclarations(rule, after);
        PushUndo(new RmlEditAction(
            description,
            () =>
            {
                SetRuleDeclarations(rule, after);
                RebuildAll();
                RestoreRuleSelection(selected);
            },
            () =>
            {
                SetRuleDeclarations(rule, before);
                RebuildAll();
                RestoreRuleSelection(selected);
            }));
        RebuildAll();
    }

    private void SetInlineStyleText(RmlElement element, string inline)
    {
        if (string.IsNullOrEmpty(inline))
        {
            element.RemoveAttribute(RmlConstants.Attributes.Style);
        }
        else
        {
            element.SetAttribute(RmlConstants.Attributes.Style, inline);
        }
    }

    private void SetRuleDeclarations(RcssRule rule, string inline)
    {
        rule.Declarations.Clear();
        var parsed = rcssDocumentService.ParseInlineStyle(inline);
        if (parsed.Success && parsed.Data is not null)
        {
            rule.Declarations.AddRange(parsed.Data);
        }
    }

    private List<RcssDeclaration>? TryGetInlineDeclarations(RmlElement element)
    {
        var style = element.GetAttribute(RmlConstants.Attributes.Style);
        if (string.IsNullOrWhiteSpace(style))
        {
            return [];
        }

        var parsed = rcssDocumentService.ParseInlineStyle(style);
        return parsed.Success && parsed.Data is not null ? parsed.Data.ToList() : null;
    }

    private StyleSheetEntry? GetSheetEntry(int sheetIndex)
    {
        return sheetIndex >= 0 && sheetIndex < _sheets.Count ? _sheets[sheetIndex] : null;
    }

    private RcssRule? GetEditableRule(int sheetIndex, int ruleIndex)
    {
        var entry = GetSheetEntry(sheetIndex);
        if (entry is null || entry.IsEmbedded || entry.FilePath is null)
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.CannotEditEmbedded"));
            return null;
        }

        if (ruleIndex < 0 || ruleIndex >= entry.Document.Rules.Count)
        {
            return null;
        }

        return entry.Document.Rules[ruleIndex];
    }

    private StyleSheetEntry? FindRuleTargetSheet()
    {
        for (var i = _sheets.Count - 1; i >= 0; i--)
        {
            if (!_sheets[i].IsEmbedded && _sheets[i].FilePath is not null)
            {
                return _sheets[i];
            }
        }

        return null;
    }

    private void SelectRule(int sheetIndex, int ruleIndex)
    {
        SelectedStyleRule = StyleRules.FirstOrDefault(r => r.SheetIndex == sheetIndex && r.RuleIndex == ruleIndex);
    }

    private void RestoreRuleSelection((int SheetIndex, int RuleIndex)? selected)
    {
        if (selected.HasValue)
        {
            SelectRule(selected.Value.SheetIndex, selected.Value.RuleIndex);
        }
    }

    private RmlElement? PasteTarget()
    {
        if (_document is null)
        {
            return null;
        }

        if (SelectedNode?.Node is RmlElement element)
        {
            return element;
        }

        if (SelectedNode is not null)
        {
            return _nodeParents.GetValueOrDefault(SelectedNode.Node.Id) ?? _document.Body;
        }

        return _document.Body;
    }

    private void NotifyInvalid(string message)
    {
        Notifications.ShowWarning(
            Localization.GetString("Tools.RmlEditor.Error.InvalidTitle"),
            message,
            NotificationDurations.Medium);
    }

    private void RefreshSourceText()
    {
        if (_document is null || !IsSourceView)
        {
            return;
        }

        SourceText = rmlDocumentService.WriteDocument(_document);
        SourceError = null;
    }

    private void RequestPreviewRefresh()
    {
        if (_document is null)
        {
            PreviewContent = null;
            return;
        }

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var generation = ++_previewGeneration;
        var token = _previewCts.Token;
        _ = RefreshPreviewAsync(generation, token);
    }

    private async Task RefreshPreviewAsync(int generation, CancellationToken cancellationToken)
    {
        var document = _document;
        if (document is null)
        {
            return;
        }

        try
        {
            var sheets = _sheets.Select(s => s.Document).ToList();
            var references = CollectImageReferences(document, sheets);
            var images = new Dictionary<string, Bitmap?>(StringComparer.OrdinalIgnoreCase);
            var directory = GetDocumentDirectory();
            foreach (var reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                images[reference] = await imageResolver.ResolveAsync(reference, directory, cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (generation != _previewGeneration)
            {
                return;
            }

            RmlPreviewResult? result = null;
            try
            {
                await InvokeOnUIThreadAsync(() =>
            {
                if (generation != _previewGeneration || _document is null)
                {
                    return;
                }

                result = previewBuilder.Build(document, new RmlPreviewOptions
                {
                    StyleSheets = _sheets.Select(s => s.Document).ToList(),
                    Images = images,
                    ShowHidden = ShowHidden,
                    BaseFontSize = 16.0,
                    SurfaceWidth = RmlConstants.Editor.DefaultPreviewWidth,
                    SelectedId = SelectedNode?.Node.Id,
                    ElementPressed = OnPreviewElementPressed,
                });
            }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Preview build failed and was skipped");
                return;
            }

            if (result is null || generation != _previewGeneration)
            {
                return;
            }

            await InvokeOnUIThreadAsync(() =>
            {
                if (generation != _previewGeneration)
                {
                    return;
                }

                _lastPreview = result;
                PreviewContent = result.Root;
                UpdateAssetStatus(references.Count, result.MissingImages.Count);
                UpdateComputedPanel();
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A newer refresh superseded this one.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Preview refresh failed while resolving images");
        }
    }

    private void UpdateAssetStatus(int total, int missing)
    {
        HasMissingImages = missing > 0;
        AssetStatusText = total == 0
            ? Localization.GetString("Tools.RmlEditor.Assets.EmptyStatus")
            : Localization.GetString("Tools.RmlEditor.Assets.ResolvedStatus", total - missing, total);
    }

    private void OnPreviewElementPressed(Guid nodeId)
    {
        if (IsPanMode)
        {
            return;
        }

        SelectNode(nodeId);
        LeftSidebarTabIndex = 0;
    }

    private void OnRuleSelectorsCommitted(RmlStyleRuleViewModel rule)
    {
        CommitRuleSelectors(rule.SheetIndex, rule.RuleIndex, rule.Selectors);
    }

    private async Task<bool> WriteDocumentToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_document is null)
        {
            return false;
        }

        try
        {
            var text = rmlDocumentService.WriteDocument(_document);
            await AtomicFile.WriteAllTextAsync(filePath, text, cancellationToken).ConfigureAwait(false);
            var sheetsSaved = await WriteStyleSheetsAsync(cancellationToken).ConfigureAwait(false);
            _savedHistoryVersion = _historyVersion;
            IsModified = false;
            telemetryService?.TrackEvent(TelemetryConstants.Events.RmlDocumentSaved, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.FilePath] = TelemetryConstants.RmlEditor.AnonymousDocumentName,
                [TelemetryConstants.Properties.ElementCount] = _document.AllElements().Count(),
                [TelemetryConstants.Properties.StyleRuleCount] = _sheets.Sum(s => s.Document.Rules.Count),
            });
            var saveMessage = sheetsSaved == 0 ? Localization.GetString("Tools.RmlEditor.Save.SuccessMessage", Path.GetFileName(filePath)) : Localization.GetString("Tools.RmlEditor.Save.SuccessMessageSheets", Path.GetFileName(filePath), sheetsSaved);
            Notifications.ShowSuccess(
                Localization.GetString("Tools.RmlEditor.Save.SuccessTitle"),
                saveMessage,
                NotificationDurations.Medium);
            logger.LogInformation("Saved interface file {Path} with {Sheets} style sheets", filePath, sheetsSaved);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to save interface file {Path}", filePath);
            Notifications.ShowError(
                Localization.GetString("Tools.RmlEditor.Save.FailureTitle"),
                Localization.GetString("Tools.RmlEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied saving interface file {Path}", filePath);
            Notifications.ShowError(
                Localization.GetString("Tools.RmlEditor.Save.FailureTitle"),
                Localization.GetString("Tools.RmlEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
            return false;
        }
    }

    private async Task<int> WriteStyleSheetsAsync(CancellationToken cancellationToken)
    {
        var saved = 0;
        foreach (var entry in _sheets)
        {
            if (entry.IsEmbedded || entry.FilePath is null)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var text = rcssDocumentService.WriteDocument(entry.Document);
            await AtomicFile.WriteAllTextAsync(entry.FilePath, text, cancellationToken).ConfigureAwait(false);
            saved++;
        }

        return saved;
    }

    private async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.GetString("Tools.RmlEditor.FileDialog.FolderTitle"),
            AllowMultiple = false,
        });
        if (folders.Count == 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return folders[0].TryGetLocalPath();
    }

    private async Task<bool> AdoptExplorerDirectoryAsync(string folder, CancellationToken cancellationToken)
    {
        LeftSidebarTabIndex = 1;
        if (HasDocument && !string.IsNullOrEmpty(FilePath) && IsSubPathOf(FilePath, folder))
        {
            FileExplorer.CurrentPath = FilePath;
            return true;
        }

        var firstRml = FindFirstRmlFile(FileExplorer.Nodes);
        if (!string.IsNullOrEmpty(firstRml))
        {
            return await OpenFileAsync(firstRml, cancellationToken).ConfigureAwait(false);
        }

        Notifications.ShowInfo(
            Localization.GetString("Tools.RmlEditor.Files.NoRmlFilesTitle"),
            Localization.GetString("Tools.RmlEditor.Files.NoRmlFilesMessage"),
            NotificationDurations.Medium);
        return true;
    }

    private string? FindFirstRmlFile(IEnumerable<EditorFileTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile && node.FullPath.EndsWith(RmlConstants.File.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return node.FullPath;
            }

            var child = FindFirstRmlFile(node.Children);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private void OnExplorerFileActivated(object? sender, EditorFileTreeNodeViewModel node)
    {
        OpenExplorerFileCommand.Execute(node);
    }

    private void SyncFilesDirectory(string? filePath)
    {
        if (string.IsNullOrEmpty(FileExplorer.Directory) && !string.IsNullOrEmpty(filePath))
        {
            try
            {
                FileExplorer.Directory = Path.GetDirectoryName(filePath);
            }
            catch (ArgumentException)
            {
                // Keep the explorer unbound when the path is not usable.
            }
        }
    }

    private bool IsExternalReference(string reference)
    {
        return reference.StartsWith(RmlConstants.ExternalSchemes.HttpPrefix, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(RmlConstants.ExternalSchemes.HttpsPrefix, StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith(RmlConstants.ExternalSchemes.DataPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private void CollectExpanded(IEnumerable<RmlTreeNodeViewModel> nodes, HashSet<Guid> expanded)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded)
            {
                expanded.Add(node.Node.Id);
            }

            CollectExpanded(node.Children, expanded);
        }
    }

    /// <summary>
    /// Validates the open document and its style sheets, reporting the outcome.
    /// </summary>
    [RelayCommand]
    private void ValidateDocument()
    {
        if (_document is null)
        {
            return;
        }

        var target = FilePath ?? Localization.GetString("Tools.RmlEditor.Document.Untitled");
        var result = rmlDocumentService.ValidateDocument(_document, target);
        var errors = result.CriticalIssueCount;
        var warnings = result.WarningIssueCount;
        foreach (var entry in _sheets)
        {
            if (entry.IsEmbedded)
            {
                continue;
            }

            var sheetResult = rcssDocumentService.ValidateDocument(entry.Document, entry.FilePath ?? entry.SourceLabel);
            errors += sheetResult.CriticalIssueCount;
            warnings += sheetResult.WarningIssueCount;
        }

        telemetryService?.TrackEvent(TelemetryConstants.Events.RmlDocumentValidated, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.IsValid] = errors == 0,
            [TelemetryConstants.Properties.ElementCount] = _document.AllElements().Count(),
        });
        if (errors == 0)
        {
            var validateMessage = warnings == 0 ? Localization.GetString("Tools.RmlEditor.Validate.SuccessMessage") : Localization.GetString("Tools.RmlEditor.Validate.WarningsMessage", warnings);
            Notifications.ShowSuccess(
                Localization.GetString("Tools.RmlEditor.Validate.SuccessTitle"),
                validateMessage,
                NotificationDurations.Medium);
        }
        else
        {
            var first = result.Issues.Count > 0 ? result.Issues[0].Message : string.Empty;
            Notifications.ShowWarning(
                Localization.GetString("Tools.RmlEditor.Validate.IssuesTitle"),
                Localization.GetString("Tools.RmlEditor.Validate.IssuesMessage", errors, warnings, first),
                NotificationDurations.Long);
        }
    }

    /// <summary>
    /// Adds a child element to the selected element, or to the body when nothing is selected.
    /// </summary>
    /// <param name="tag">The tag name, defaulting to a division.</param>
    [RelayCommand]
    private void AddChildElement(string? tag)
    {
        if (_document is null)
        {
            return;
        }

        tag = string.IsNullOrWhiteSpace(tag) ? RmlConstants.Elements.Div : tag.Trim().ToLowerInvariant();
        if (!IsValidTagName(tag))
        {
            NotifyInvalid(Localization.GetString("Tools.RmlEditor.Error.InvalidTag", tag));
            return;
        }

        var parent = PasteTarget();
        if (parent is null)
        {
            return;
        }

        var element = CreateNewElement(tag);
        parent.Children.Add(element);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.AddElement", $"<{tag}>"),
            () =>
            {
                parent.Children.Add(element);
                RebuildAll();
                SelectNode(element.Id);
            },
            () =>
            {
                parent.Children.Remove(element);
                RebuildAll();
                SelectNode(parent.Id);
            }));
        RebuildAll();
        SelectNode(element.Id);
        RefreshEditorCommands();
    }

    /// <summary>
    /// Moves the selected node earlier among its siblings.
    /// </summary>
    [RelayCommand]
    private void MoveNodeUp()
    {
        MoveSelectedNode(-1);
    }

    /// <summary>
    /// Moves the selected node later among its siblings.
    /// </summary>
    [RelayCommand]
    private void MoveNodeDown()
    {
        MoveSelectedNode(1);
    }

    /// <summary>
    /// Applies the edited source text to the document.
    /// </summary>
    [RelayCommand]
    private void ApplySource()
    {
        if (_document is null)
        {
            return;
        }

        var parsed = rmlDocumentService.ParseText(SourceText, FilePath);
        if (!parsed.Success || parsed.Data is null)
        {
            SourceError = parsed.FirstError ?? string.Empty;
            Notifications.ShowError(
                Localization.GetString("Tools.RmlEditor.Source.FailureTitle"),
                Localization.GetString("Tools.RmlEditor.Source.FailureMessage", SourceError),
                NotificationDurations.Long);
            return;
        }

        var before = rmlDocumentService.WriteDocument(_document);
        var after = rmlDocumentService.WriteDocument(parsed.Data);
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            SourceError = null;
            return;
        }

        AdoptParsedSource(parsed.Data);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.ApplySource", string.Empty),
            () => AdoptSourceText(after),
            () => AdoptSourceText(before)));
        SourceError = null;
        Notifications.ShowSuccess(
            Localization.GetString("Tools.RmlEditor.Source.SuccessTitle"),
            Localization.GetString("Tools.RmlEditor.Source.SuccessMessage"),
            NotificationDurations.Medium);
        RefreshEditorCommands();
    }

    /// <summary>
    /// Links an asset folder for image resolution.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task LinkAssetsFolderAsync(CancellationToken cancellationToken)
    {
        var folder = await PickFolderAsync(cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        LinkedAssetsFolder = folder;
        imageResolver.SetSearchFolders([folder]);
        RequestPreviewRefresh();
        Notifications.ShowSuccess(
            Localization.GetString("Tools.RmlEditor.Assets.LinkSuccessTitle"),
            Localization.GetString("Tools.RmlEditor.Assets.LinkSuccessMessage", folder),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Clears the linked asset folder.
    /// </summary>
    [RelayCommand]
    private void ClearLinkedAssets()
    {
        LinkedAssetsFolder = null;
        imageResolver.SetSearchFolders([]);
        RequestPreviewRefresh();
    }

    /// <summary>
    /// Opens a file activated in the explorer.
    /// </summary>
    /// <param name="node">The activated file node.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task OpenExplorerFileAsync(EditorFileTreeNodeViewModel? node, CancellationToken cancellationToken = default)
    {
        if (node is null || node.IsDirectory)
        {
            return;
        }

        if (node.FullPath.EndsWith(RmlConstants.File.Extension, StringComparison.OrdinalIgnoreCase))
        {
            await OpenFileAsync(node.FullPath, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (node.FullPath.EndsWith(RmlConstants.File.RcssExtension, StringComparison.OrdinalIgnoreCase))
        {
            OpenStyleSheetFile(node.FullPath);
        }
    }

    /// <summary>
    /// Adds the staged style rule to the target sheet.
    /// </summary>
    [RelayCommand]
    private void AddStyleRule()
    {
        AddRule(NewRuleSelectors);
    }

    /// <summary>
    /// Deletes the selected style rule.
    /// </summary>
    [RelayCommand]
    private void DeleteStyleRule()
    {
        DeleteSelectedRule();
    }

    /// <summary>
    /// Adds the staged declaration to the selected style rule.
    /// </summary>
    [RelayCommand]
    private void AddStyleDeclaration()
    {
        if (SelectedStyleRule is null)
        {
            return;
        }

        AddRuleDeclaration(SelectedStyleRule.SheetIndex, SelectedStyleRule.RuleIndex, NewRuleProperty, NewRuleValue);
        NewRuleProperty = string.Empty;
        NewRuleValue = string.Empty;
    }

    /// <summary>
    /// Expands every element tree node.
    /// </summary>
    [RelayCommand]
    private void ExpandAllNodes()
    {
        SetNodesExpanded(RootNodes, true);
    }

    /// <summary>
    /// Collapses every element tree node.
    /// </summary>
    [RelayCommand]
    private void CollapseAllNodes()
    {
        SetNodesExpanded(RootNodes, false);
    }

    partial void OnSelectedNodeChanged(RmlTreeNodeViewModel? value)
    {
        RebuildProperties();
        if (!_suppressSelectionRefresh)
        {
            RequestPreviewRefresh();
        }

        RefreshEditorCommands();
    }

    partial void OnElementsFilterChanged(string value)
    {
        RebuildTree();
    }

    partial void OnShowHiddenChanged(bool value)
    {
        RequestPreviewRefresh();
    }

    partial void OnSelectedStyleRuleChanged(RmlStyleRuleViewModel? value)
    {
        RebuildRuleDeclarations();
    }

    partial void OnIsSourceViewChanged(bool value)
    {
        if (value)
        {
            RefreshSourceText();
        }
    }

    private void MoveSelectedNode(int direction)
    {
        if (_document is null || SelectedNode is null || IsRootNode(SelectedNode.Node))
        {
            return;
        }

        var node = SelectedNode.Node;
        var parent = _nodeParents.GetValueOrDefault(node.Id);
        var siblings = parent?.Children;
        if (siblings is null)
        {
            return;
        }

        var index = siblings.IndexOf(node);
        var swapped = index + direction;
        if (swapped < 0 || swapped >= siblings.Count)
        {
            return;
        }

        (siblings[index], siblings[swapped]) = (siblings[swapped], siblings[index]);
        PushUndo(new RmlEditAction(
            Localization.GetString("Tools.RmlEditor.History.MoveNode", DescribeNode(node)),
            () =>
            {
                var at = siblings.IndexOf(node);
                var other = at + direction;
                if (other >= 0 && other < siblings.Count)
                {
                    (siblings[at], siblings[other]) = (siblings[other], siblings[at]);
                }

                RebuildAll();
                SelectNode(node.Id);
            },
            () =>
            {
                var at = siblings.IndexOf(node);
                var other = at - direction;
                if (other >= 0 && other < siblings.Count)
                {
                    (siblings[at], siblings[other]) = (siblings[other], siblings[at]);
                }

                RebuildAll();
                SelectNode(node.Id);
            }));
        RebuildAll();
        SelectNode(node.Id);
    }

    private RmlElement CreateNewElement(string tag)
    {
        // Placeholder texts below are intentional sample element content, not UI chrome.
        var element = new RmlElement { Tag = tag };
        if (string.Equals(tag, RmlConstants.Elements.Image, StringComparison.OrdinalIgnoreCase))
        {
            element.SetAttribute(RmlConstants.Attributes.Src, string.Empty);
            element.SetAttribute(RmlConstants.Attributes.Alt, string.Empty);
        }
        else if (string.Equals(tag, RmlConstants.Elements.Input, StringComparison.OrdinalIgnoreCase))
        {
            element.SetAttribute(RmlConstants.Attributes.Type, RmlConstants.InputTypes.Text);
        }
        else if (string.Equals(tag, RmlConstants.Elements.Anchor, StringComparison.OrdinalIgnoreCase))
        {
            element.SetAttribute(RmlConstants.Attributes.Href, "#");
            element.Children.Add(new RmlText { Text = "Link" });
        }
        else if (string.Equals(tag, RmlConstants.Elements.Button, StringComparison.OrdinalIgnoreCase))
        {
            element.Children.Add(new RmlText { Text = "Button" });
        }
        else if (string.Equals(tag, RmlConstants.Elements.Select, StringComparison.OrdinalIgnoreCase))
        {
            var option = new RmlElement { Tag = RmlConstants.Elements.Option };
            option.Children.Add(new RmlText { Text = "Option" });
            element.Children.Add(option);
        }
        else if (string.Equals(tag, RmlConstants.Elements.UnorderedList, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.OrderedList, StringComparison.OrdinalIgnoreCase))
        {
            var item = new RmlElement { Tag = RmlConstants.Elements.ListItem };
            item.Children.Add(new RmlText { Text = "Item" });
            element.Children.Add(item);
        }
        else if (!IsVoidTag(tag))
        {
            element.Children.Add(new RmlText { Text = "Text" });
        }

        return element;
    }

    private bool IsVoidTag(string tag)
    {
        return RmlConstants.Elements.Void.Any(v => string.Equals(v, tag, StringComparison.OrdinalIgnoreCase));
    }

    private void AdoptParsedSource(RmlDocument parsed)
    {
        parsed.SourcePath = FilePath;
        var selectedId = SelectedNode?.Node.Id;
        _document = parsed;
        LoadStyleSheets();
        RebuildIndex();
        RebuildTree();
        RebuildStyleRules();
        RebuildProperties();
        if (selectedId.HasValue)
        {
            SelectNode(selectedId.Value);
        }

        RefreshSourceText();
        RequestPreviewRefresh();
        RefreshEditorCommands();
    }

    private void AdoptSourceText(string text)
    {
        var parsed = rmlDocumentService.ParseText(text, FilePath);
        if (parsed.Success && parsed.Data is not null)
        {
            AdoptParsedSource(parsed.Data);
        }
    }

    private void OpenStyleSheetFile(string fullPath)
    {
        var index = _sheets.FindIndex(s => string.Equals(s.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.RmlEditor.Files.StylesheetHintTitle"),
                Localization.GetString("Tools.RmlEditor.Files.StylesheetHintMessage", Path.GetFileName(fullPath)),
                NotificationDurations.Medium);
            return;
        }

        LeftSidebarTabIndex = 2;
        var rule = StyleRules.FirstOrDefault(r => r.SheetIndex == index);
        if (rule is not null)
        {
            SelectedStyleRule = rule;
        }
    }

    private void SetNodesExpanded(IEnumerable<RmlTreeNodeViewModel> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = expanded;
            SetNodesExpanded(node.Children, expanded);
        }
    }
}
