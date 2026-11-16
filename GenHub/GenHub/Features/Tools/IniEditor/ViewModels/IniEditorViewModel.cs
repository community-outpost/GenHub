using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.Editors;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Messages;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// ViewModel for the INI editor tool. Edits Generals and Zero Hour INI data files with undo support.
/// </summary>
[method: SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for INI editor tool orchestrator.")]
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for INI editor tool orchestrator.")]
public sealed partial class IniEditorViewModel(
    IIniDocumentService iniDocumentService,
    IIniSchemaService schemaService,
    IIniReferenceService referenceService,
    ISageMappedImageParser mappedImageParser,
    IWndImageAssetService imageAssetService,
    IGameInstallationService installationService,
    INotificationService notificationService,
    ILocalizationService localizationService,
    IDialogService dialogService,
    ILogger<IniEditorViewModel> logger)
    : EditorToolViewModelBase(notificationService, localizationService, dialogService)
{
    private static readonly (string Key, string Value)[] UpgradeHookupTemplate =
    [
        (IniConstants.FieldKeys.Upgrade, "Upgrade_"),
        (IniConstants.FieldKeys.TriggeredBy, "Upgrade_"),
    ];

    private static readonly (string Key, string Value)[] DamageProfileTemplate =
    [
        (IniConstants.FieldKeys.DamageType, "EXPLOSION"),
        (IniConstants.FieldKeys.PrimaryDamage, "50.0"),
        (IniConstants.FieldKeys.PrimaryDamageRadius, "20.0"),
        (IniConstants.FieldKeys.DeathType, "EXPLODED"),
    ];

    private readonly Stack<IniEditAction> _undoStack = new();
    private readonly Stack<IniEditAction> _redoStack = new();
    private readonly Dictionary<string, Bitmap?> _textureThumbnails = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GameInstallation> _installations = [];
    private FileExplorerViewModel? _fileExplorer;
    private IniDocument? _document;
    private bool _cultureSubscribed;
    private bool _installationsLoaded;
    private int _thumbnailGeneration;
    private IniEditAction? _savedTopAction;
    private bool _isRebuilding;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _thumbnailCts;

    /// <summary>
    /// Gets the root block nodes of the edited document.
    /// </summary>
    public ObservableCollection<IniTreeNodeViewModel> RootNodes { get; } = [];

    /// <summary>
    /// Gets the field rows of the selected block.
    /// </summary>
    public ObservableCollection<IniFieldRowViewModel> FieldRows { get; } = [];

    /// <summary>
    /// Gets the editable rows for file-scope settings written outside of any block.
    /// </summary>
    public ObservableCollection<IniFieldRowViewModel> GlobalFieldRows { get; } = [];

    /// <summary>
    /// Gets the canvas summary lines for the selected block.
    /// </summary>
    public ObservableCollection<IniCanvasSummaryRow> CanvasSummary { get; } = [];

    /// <summary>
    /// Gets the assembled attachment rows for the selected block.
    /// </summary>
    public ObservableCollection<IniAssembledRowViewModel> AssembledRows { get; } = [];

    /// <summary>
    /// Gets the filtered reference index results.
    /// </summary>
    public ObservableCollection<IniReferenceEntry> ReferenceResults { get; } = [];

    /// <summary>
    /// Gets the mapped image definitions available for texture pickers.
    /// </summary>
    public ObservableCollection<MappedImageDefinition> TexturePickerItems { get; } = [];

    /// <summary>
    /// Gets the game installations available as asset sources.
    /// </summary>
    public ObservableCollection<GameInstallationOption> AvailableInstallations { get; } = [];

    /// <summary>
    /// Gets or sets the selected block node.
    /// </summary>
        /// <summary>
    /// Gets or sets the active tab index of the left sidebar.
    /// 0 = Blocks, 1 = Files, 2 = Reference.
    /// </summary>
    [ObservableProperty]
    private int _leftSidebarTabIndex;

    [ObservableProperty]
    private IniTreeNodeViewModel? _selectedNode;

    /// <summary>
    /// Gets or sets the block filter text.
    /// </summary>
    [ObservableProperty]
    private string? _blockFilter;

    /// <summary>
    /// Gets or sets the current file path.
    /// </summary>
    [ObservableProperty]
    private string? _filePath;

    /// <summary>
    /// Gets or sets the raw text preview of the document.
    /// </summary>
    [ObservableProperty]
    private string _rawText = string.Empty;

    /// <summary>
    /// Gets or sets the new field key input.
    /// </summary>
    [ObservableProperty]
    private string _newFieldKey = string.Empty;

    /// <summary>
    /// Gets or sets the new field value input.
    /// </summary>
    [ObservableProperty]
    private string _newFieldValue = string.Empty;

    /// <summary>
    /// Gets or sets the new file-setting key input.
    /// </summary>
    [ObservableProperty]
    private string _newGlobalFieldKey = string.Empty;

    /// <summary>
    /// Gets or sets the new file-setting value input.
    /// </summary>
    [ObservableProperty]
    private string _newGlobalFieldValue = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the open file declares file-scope settings.
    /// </summary>
    [ObservableProperty]
    private bool _hasGlobalFields;

    /// <summary>
    /// Gets or sets the new block type input.
    /// </summary>
    [ObservableProperty]
    private string _newBlockType = IniConstants.BlockTypes.Object;

    /// <summary>
    /// Gets or sets the new block name input.
    /// </summary>
    [ObservableProperty]
    private string _newBlockName = string.Empty;

    /// <summary>
    /// Gets or sets the reference search text.
    /// </summary>
    [ObservableProperty]
    private string? _referenceFilter;

    /// <summary>
    /// Gets or sets the reference type filter, or null for all types.
    /// </summary>
    [ObservableProperty]
    private string? _referenceTypeFilter;

    /// <summary>
    /// Gets or sets the selected reference entry.
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InsertReferenceCommand))]
    private IniReferenceEntry? _selectedReference;

    /// <summary>
    /// Gets or sets the installation used for texture resolution.
    /// </summary>
    [ObservableProperty]
    private GameInstallationOption? _selectedInstallation;

    /// <summary>
    /// Gets the document title with a modification marker.
    /// </summary>
    public override string DocumentTitle
    {
        get
        {
            var name = FilePath == null
                ? Localization.GetString("Tools.IniEditor.Document.Untitled")
                : Path.GetFileName(FilePath);
            return IsDirty ? $"*{name}" : name;
        }
    }

    /// <summary>
    /// Gets the canvas content font size scaled by the shared zoom factor.
    /// </summary>
    public double CanvasFontSize => 12 * Zoom;

    /// <inheritdoc />
    public override bool CanSave => HasDocument;

    /// <inheritdoc />
    public override bool CanSaveAs => HasDocument;

    /// <inheritdoc />
    public override bool CanUndo => _undoStack.Count > 0;

    /// <inheritdoc />
    public override bool CanRedo => _redoStack.Count > 0;

    /// <inheritdoc />
    public override bool CanCopy => HasDocument && SelectedNode != null;

    /// <inheritdoc />
    public override bool CanCut => HasDocument && SelectedNode != null;

    /// <inheritdoc />
    public override bool CanPaste => HasDocument;

    /// <inheritdoc />
    public override bool CanDuplicate => HasDocument && SelectedNode != null;

    /// <inheritdoc />
    public override bool CanDelete => HasDocument && SelectedNode != null;

    /// <summary>
    /// Gets the shared file explorer listing INI files.
    /// </summary>
    public FileExplorerViewModel FileExplorer
    {
        get
        {
            if (_fileExplorer != null)
            {
                return _fileExplorer;
            }

            var explorer = new FileExplorerViewModel(logger)
            {
                FilePatterns = [ModBuilderConstants.FileNames.IniSearchPattern],
                ShowFileExtensions = true,
                ExcludedDirectoryNames =
                [
                    ModBuilderConstants.DefaultBuildDir,
                    ModBuilderConstants.DefaultReleaseDir,
                ],
                BrowseFolderAsync = BrowseExplorerFolderAsync,
                DirectoryAdoptedAsync = OnExplorerDirectoryAdoptedAsync,
            };
            explorer.FileActivated += OnExplorerFileActivated;
            _fileExplorer = explorer;
            return explorer;
        }
    }

    /// <summary>
    /// Gets the available block types for the add-block input.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public IReadOnlyList<string> AvailableBlockTypes => IniConstants.BlockTypes.All;

    /// <summary>
    /// Gets the block types offered by the reference type filter.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public IReadOnlyList<string> ReferenceTypeOptions => IniConstants.BlockTypes.All;

    /// <summary>
    /// Gets the thumbnail provider for texture picker controls.
    /// </summary>
    public Func<MappedImageDefinition, IImage?> PickerThumbnailProvider => definition =>
        _textureThumbnails.TryGetValue(definition.Name, out var thumbnail) ? thumbnail : null;

    /// <summary>
    /// Opens a mapped image in the Texture Editor tool.
    /// </summary>
    /// <param name="definition">The mapped image definition.</param>
    public static void OpenTextureInEditor(MappedImageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        WeakReferenceMessenger.Default.Send(new OpenFileInToolMessage(
            TextureEditorConstants.ToolId,
            definition.SourcePath ?? definition.Name));
    }

    /// <summary>
    /// Opens an INI file, asking to discard unsaved changes first.
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

        filePath = NormalizePath(filePath);
        var result = await iniDocumentService.ParseFileAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Open.FailureTitle"),
                Localization.GetString("Tools.IniEditor.Open.FailureMessage", result.FirstError ?? filePath),
                NotificationDurations.Long);
            return false;
        }

        await AdoptDocumentAsync(result.Data, filePath, cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Opened INI file {Path}", filePath);
        return true;
    }

    /// <summary>
    /// Opens a folder in the file explorer and optionally loads the first INI file.
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

        var gameFilesEdited = Path.Combine(folderPath, ModBuilderConstants.GameFilesEditedDir);
        if (Directory.Exists(gameFilesEdited))
        {
            folderPath = gameFilesEdited;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            FileExplorer.Directory = folderPath;
            LeftSidebarTabIndex = 1;
        }).ConfigureAwait(false);

        if (HasDocument && !string.IsNullOrEmpty(FilePath) && IsSubPathOf(FilePath, folderPath))
        {
            FileExplorer.CurrentPath = FilePath;
            return true;
        }

        var first = FileExplorer.FindFirstFile();
        if (!string.IsNullOrEmpty(first))
        {
            var opened = await OpenFileAsync(first, cancellationToken).ConfigureAwait(false);
            if (opened)
            {
                await InvokeOnUIThreadAsync(() => LeftSidebarTabIndex = 1).ConfigureAwait(false);
            }

            return opened;
        }

        Notifications.ShowInfo(
            Localization.GetString("Tools.IniEditor.Files.NoIniFilesTitle"),
            Localization.GetString("Tools.IniEditor.Files.NoIniFilesMessage"),
            NotificationDurations.Medium);
        return true;
    }

    /// <inheritdoc />
    protected override async Task OnNewDocumentAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await AdoptDocumentAsync(new IniDocument(), null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override async Task OnOpenFolderAsync(CancellationToken cancellationToken)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.GetString("Tools.IniEditor.FileDialog.FolderTitle"),
            AllowMultiple = false,
        }).ConfigureAwait(true);
        if (folders.Count == 0)
        {
            return;
        }

        var localPath = folders[0].TryGetLocalPath();
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
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localization.GetString("Tools.IniEditor.FileDialog.OpenTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localization.GetString("Tools.IniEditor.FileDialog.FilterName"))
                {
                    Patterns = [ModBuilderConstants.FileNames.IniSearchPattern],
                },
            ],
        }).ConfigureAwait(true);
        if (files.Count > 0)
        {
            var localPath = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                await OpenFileAsync(localPath, cancellationToken).ConfigureAwait(true);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnSaveAsync(CancellationToken cancellationToken)
    {
        if (_document == null)
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
        if (_document == null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localization.GetString("Tools.IniEditor.FileDialog.SaveTitle"),
            SuggestedFileName = string.IsNullOrEmpty(FilePath) ? "Untitled.ini" : Path.GetFileName(FilePath),
        }).ConfigureAwait(true);
        if (file == null)
        {
            return;
        }

        var localPath = file.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            await WriteDocumentToFileAsync(localPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override void OnUndo()
    {
        if (_undoStack.Count == 0)
        {
            return;
        }

        var action = _undoStack.Pop();
        action.Undo();
        _redoStack.Push(action);
        SyncDirtyAfterHistory();
        RebuildAll();
    }

    /// <inheritdoc />
    protected override void OnRedo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        var action = _redoStack.Pop();
        action.Redo();
        _undoStack.Push(action);
        SyncDirtyAfterHistory();
        RebuildAll();
    }

    /// <inheritdoc />
    protected override void OnCopy()
    {
        if (IsTextInputFocused() || SelectedNode == null)
        {
            return;
        }

        var text = SerializeBlocks([SelectedNode.Block]);
        _ = CopyTextToClipboardAsync(text);
    }

    /// <inheritdoc />
    protected override void OnCut()
    {
        if (IsTextInputFocused() || _document == null || SelectedNode == null)
        {
            return;
        }

        var text = SerializeBlocks([SelectedNode.Block]);
        _ = CopyTextToClipboardAsync(text);
        DeleteBlock(SelectedNode);
    }

    /// <inheritdoc />
    protected override async Task OnPasteAsync(CancellationToken cancellationToken)
    {
        if (IsTextInputFocused() || _document == null)
        {
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel?.Clipboard == null)
        {
            return;
        }

        var text = await topLevel.Clipboard.GetTextAsync().ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var parsed = iniDocumentService.ParseText(text);
        if (!parsed.Success || parsed.Data == null || parsed.Data.Blocks.Count == 0)
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.Paste.NoBlocksTitle"),
                Localization.GetString("Tools.IniEditor.Paste.NoBlocksMessage"),
                NotificationDurations.Medium);
            return;
        }

        var added = parsed.Data.Blocks;
        foreach (var block in added)
        {
            _document.Blocks.Add(block);
        }

        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.PasteBlocks"),
            () =>
            {
                foreach (var block in added)
                {
                    _document.Blocks.Add(block);
                }

                RebuildAll();
            },
            () =>
            {
                foreach (var block in added)
                {
                    _document.Blocks.Remove(block);
                }

                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
        SelectBlock(added[0]);
    }

    /// <inheritdoc />
    protected override void OnDuplicate()
    {
        if (IsTextInputFocused() || _document == null || SelectedNode == null)
        {
            return;
        }

        var parsed = iniDocumentService.ParseText(SerializeBlocks([SelectedNode.Block]));
        if (!parsed.Success || parsed.Data == null || parsed.Data.Blocks.Count == 0)
        {
            return;
        }

        var siblings = SelectedNode.Parent == null ? _document.Blocks : SelectedNode.Parent.Block.Children;
        var index = siblings.IndexOf(SelectedNode.Block);
        var copy = parsed.Data.Blocks[0];
        if (copy.AssignmentValue == null && copy.Name.Length > 0)
        {
            copy.Name += " Copy";
        }

        siblings.Insert(Math.Min(index + 1, siblings.Count), copy);
        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.DuplicateBlock"),
            () =>
            {
                siblings.Insert(Math.Min(index + 1, siblings.Count), copy);
                RebuildAll();
            },
            () =>
            {
                siblings.Remove(copy);
                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
        SelectBlock(copy);
    }

    /// <inheritdoc />
    protected override void OnDelete()
    {
        if (IsTextInputFocused() || SelectedNode == null)
        {
            return;
        }

        DeleteBlock(SelectedNode);
    }

    /// <inheritdoc />
    protected override void OnZoomChanged()
    {
        OnPropertyChanged(nameof(CanvasFontSize));
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_cultureSubscribed)
            {
                Localization.PropertyChanged -= OnLocalizationPropertyChanged;
                _cultureSubscribed = false;
            }

            CancelDeferred(ref _previewCts);
            CancelDeferred(ref _filterCts);
            CancelDeferred(ref _thumbnailCts);
            if (_fileExplorer != null)
            {
                _fileExplorer.FileActivated -= OnExplorerFileActivated;
            }
        }

        base.Dispose(disposing);
    }

    private static string? ResolveExplorerDirectory(string? directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        var current = directory;
        while (!string.IsNullOrEmpty(current))
        {
            if (string.Equals(Path.GetFileName(current), ModBuilderConstants.GameFilesEditedDir, PathHelper.PathComparison))
            {
                return current;
            }

            current = Path.GetDirectoryName(current);
        }

        return directory;
    }

    private static bool IsSubPathOf(string path, string basePath)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedPath.StartsWith(normalizedBase + Path.DirectorySeparatorChar, comparison)
                || string.Equals(normalizedPath, normalizedBase, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException)
        {
            return false;
        }
    }

    private static void SetAllExpanded(IEnumerable<IniTreeNodeViewModel> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = expanded;
            SetAllExpanded(node.Children, expanded);
        }
    }

    private static string NormalizePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return filePath;
        }

        try
        {
            return Path.GetFullPath(filePath.Trim().Trim('"'));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return filePath;
        }
    }

    private static bool IsPathUnder(string filePath, string directory)
    {
        try
        {
            var fullFile = Path.GetFullPath(filePath);
            var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return fullFile.StartsWith(fullDirectory + Path.DirectorySeparatorChar, PathHelper.PathComparison);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool MatchesFilter(IniBlock block, string? filter)
    {
        if (string.IsNullOrEmpty(filter))
        {
            return true;
        }

        if (block.DisplayHeader.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return block.Children.Any(child => MatchesFilter(child, filter));
    }

    private static IniTreeNodeViewModel BuildTreeNode(IniBlock block, IniTreeNodeViewModel? parent)
    {
        var node = new IniTreeNodeViewModel(block, parent);
        foreach (var child in block.Children)
        {
            node.Children.Add(BuildTreeNode(child, node));
        }

        return node;
    }

    private static IniTreeNodeViewModel? FindNode(IEnumerable<IniTreeNodeViewModel> nodes, IniBlock block)
    {
        foreach (var node in nodes)
        {
            if (ReferenceEquals(node.Block, block))
            {
                return node;
            }

            var child = FindNode(node.Children, block);
            if (child != null)
            {
                return child;
            }
        }

        return null;
    }

    private static IReadOnlyList<string> CanvasHighlightKeys(string blockType)
    {
        if (string.Equals(blockType, IniConstants.BlockTypes.Object, StringComparison.OrdinalIgnoreCase))
        {
            return [IniConstants.FieldKeys.Health, IniConstants.FieldKeys.BuildCost, IniConstants.FieldKeys.BuildTime, IniConstants.FieldKeys.Side, IniConstants.FieldKeys.DisplayName, IniConstants.BlockTypes.ArmorSet, IniConstants.BlockTypes.WeaponSet, IniConstants.BlockTypes.CommandSet, IniConstants.FieldKeys.Icon, IniConstants.FieldKeys.ButtonImage];
        }

        if (string.Equals(blockType, IniConstants.BlockTypes.Weapon, StringComparison.OrdinalIgnoreCase))
        {
            return [IniConstants.FieldKeys.PrimaryDamage, IniConstants.FieldKeys.PrimaryDamageRadius, IniConstants.FieldKeys.AttackRange, IniConstants.FieldKeys.DamageType, IniConstants.FieldKeys.DeathType, IniConstants.FieldKeys.WeaponSpeed];
        }

        if (string.Equals(blockType, IniConstants.BlockTypes.CommandButton, StringComparison.OrdinalIgnoreCase))
        {
            return [IniConstants.FieldKeys.Command, IniConstants.FieldKeys.Object, IniConstants.FieldKeys.Upgrade, IniConstants.FieldKeys.TextLabel, IniConstants.FieldKeys.ButtonImage];
        }

        if (string.Equals(blockType, IniConstants.BlockTypes.Upgrade, StringComparison.OrdinalIgnoreCase))
        {
            return [IniConstants.FieldKeys.Type, IniConstants.FieldKeys.BuildCost, IniConstants.FieldKeys.BuildTime, IniConstants.FieldKeys.DisplayName, IniConstants.FieldKeys.ButtonImage];
        }

        if (string.Equals(blockType, IniConstants.BlockTypes.Locomotor, StringComparison.OrdinalIgnoreCase))
        {
            return [IniConstants.FieldKeys.Speed, IniConstants.FieldKeys.TurnRate, IniConstants.FieldKeys.Lift, IniConstants.FieldKeys.Appearance];
        }

        return [IniConstants.FieldKeys.DisplayName, IniConstants.FieldKeys.ButtonImage, IniConstants.FieldKeys.Icon];
    }

    private static string? FindFieldValue(IniBlock block, string key)
    {
        return block.Fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static void SeedBlockTemplate(IniBlock block)
    {
        if (string.Equals(block.BlockType, IniConstants.BlockTypes.Object, StringComparison.OrdinalIgnoreCase))
        {
            block.Fields.Add(new IniField(IniConstants.FieldKeys.DisplayName, "OBJECT:Name"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.Side, "USA"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.BuildCost, "100"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.BuildTime, "5.0"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.Health, "100.0"));
        }
        else if (string.Equals(block.BlockType, IniConstants.BlockTypes.Weapon, StringComparison.OrdinalIgnoreCase))
        {
            block.Fields.Add(new IniField(IniConstants.FieldKeys.PrimaryDamage, "50.0"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.PrimaryDamageRadius, "20.0"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.AttackRange, "200.0"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.DamageType, "EXPLOSION"));
        }
        else if (string.Equals(block.BlockType, IniConstants.BlockTypes.Upgrade, StringComparison.OrdinalIgnoreCase))
        {
            block.Fields.Add(new IniField(IniConstants.FieldKeys.Type, "OBJECT"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.BuildCost, "500"));
            block.Fields.Add(new IniField(IniConstants.FieldKeys.BuildTime, "30.0"));
        }
    }

    private static void ExpandAncestors(IniTreeNodeViewModel node)
    {
        for (var current = node.Parent; current != null; current = current.Parent)
        {
            current.IsExpanded = true;
        }

        node.IsExpanded = true;
    }

    private static void SetFieldValueByKey(IList<IniField> fields, string key, string value)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (string.Equals(fields[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                fields[i] = fields[i] with { Value = value };
                return;
            }
        }
    }

    private static void SetFieldValue(IList<IniField> fields, int index, string key, string value)
    {
        if (index >= 0 &&
            index < fields.Count &&
            string.Equals(fields[index].Key, key, StringComparison.OrdinalIgnoreCase))
        {
            fields[index] = fields[index] with { Value = value };
            return;
        }

        SetFieldValueByKey(fields, key, value);
    }

    private static async Task RunDeferredRefreshAsync(CancellationTokenSource source, int delayMs, Action refresh)
    {
        try
        {
            await Task.Delay(delayMs, source.Token).ConfigureAwait(false);
            if (source.IsCancellationRequested)
            {
                return;
            }

            PostToUIThread(refresh);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Superseded by a newer edit, or the view model was disposed.
        }
    }

    private static void CollectExpandedBlocks(IEnumerable<IniTreeNodeViewModel> nodes, HashSet<IniBlock> expanded)
    {
        foreach (var node in nodes)
        {
            if (node.IsExpanded)
            {
                expanded.Add(node.Block);
            }

            CollectExpandedBlocks(node.Children, expanded);
        }
    }

    private static void RestoreExpandedBlocks(IEnumerable<IniTreeNodeViewModel> nodes, HashSet<IniBlock> expanded)
    {
        foreach (var node in nodes)
        {
            if (expanded.Contains(node.Block))
            {
                node.IsExpanded = true;
            }

            RestoreExpandedBlocks(node.Children, expanded);
        }
    }

    private static void RemoveAddedField(IList<IniField> fields, int index, string key)
    {
        if (index >= 0 &&
            index < fields.Count &&
            string.Equals(fields[index].Key, key, StringComparison.OrdinalIgnoreCase))
        {
            fields.RemoveAt(index);
            return;
        }

        RemoveFieldByKey(fields, key);
    }

    private static void RemoveFieldByKey(IList<IniField> fields, string key)
    {
        var match = fields.FirstOrDefault(field => string.Equals(field.Key, key, StringComparison.OrdinalIgnoreCase));
        if (match != null)
        {
            fields.Remove(match);
        }
    }

    private static List<IniField> BuildMissingTemplateFields(IniBlock block, (string Key, string Value)[] desired)
    {
        var missing = new List<IniField>(desired.Length);
        foreach (var (key, value) in desired)
        {
            if (FindFieldValue(block, key) == null)
            {
                missing.Add(new IniField(key, value));
            }
        }

        return missing;
    }

    private static bool AllowsEmptyName(string blockType)
    {
        return string.Equals(blockType, IniConstants.BlockTypes.ExperienceLevels, StringComparison.OrdinalIgnoreCase);
    }

    private static void CancelDeferred(ref CancellationTokenSource? slot)
    {
        slot?.Cancel();
        slot?.Dispose();
        slot = null;
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

    private static void PostToUIThread(Action action)
    {
        if (Avalonia.Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static string FormatCommandButtonAction(string? command, string? target)
    {
        if (command == null)
        {
            return target ?? string.Empty;
        }

        if (target == null)
        {
            return command;
        }

        return $"{command} → {target}";
    }

    private static void ScheduleDeferred(ref CancellationTokenSource? slot, int delayMs, Action refresh)
    {
        slot?.Cancel();
        var cts = new CancellationTokenSource();
        slot = cts;
        _ = Task.Run(() => RunDeferredRefreshAsync(cts, delayMs, refresh), CancellationToken.None);
    }

    /// <summary>
    /// Validates the open document and reports issues via toast.
    /// </summary>
    [RelayCommand]
    private void ValidateDocument()
    {
        if (_document == null)
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.IniEditor.Validate.NoDocumentTitle"),
                Localization.GetString("Tools.IniEditor.Validate.NoDocumentMessage"),
                NotificationDurations.Short);
            return;
        }

        var result = iniDocumentService.ValidateDocument(_document, FilePath ?? DocumentTitle);
        if (result.IsValid)
        {
            Notifications.ShowSuccess(
                Localization.GetString("Tools.IniEditor.Validate.SuccessTitle"),
                Localization.GetString("Tools.IniEditor.Validate.SuccessMessage", _document.Blocks.Count),
                NotificationDurations.Medium);
            return;
        }

        var first = result.Issues.Count > 0 ? result.Issues[0].Message : result.FirstError;
        Notifications.ShowWarning(
            Localization.GetString("Tools.IniEditor.Validate.IssuesTitle"),
            Localization.GetString("Tools.IniEditor.Validate.IssuesMessage", result.Issues.Count, first ?? string.Empty),
            NotificationDurations.Long);
    }

    /// <summary>
    /// Formats the open file on disk in canonical form.
    /// </summary>
    [RelayCommand]
    private async Task FormatDocumentAsync(CancellationToken cancellationToken = default)
    {
        if (_document == null || string.IsNullOrEmpty(FilePath))
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.IniEditor.Format.NoFileTitle"),
                Localization.GetString("Tools.IniEditor.Format.NoFileMessage"),
                NotificationDurations.Short);
            return;
        }

        await WriteDocumentToFileAsync(FilePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Adds a top-level block from the new block inputs.
    /// </summary>
    [RelayCommand]
    private void AddBlock()
    {
        if (_document == null || string.IsNullOrWhiteSpace(NewBlockType))
        {
            return;
        }

        var block = CreateValidatedBlock();
        if (block == null)
        {
            return;
        }

        SeedBlockTemplate(block);
        var index = _document.Blocks.Count;
        _document.Blocks.Add(block);
        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.AddBlock"),
            () =>
            {
                _document.Blocks.Insert(Math.Min(index, _document.Blocks.Count), block);
                RebuildAll();
            },
            () =>
            {
                _document.Blocks.Remove(block);
                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
        SelectBlock(block);
        NewBlockName = string.Empty;
    }

    /// <summary>
    /// Adds a field to the selected block from the new field inputs.
    /// </summary>
    [RelayCommand]
    private void AddField()
    {
        if (SelectedNode == null || string.IsNullOrWhiteSpace(NewFieldKey))
        {
            return;
        }

        var block = SelectedNode.Block;
        var field = new IniField(NewFieldKey.Trim(), NewFieldValue.Trim());
        var index = block.Fields.Count;
        block.Fields.Add(field);
        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.AddField"),
            () =>
            {
                block.Fields.Insert(Math.Min(index, block.Fields.Count), field);
                RebuildAll();
            },
            () =>
            {
                RemoveAddedField(block.Fields, index, field.Key);
                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
        NewFieldKey = string.Empty;
        NewFieldValue = string.Empty;
    }

    /// <summary>
    /// Adds a file-scope setting from the new file-setting inputs.
    /// </summary>
    [RelayCommand]
    private void AddGlobalField()
    {
        if (_document == null || string.IsNullOrWhiteSpace(NewGlobalFieldKey))
        {
            return;
        }

        var fields = _document.GlobalFields;
        var field = new IniField(NewGlobalFieldKey.Trim(), NewGlobalFieldValue.Trim());
        var index = fields.Count;
        fields.Add(field);
        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.AddField"),
            () =>
            {
                fields.Insert(Math.Min(index, fields.Count), field);
                RebuildAll();
            },
            () =>
            {
                RemoveAddedField(fields, index, field.Key);
                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
        NewGlobalFieldKey = string.Empty;
        NewGlobalFieldValue = string.Empty;
    }

    /// <summary>
    /// Deletes a single field row from the selected block.
    /// </summary>
    /// <param name="row">The field row to delete.</param>
    [RelayCommand]
    private void DeleteField(IniFieldRowViewModel? row)
    {
        if (row == null)
        {
            return;
        }

        var fields = row.OwnerFields;
        var index = row.FieldIndex;
        if (index < 0 || index >= fields.Count || !string.Equals(fields[index].Key, row.Key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var removed = fields[index];
        fields.RemoveAt(index);
        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.DeleteField"),
            () =>
            {
                RemoveAddedField(fields, index, removed.Key);
                RebuildAll();
            },
            () =>
            {
                fields.Insert(Math.Min(index, fields.Count), removed);
                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
    }

    /// <summary>
    /// Navigates to the block referenced by a field value.
    /// </summary>
    /// <param name="row">The field row whose reference to follow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task GoToDefinitionAsync(IniFieldRowViewModel? row, CancellationToken cancellationToken = default)
    {
        if (row?.ReferenceBlockType == null || string.IsNullOrWhiteSpace(row.Value))
        {
            return;
        }

        var local = _document?.Blocks.FirstOrDefault(block =>
            string.Equals(block.BlockType, row.ReferenceBlockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(block.Name, row.Value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (local != null)
        {
            SelectBlock(local);
            return;
        }

        var entry = referenceService.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.BlockType, row.ReferenceBlockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Name, row.Value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry?.FilePath == null)
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.IniEditor.Reference.NotFoundTitle"),
                Localization.GetString("Tools.IniEditor.Reference.NotFoundMessage", row.Value.Trim()),
                NotificationDurations.Short);
            return;
        }

        if (await OpenFileAsync(entry.FilePath, cancellationToken).ConfigureAwait(false))
        {
            var target = _document?.Blocks.FirstOrDefault(block =>
                string.Equals(block.BlockType, entry.BlockType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(block.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                SelectBlock(target);
            }
        }
    }

    /// <summary>
    /// Adds a standard upgrade hookup to the selected Object block.
    /// </summary>
    [RelayCommand]
    private void AddUpgradeHookup()
    {
        if (SelectedNode == null)
        {
            return;
        }

        var block = SelectedNode.Block;
        if (!string.Equals(block.BlockType, IniConstants.BlockTypes.Object, StringComparison.OrdinalIgnoreCase))
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.IniEditor.Upgrade.ObjectOnlyTitle"),
                Localization.GetString("Tools.IniEditor.Upgrade.ObjectOnlyMessage"),
                NotificationDurations.Short);
            return;
        }

        AddTemplateFields(block, UpgradeHookupTemplate, "Tools.IniEditor.History.AddUpgrade");
    }

    /// <summary>
    /// Adds a damage profile starter to the selected Weapon block.
    /// </summary>
    [RelayCommand]
    private void AddDamageProfile()
    {
        if (SelectedNode == null)
        {
            return;
        }

        var block = SelectedNode.Block;
        if (!string.Equals(block.BlockType, IniConstants.BlockTypes.Weapon, StringComparison.OrdinalIgnoreCase))
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.IniEditor.Damage.WeaponOnlyTitle"),
                Localization.GetString("Tools.IniEditor.Damage.WeaponOnlyMessage"),
                NotificationDurations.Short);
            return;
        }

        AddTemplateFields(block, DamageProfileTemplate, "Tools.IniEditor.History.AddDamage");
    }

    /// <summary>
    /// Clears the active reference type filter.
    /// </summary>
    [RelayCommand]
    private void ClearReferenceTypeFilter()
    {
        ReferenceTypeFilter = null;
    }

    [RelayCommand]
    private Task RefreshReferenceIndexAsync(CancellationToken cancellationToken = default) =>
        RebuildReferenceIndexAsync(true, cancellationToken);

    /// <summary>
    /// Inserts a copy of the selected reference block into the open document.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand(CanExecute = nameof(CanInsertReference))]
    private async Task InsertReferenceAsync(CancellationToken cancellationToken = default)
    {
        if (_document == null || SelectedReference == null)
        {
            return;
        }

        if (HasBlock(SelectedReference.BlockType, SelectedReference.Name))
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.AddBlock.DuplicateTitle"),
                Localization.GetString("Tools.IniEditor.AddBlock.DuplicateMessage", SelectedReference.BlockType, SelectedReference.Name),
                NotificationDurations.Long);
            return;
        }

        var cloned = await referenceService.CloneBlockAsync(SelectedReference, cancellationToken).ConfigureAwait(false);
        if (!cloned.Success || cloned.Data == null)
        {
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Reference.CloneFailureTitle"),
                Localization.GetString("Tools.IniEditor.Reference.CloneFailureMessage", cloned.FirstError ?? SelectedReference.Name),
                NotificationDurations.Long);
            return;
        }

        var block = cloned.Data;
        var index = _document.Blocks.Count;
        _document.Blocks.Add(block);
        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.InsertReference"),
            () =>
            {
                _document.Blocks.Insert(Math.Min(index, _document.Blocks.Count), block);
                RebuildAll();
            },
            () =>
            {
                _document.Blocks.Remove(block);
                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
        SelectBlock(block);
    }

    /// <summary>
    /// Expands all block tree nodes.
    /// </summary>
    [RelayCommand]
    private void ExpandAllBlocks()
    {
        SetAllExpanded(RootNodes, true);
    }

    /// <summary>
    /// Collapses all block tree nodes.
    /// </summary>
    [RelayCommand]
    private void CollapseAllBlocks()
    {
        SetAllExpanded(RootNodes, false);
    }

    private bool CanInsertReference => _document != null && SelectedReference != null;

    private async Task AdoptDocumentAsync(IniDocument document, string? filePath, CancellationToken cancellationToken)
    {
        EnsureCultureSubscription();
        await InvokeOnUIThreadAsync(() =>
        {
            _document = document;
            _undoStack.Clear();
            _redoStack.Clear();
            FilePath = filePath;
            UpdateExplorerForFile(filePath);
            RebuildAll();
            MarkSaved();
            _undoStack.TryPeek(out var topAction);
            _savedTopAction = topAction;
        }).ConfigureAwait(false);

        await RebuildReferenceIndexAsync(false, cancellationToken).ConfigureAwait(false);
        await LoadTexturePickerItemsAsync(cancellationToken).ConfigureAwait(false);
    }

    private void UpdateExplorerForFile(string? filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            FileExplorer.CurrentPath = filePath;
            var currentDirectory = FileExplorer.Directory;
            if (string.IsNullOrEmpty(currentDirectory) ||
                !Directory.Exists(currentDirectory) ||
                !IsSubPathOf(filePath, currentDirectory))
            {
                var directory = ResolveExplorerDirectory(Path.GetDirectoryName(filePath));
                if (!string.IsNullOrEmpty(directory) &&
                    !string.Equals(FileExplorer.Directory, directory, PathHelper.PathComparison))
                {
                    FileExplorer.Directory = directory;
                }
            }
        }

        RefreshEditorCommands();
    }

    private void RebuildAll()
    {
        _isRebuilding = true;
        try
        {
            RebuildTree();
            RebuildFieldRows();
            RebuildGlobalFieldRows();
            RebuildCanvasSummary();
            RebuildAssembledRows();
            RefreshRawText();
            HasDocument = _document != null;
            InsertReferenceCommand.NotifyCanExecuteChanged();
            RefreshEditorCommands();
            QueueThumbnailRefresh();
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private void RebuildTree()
    {
        var selectedBlock = SelectedNode?.Block;
        var expanded = new HashSet<IniBlock>();
        CollectExpandedBlocks(RootNodes, expanded);
        RootNodes.Clear();
        if (_document == null)
        {
            return;
        }

        var filter = BlockFilter?.Trim();
        foreach (var block in _document.Blocks)
        {
            if (!MatchesFilter(block, filter))
            {
                continue;
            }

            RootNodes.Add(BuildTreeNode(block, null));
        }

        RestoreExpandedBlocks(RootNodes, expanded);
        if (selectedBlock == null)
        {
            return;
        }

        var match = FindNode(RootNodes, selectedBlock);
        if (match == null)
        {
            SelectedNode = null;
            return;
        }

        ExpandAncestors(match);
        SelectedNode = match;
    }

    private void RebuildFieldRows()
    {
        FieldRows.Clear();
        var block = SelectedNode?.Block;
        if (block == null)
        {
            return;
        }

        for (var i = 0; i < block.Fields.Count; i++)
        {
            var key = block.Fields[i].Key;
            var fieldIndex = i;
            var known = schemaService.TryGetField(block.BlockType, key, out var schema);
            var metadata = new IniFieldMetadata(
                schema?.Description,
                known,
                BuildFieldTooltip(key, schema),
                ResolveSuggestions(schema),
                schema?.ReferenceBlockType,
                schema?.IsTexture ?? false);
            FieldRows.Add(new IniFieldRowViewModel(
                block.Fields,
                fieldIndex,
                metadata,
                MarkDocumentDirty,
                (oldValue, newValue) => PushFieldValueUndo(block.Fields, key, fieldIndex, oldValue, newValue)));
        }
    }

    private void RebuildGlobalFieldRows()
    {
        GlobalFieldRows.Clear();
        if (_document == null)
        {
            HasGlobalFields = false;
            return;
        }

        var fields = _document.GlobalFields;
        for (var i = 0; i < fields.Count; i++)
        {
            var key = fields[i].Key;
            var fieldIndex = i;
            var metadata = new IniFieldMetadata(
                Tooltip: BuildFieldTooltip(key, null));
            GlobalFieldRows.Add(new IniFieldRowViewModel(
                fields,
                fieldIndex,
                metadata,
                MarkDocumentDirty,
                (oldValue, newValue) => PushFieldValueUndo(fields, key, fieldIndex, oldValue, newValue)));
        }

        HasGlobalFields = GlobalFieldRows.Count > 0;
    }

    private void RebuildCanvasSummary()
    {
        CanvasSummary.Clear();
        var block = SelectedNode?.Block;
        if (block == null || _document == null)
        {
            return;
        }

        CanvasSummary.Add(new IniCanvasSummaryRow(
            Localization.GetString("Tools.IniEditor.Canvas.BlockType"),
            block.BlockType));
        if (!string.IsNullOrEmpty(block.Name))
        {
            CanvasSummary.Add(new IniCanvasSummaryRow(
                Localization.GetString("Tools.IniEditor.Canvas.BlockName"),
                block.Name));
        }

        var schema = schemaService.GetBlockSchema(block.BlockType);
        if (schema != null)
        {
            CanvasSummary.Add(new IniCanvasSummaryRow(
                Localization.GetString("Tools.IniEditor.Canvas.Schema"),
                schema.Description));
        }

        foreach (var key in CanvasHighlightKeys(block.BlockType))
        {
            var value = FindFieldValue(block, key);
            if (value != null)
            {
                CanvasSummary.Add(new IniCanvasSummaryRow(key, value));
            }
        }

        AddObjectCanvasLinks(block);
        AddWeaponCanvasDamage(block);
    }

    private void RebuildAssembledRows()
    {
        AssembledRows.Clear();
        var block = SelectedNode?.Block;
        if (block == null || _document == null)
        {
            return;
        }

        if (string.Equals(block.BlockType, IniConstants.BlockTypes.Object, StringComparison.OrdinalIgnoreCase))
        {
            BuildObjectAssembledRows(block);
        }
        else if (string.Equals(block.BlockType, IniConstants.BlockTypes.Weapon, StringComparison.OrdinalIgnoreCase))
        {
            BuildWeaponAssembledRows(block);
        }
        else
        {
            BuildGenericAssembledRows(block);
        }

        BuildTextureAssembledRows(block);
    }

    private void BuildObjectAssembledRows(IniBlock block)
    {
        foreach (var child in block.Children)
        {
            AssembledRows.Add(new IniAssembledRowViewModel(
                Localization.GetString("Tools.IniEditor.Assembled.ModuleLabel", child.BlockType),
                child.AssignmentValue ?? child.Name,
                Localization.GetString("Tools.IniEditor.Assembled.ModuleDetail", child.Fields.Count, child.Children.Count),
                BuildModuleTooltip(child),
                null));
        }

        var weaponSetName = FindFieldValue(block, IniConstants.BlockTypes.WeaponSet);
        if (weaponSetName != null)
        {
            var set = FindBlocks(IniConstants.BlockTypes.WeaponSet, weaponSetName).FirstOrDefault();
            if (set != null)
            {
                foreach (var field in set.Fields.Where(field => string.Equals(field.Key, "Weapon", StringComparison.OrdinalIgnoreCase)))
                {
                    AddWeaponSlotRow(field.Value);
                }
            }
            else
            {
                AddUnresolvedRow(
                    Localization.GetString("Tools.IniEditor.Assembled.WeaponSetLabel"),
                    weaponSetName,
                    IniConstants.BlockTypes.WeaponSet);
            }
        }

        var armorSetName = FindFieldValue(block, IniConstants.BlockTypes.ArmorSet);
        if (armorSetName != null)
        {
            AddArmorSetRow(armorSetName);
        }

        var commandSetName = FindFieldValue(block, IniConstants.BlockTypes.CommandSet);
        if (commandSetName != null)
        {
            AddCommandSetRows(commandSetName);
        }

        foreach (var upgrade in block.Fields.Where(field =>
            string.Equals(field.Key, IniConstants.FieldKeys.Upgrade, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field.Key, "Upgrades", StringComparison.OrdinalIgnoreCase)))
        {
            AddReferenceRow(
                Localization.GetString("Tools.IniEditor.Assembled.UpgradeLabel"),
                upgrade.Value,
                IniConstants.BlockTypes.Upgrade);
        }
    }

    private void BuildWeaponAssembledRows(IniBlock block)
    {
        var damage = FindFieldValue(block, IniConstants.FieldKeys.PrimaryDamage);
        var damageType = FindFieldValue(block, IniConstants.FieldKeys.DamageType);
        var range = FindFieldValue(block, "AttackRange");
        AssembledRows.Add(new IniAssembledRowViewModel(
            Localization.GetString("Tools.IniEditor.Assembled.DamageLabel"),
            $"{damage ?? "?"} ({damageType ?? "?"})",
            range == null ? null : Localization.GetString("Tools.IniEditor.Assembled.RangeDetail", range),
            Localization.GetString("Tools.IniEditor.Assembled.DamageTooltip", damage ?? "?", damageType ?? "?", range ?? "?"),
            null));

        var projectile = FindFieldValue(block, "ProjectileObject");
        if (projectile != null)
        {
            AddReferenceRow(
                Localization.GetString("Tools.IniEditor.Assembled.ProjectileLabel"),
                projectile,
                IniConstants.BlockTypes.Object);
        }
    }

    private void BuildGenericAssembledRows(IniBlock block)
    {
        foreach (var child in block.Children)
        {
            AssembledRows.Add(new IniAssembledRowViewModel(
                child.DisplayHeader,
                Localization.GetString("Tools.IniEditor.Assembled.ModuleDetail", child.Fields.Count, child.Children.Count),
                null,
                BuildModuleTooltip(child),
                null));
        }
    }

    private void BuildTextureAssembledRows(IniBlock block)
    {
        foreach (var field in block.Fields)
        {
            if (!schemaService.TryGetField(block.BlockType, field.Key, out var schema) || schema?.IsTexture != true)
            {
                continue;
            }

            AssembledRows.Add(new IniAssembledRowViewModel(
                field.Key,
                field.Value,
                Localization.GetString("Tools.IniEditor.Assembled.TextureDetail"),
                Localization.GetString("Tools.IniEditor.Assembled.TextureTooltip", field.Value),
                field.Value));
        }
    }

    private void AddWeaponSlotRow(string slotValue)
    {
        var parts = slotValue.Split([' '], StringSplitOptions.RemoveEmptyEntries);
        var slot = parts.Length > 0 ? parts[0] : slotValue;
        var weaponName = parts.Length > 1 ? string.Join(' ', parts[1..]) : string.Empty;
        var weapon = string.IsNullOrEmpty(weaponName) ? null : FindBlocks(IniConstants.BlockTypes.Weapon, weaponName).FirstOrDefault();
        if (weapon == null)
        {
            AddUnresolvedRow(
                Localization.GetString("Tools.IniEditor.Assembled.WeaponSlotLabel", slot),
                weaponName,
                IniConstants.BlockTypes.Weapon);
            return;
        }

        var damage = FindFieldValue(weapon, IniConstants.FieldKeys.PrimaryDamage) ?? "?";
        var damageType = FindFieldValue(weapon, IniConstants.FieldKeys.DamageType) ?? "?";
        AssembledRows.Add(new IniAssembledRowViewModel(
            Localization.GetString("Tools.IniEditor.Assembled.WeaponSlotLabel", slot),
            weapon.Name,
            $"{damage} ({damageType})",
            Localization.GetString("Tools.IniEditor.Assembled.WeaponSlotTooltip", weapon.Name, damage, damageType),
            null));
    }

    private void AddArmorSetRow(string armorSetName)
    {
        var set = FindBlocks(IniConstants.BlockTypes.ArmorSet, armorSetName).FirstOrDefault();
        var tableName = set == null ? null : FindFieldValue(set, "Armor");
        var table = tableName == null ? null : FindBlocks(IniConstants.BlockTypes.Armor, tableName).FirstOrDefault();
        if (table == null)
        {
            AddUnresolvedRow(
                Localization.GetString("Tools.IniEditor.Assembled.ArmorLabel"),
                tableName ?? armorSetName,
                IniConstants.BlockTypes.Armor);
            return;
        }

        var preview = string.Join(", ", table.Fields.Take(3).Select(field => $"{field.Key} {field.Value}"));
        AssembledRows.Add(new IniAssembledRowViewModel(
            Localization.GetString("Tools.IniEditor.Assembled.ArmorLabel"),
            table.Name,
            preview,
            Localization.GetString("Tools.IniEditor.Assembled.ArmorTooltip", armorSetName, table.Name, table.Fields.Count),
            null));
    }

    private void AddCommandSetRows(string commandSetName)
    {
        var set = FindBlocks(IniConstants.BlockTypes.CommandSet, commandSetName).FirstOrDefault();
        if (set == null)
        {
            AddUnresolvedRow(
                Localization.GetString("Tools.IniEditor.Assembled.CommandSetLabel"),
                commandSetName,
                IniConstants.BlockTypes.CommandSet);
            return;
        }

        foreach (var slot in set.Fields)
        {
            var button = FindBlocks(IniConstants.BlockTypes.CommandButton, slot.Value).FirstOrDefault();
            if (button == null)
            {
                AddUnresolvedRow(
                    Localization.GetString("Tools.IniEditor.Assembled.CommandSlotLabel", slot.Key),
                    slot.Value,
                    IniConstants.BlockTypes.CommandButton);
                continue;
            }

            var command = FindFieldValue(button, "Command");
            var target = FindFieldValue(button, "Object") ?? FindFieldValue(button, IniConstants.FieldKeys.Upgrade);
            var texture = FindFieldValue(button, IniConstants.FieldKeys.ButtonImage);
            AssembledRows.Add(new IniAssembledRowViewModel(
                Localization.GetString("Tools.IniEditor.Assembled.CommandSlotLabel", slot.Key),
                button.Name,
                FormatCommandButtonAction(command, target),
                Localization.GetString("Tools.IniEditor.Assembled.CommandSlotTooltip", button.Name, command ?? "?", target ?? "?"),
                texture));
        }
    }

    private void AddReferenceRow(string label, string value, string blockType)
    {
        var local = FindBlocks(blockType, value).FirstOrDefault();
        if (local != null)
        {
            AssembledRows.Add(new IniAssembledRowViewModel(
                label,
                value,
                Localization.GetString("Tools.IniEditor.Assembled.LocalDetail"),
                Localization.GetString("Tools.IniEditor.Assembled.LocalTooltip", blockType, value),
                null));
            return;
        }

        var entry = referenceService.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.BlockType, blockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Name, value, StringComparison.OrdinalIgnoreCase));
        if (entry == null)
        {
            AddUnresolvedRow(label, value, blockType);
            return;
        }

        AssembledRows.Add(new IniAssembledRowViewModel(
            label,
            value,
            entry.SourceLabel,
            Localization.GetString("Tools.IniEditor.Assembled.ReferenceTooltip", blockType, value, entry.SourceLabel, entry.FilePath ?? string.Empty),
            null));
    }

    private void AddUnresolvedRow(string label, string value, string blockType)
    {
        AssembledRows.Add(new IniAssembledRowViewModel(
            label,
            value,
            Localization.GetString("Tools.IniEditor.Assembled.UnresolvedDetail"),
            Localization.GetString("Tools.IniEditor.Assembled.UnresolvedTooltip", blockType, value),
            null));
    }

    private string BuildModuleTooltip(IniBlock child)
    {
        var fields = string.Join(", ", child.Fields.Take(4).Select(field => $"{field.Key} = {field.Value}"));
        return Localization.GetString(
            "Tools.IniEditor.Assembled.ModuleTooltip",
            child.DisplayHeader,
            child.Fields.Count,
            child.Children.Count,
            fields);
    }

    private void AddObjectCanvasLinks(IniBlock block)
    {
        if (!string.Equals(block.BlockType, IniConstants.BlockTypes.Object, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var weaponSet = FindFieldValue(block, IniConstants.BlockTypes.WeaponSet);
        if (weaponSet != null)
        {
            foreach (var weapon in FindBlocks(IniConstants.BlockTypes.WeaponSet, weaponSet))
            {
                CanvasSummary.Add(new IniCanvasSummaryRow(
                    Localization.GetString("Tools.IniEditor.Canvas.LinkedWeapon"),
                    $"{weapon.Name} ({weapon.Fields.Count})"));
            }
        }

        var commandSet = FindFieldValue(block, IniConstants.BlockTypes.CommandSet);
        if (commandSet != null)
        {
            CanvasSummary.Add(new IniCanvasSummaryRow(
                Localization.GetString("Tools.IniEditor.Canvas.LinkedCommandSet"),
                commandSet));
        }

        var upgrades = block.Fields.Where(field =>
            string.Equals(field.Key, IniConstants.FieldKeys.Upgrade, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(field.Key, IniConstants.FieldKeys.Upgrades, StringComparison.OrdinalIgnoreCase)).ToList();
        if (upgrades.Count > 0)
        {
            CanvasSummary.Add(new IniCanvasSummaryRow(
                Localization.GetString("Tools.IniEditor.Canvas.Upgrades"),
                string.Join(", ", upgrades.Select(field => field.Value))));
        }
    }

    private void AddWeaponCanvasDamage(IniBlock block)
    {
        if (!string.Equals(block.BlockType, IniConstants.BlockTypes.Weapon, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var damage = FindFieldValue(block, IniConstants.FieldKeys.PrimaryDamage);
        var damageType = FindFieldValue(block, IniConstants.FieldKeys.DamageType);
        if (damage != null || damageType != null)
        {
            CanvasSummary.Add(new IniCanvasSummaryRow(
                Localization.GetString("Tools.IniEditor.Canvas.Damage"),
                $"{damage ?? "?"} ({damageType ?? "?"})"));
        }
    }

    private IEnumerable<IniBlock> FindBlocks(string blockType, string name)
    {
        if (_document == null)
        {
            return [];
        }

        return _document.Blocks.Where(block =>
            string.Equals(block.BlockType, blockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(block.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshRawText()
    {
        if (_document == null)
        {
            RawText = string.Empty;
            return;
        }

        var text = iniDocumentService.WriteDocument(_document);
        RawText = text.Length > IniConstants.Editor.MaxRawPreviewChars
            ? text[..IniConstants.Editor.MaxRawPreviewChars] + Localization.GetString("Tools.IniEditor.Preview.TruncatedMessage")
            : text;
    }

    private void RefreshPreviews()
    {
        RefreshRawText();
        RebuildCanvasSummary();
        RebuildAssembledRows();
    }

    private void MarkDocumentDirty()
    {
        MarkDirty();
        ScheduleDeferred(ref _previewCts, IniConstants.Editor.PreviewRefreshDebounceMs, RefreshPreviews);
    }

    private void SyncDirtyAfterHistory()
    {
        _undoStack.TryPeek(out var top);
        if (ReferenceEquals(top, _savedTopAction))
        {
            MarkSaved();
        }
        else
        {
            MarkDirty();
        }
    }

    private void PushUndo(IniEditAction action)
    {
        if (action.CoalesceKey != null &&
            _undoStack.TryPeek(out var top) &&
            Equals(top.CoalesceKey, action.CoalesceKey))
        {
            _undoStack.Pop();
            action = action with { Undo = top.Undo };
        }

        _undoStack.Push(action);
        if (_undoStack.Count > IniConstants.Editor.MaxUndoHistory)
        {
            var kept = _undoStack.Take(IniConstants.Editor.MaxUndoHistory).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var item in kept)
            {
                _undoStack.Push(item);
            }
        }

        _redoStack.Clear();
        RefreshEditorCommands();
    }

    private void PushFieldValueUndo(IList<IniField> fields, string key, int fieldIndex, string oldValue, string newValue)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.EditField"),
            () =>
            {
                SetFieldValue(fields, fieldIndex, key, newValue);
                RebuildAll();
            },
            () =>
            {
                SetFieldValue(fields, fieldIndex, key, oldValue);
                RebuildAll();
            },
            (fields, fieldIndex)));
    }

    private IniBlock? CreateValidatedBlock()
    {
        var blockType = NewBlockType.Trim();
        var name = NewBlockName.Trim();
        if (name.Length == 0 && !AllowsEmptyName(blockType))
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.AddBlock.EmptyNameTitle"),
                Localization.GetString("Tools.IniEditor.AddBlock.EmptyNameMessage", blockType),
                NotificationDurations.Long);
            return null;
        }

        if (name.Length > 0 && HasBlock(blockType, name))
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.AddBlock.DuplicateTitle"),
                Localization.GetString("Tools.IniEditor.AddBlock.DuplicateMessage", blockType, name),
                NotificationDurations.Long);
            return null;
        }

        return new IniBlock { BlockType = blockType, Name = name, LineNumber = 0 };
    }

    private bool HasBlock(string blockType, string name)
    {
        return _document != null && _document.Blocks.Any(block =>
            string.Equals(block.BlockType, blockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(block.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void SelectBlock(IniBlock block)
    {
        var match = FindNode(RootNodes, block);
        if (match != null)
        {
            ExpandAncestors(match);
            SelectedNode = match;
        }
    }

    private void DeleteBlock(IniTreeNodeViewModel node)
    {
        if (_document == null)
        {
            return;
        }

        var siblings = node.Parent == null ? _document.Blocks : node.Parent.Block.Children;
        var index = siblings.IndexOf(node.Block);
        if (index < 0)
        {
            SelectedNode = null;
            return;
        }

        siblings.RemoveAt(index);
        PushUndo(new IniEditAction(
            Localization.GetString("Tools.IniEditor.History.DeleteBlock"),
            () =>
            {
                siblings.Remove(node.Block);
                RebuildAll();
            },
            () =>
            {
                siblings.Insert(Math.Min(index, siblings.Count), node.Block);
                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
    }

    private void AddTemplateFields(IniBlock block, (string Key, string Value)[] template, string historyKey)
    {
        var added = BuildMissingTemplateFields(block, template);
        if (added.Count == 0)
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.IniEditor.Template.SkippedTitle"),
                Localization.GetString("Tools.IniEditor.Template.SkippedMessage"),
                NotificationDurations.Short);
            return;
        }

        var startIndex = block.Fields.Count;
        foreach (var field in added)
        {
            block.Fields.Add(field);
        }

        PushUndo(new IniEditAction(
            Localization.GetString(historyKey),
            () =>
            {
                for (var i = 0; i < added.Count; i++)
                {
                    block.Fields.Insert(Math.Min(startIndex + i, block.Fields.Count), added[i]);
                }

                RebuildAll();
            },
            () =>
            {
                for (var i = added.Count - 1; i >= 0; i--)
                {
                    RemoveAddedField(block.Fields, startIndex + i, added[i].Key);
                }

                RebuildAll();
            }));
        MarkDirty();
        RebuildAll();
    }

    private string SerializeBlocks(IReadOnlyList<IniBlock> blocks)
    {
        var document = new IniDocument();
        foreach (var block in blocks)
        {
            document.Blocks.Add(block);
        }

        return iniDocumentService.WriteDocument(document);
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to copy INI text to the clipboard");
        }
    }

    private async Task<string?> BrowseExplorerFolderAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.GetString("Tools.IniEditor.FileDialog.FolderTitle"),
            AllowMultiple = false,
        }).ConfigureAwait(true);
        if (folders.Count == 0)
        {
            return null;
        }

        var localPath = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(localPath))
        {
            return null;
        }

        var gameFilesEdited = Path.Combine(localPath, ModBuilderConstants.GameFilesEditedDir);
        return Directory.Exists(gameFilesEdited) ? gameFilesEdited : localPath;
    }

    private async Task OnExplorerDirectoryAdoptedAsync(string directory, CancellationToken cancellationToken)
    {
        var first = FileExplorer.FindFirstFile();
        if (string.IsNullOrEmpty(first))
        {
            return;
        }

        await OpenFileAsync(first, cancellationToken).ConfigureAwait(false);
        await LoadTexturePickerItemsAsync(cancellationToken).ConfigureAwait(false);
    }

    private async void OnExplorerFileActivated(object? sender, EditorFileTreeNodeViewModel node)
    {
        try
        {
            await OpenFileAsync(node.FullPath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Discard confirmation was cancelled.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to open INI file {Path} from the explorer", node.FullPath);
        }
    }

    private async Task RebuildReferenceIndexAsync(bool forceRescan, CancellationToken cancellationToken)
    {
        await RunOperationAsync(async token =>
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, cancellationToken);
            var result = await referenceService.RebuildIndexAsync(_document, FileExplorer.Directory, forceRescan, linked.Token).ConfigureAwait(false);
            if (!result.Success)
            {
                logger.LogWarning("Reference index rebuild reported: {Error}", result.FirstError);
            }

            await InvokeOnUIThreadAsync(FilterReferenceResults).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private void FilterReferenceResults()
    {
        ReferenceResults.Clear();
        var filter = ReferenceFilter?.Trim();
        foreach (var entry in referenceService.Entries)
        {
            if (!string.IsNullOrEmpty(ReferenceTypeFilter) &&
                !string.Equals(entry.BlockType, ReferenceTypeFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrEmpty(filter) &&
                !entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) &&
                !entry.BlockType.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            ReferenceResults.Add(entry);
            if (ReferenceResults.Count >= IniConstants.Editor.MaxReferenceResults)
            {
                break;
            }
        }
    }

    private IReadOnlyList<string>? ResolveSuggestions(IniFieldSchema? schema)
    {
        if (schema?.Options != null)
        {
            return schema.Options;
        }

        if (schema?.ReferenceBlockType != null)
        {
            return referenceService.GetNames(schema.ReferenceBlockType);
        }

        if (schema?.IsTexture == true && TexturePickerItems.Count > 0)
        {
            return TexturePickerItems.Select(item => item.Name).ToList();
        }

        return null;
    }

    private string? BuildFieldTooltip(string key, IniFieldSchema? schema)
    {
        if (schema == null)
        {
            return Localization.GetString("Tools.IniEditor.Fields.CustomTooltip", key);
        }

        if (schema.ReferenceBlockType != null)
        {
            return Localization.GetString(
                "Tools.IniEditor.Fields.ReferenceTooltip",
                schema.Description,
                schema.ReferenceBlockType);
        }

        if (schema.IsTexture)
        {
            return Localization.GetString(
                "Tools.IniEditor.Fields.TextureTooltip",
                schema.Description);
        }

        if (schema.Options != null)
        {
            return Localization.GetString(
                "Tools.IniEditor.Fields.OptionsTooltip",
                schema.Description,
                schema.Options.Count);
        }

        return schema.Description;
    }

    private void QueueThumbnailRefresh()
    {
        ScheduleDeferred(ref _thumbnailCts, IniConstants.Editor.ThumbnailDebounceMs, () => _ = RefreshThumbnailsAsync());
    }

    private async Task RefreshThumbnailsAsync()
    {
        var generation = ++_thumbnailGeneration;
        try
        {
            await EnsureInstallationsAsync(CancellationToken.None).ConfigureAwait(false);
            var names = CollectTextureNames();
            if (names.Count == 0)
            {
                return;
            }

            var missing = names.Where(name => !_textureThumbnails.ContainsKey(name)).ToList();
            if (missing.Count > 0)
            {
                await LoadThumbnailsAsync(missing, CancellationToken.None).ConfigureAwait(false);
            }

            if (generation != _thumbnailGeneration)
            {
                return;
            }

            await InvokeOnUIThreadAsync(ApplyCachedThumbnails).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer refresh.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to refresh INI texture thumbnails");
        }
    }

    private List<string> CollectTextureNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in FieldRows.Where(r => r.IsTexture && !string.IsNullOrWhiteSpace(r.Value)))
        {
            names.Add(row.Value.Trim());
        }

        foreach (var row in AssembledRows.Where(r => !string.IsNullOrWhiteSpace(r.TextureName)))
        {
            names.Add(row.TextureName!.Trim());
        }

        foreach (var item in TexturePickerItems.Take(IniConstants.Editor.MaxPickerThumbnails))
        {
            names.Add(item.Name);
        }

        return names.ToList();
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<string> names, CancellationToken cancellationToken)
    {
        var installation = SelectedInstallation ?? AvailableInstallations.FirstOrDefault();
        if (installation == null)
        {
            return;
        }

        var projectDirectory = string.IsNullOrEmpty(FilePath) ? null : Path.GetDirectoryName(FilePath);
        var result = await imageAssetService.GetImagesAsync(
            names,
            installation.Path,
            null,
            projectDirectory,
            [],
            installation.IsZeroHour,
            cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            return;
        }

        var decoded = new Dictionary<string, Bitmap?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, png) in result.Data)
        {
            try
            {
                using var stream = new MemoryStream(png);
                decoded[name] = new Bitmap(stream);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException)
            {
                logger.LogWarning(ex, "Failed to decode texture thumbnail {Name}", name);
                decoded[name] = null;
            }
        }

        foreach (var name in names)
        {
            decoded.TryAdd(name, null);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            foreach (var (name, bitmap) in decoded)
            {
                _textureThumbnails[name] = bitmap;
            }

            ApplyCachedThumbnails();
        }).ConfigureAwait(false);
    }

    private void ApplyCachedThumbnails()
    {
        foreach (var row in FieldRows)
        {
            if (row.IsTexture &&
                !string.IsNullOrWhiteSpace(row.Value) &&
                _textureThumbnails.TryGetValue(row.Value.Trim(), out var thumbnail))
            {
                row.TextureThumbnail = thumbnail;
            }
        }

        foreach (var row in AssembledRows)
        {
            if (!string.IsNullOrWhiteSpace(row.TextureName) &&
                _textureThumbnails.TryGetValue(row.TextureName.Trim(), out var thumbnail))
            {
                row.TextureThumbnail = thumbnail;
            }
        }

        OnPropertyChanged(nameof(PickerThumbnailProvider));
    }

    private async Task LoadTexturePickerItemsAsync(CancellationToken cancellationToken)
    {
        var directory = FileExplorer.Directory;
        var files = new List<string>();
        if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory))
        {
            try
            {
                files.AddRange(Directory
                    .EnumerateFiles(directory, ModBuilderConstants.FileNames.IniSearchPattern, SearchOption.AllDirectories)
                    .Where(file => file.Contains("MappedImages", StringComparison.OrdinalIgnoreCase))
                    .Take(IniConstants.Editor.MaxMappedImageFiles));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(ex, "Failed to enumerate mapped image files in {Directory}", directory);
            }
        }

        var definitions = new List<MappedImageDefinition>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var parsed = await mappedImageParser.ParseFileAsync(file, cancellationToken).ConfigureAwait(false);
            if (parsed.Success && parsed.Data != null)
            {
                definitions.AddRange(parsed.Data.Take(IniConstants.Editor.MaxPickerDefinitions - definitions.Count));
            }

            if (definitions.Count >= IniConstants.Editor.MaxPickerDefinitions)
            {
                break;
            }
        }

        await InvokeOnUIThreadAsync(() =>
        {
            TexturePickerItems.Clear();
            foreach (var definition in definitions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                TexturePickerItems.Add(definition);
            }

            RebuildFieldRows();
        }).ConfigureAwait(false);
        QueueThumbnailRefresh();
    }

    private async Task EnsureInstallationsAsync(CancellationToken cancellationToken)
    {
        if (_installationsLoaded)
        {
            return;
        }

        _installationsLoaded = true;
        var result = await installationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            return;
        }

        _installations.AddRange(result.Data);
        await InvokeOnUIThreadAsync(() =>
        {
            foreach (var installation in _installations)
            {
                if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
                {
                    AvailableInstallations.Add(new GameInstallationOption
                    {
                        DisplayName = $"Zero Hour ({installation.InstallationType.GetDisplayName()})",
                        Path = installation.ZeroHourPath,
                        IsZeroHour = true,
                    });
                }

                if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
                {
                    AvailableInstallations.Add(new GameInstallationOption
                    {
                        DisplayName = $"Generals ({installation.InstallationType.GetDisplayName()})",
                        Path = installation.GeneralsPath,
                        IsZeroHour = false,
                    });
                }
            }

            SelectedInstallation = SelectBestInstallation();
        }).ConfigureAwait(false);
    }

    private GameInstallationOption? SelectBestInstallation()
    {
        if (!string.IsNullOrEmpty(FilePath))
        {
            var containing = AvailableInstallations.FirstOrDefault(option => IsPathUnder(FilePath, option.Path));
            if (containing != null)
            {
                return containing;
            }
        }

        return AvailableInstallations.FirstOrDefault();
    }

    private async Task WriteDocumentToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_document == null)
        {
            return;
        }

        try
        {
            var canonical = iniDocumentService.WriteDocument(_document);
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await AtomicFile.WriteAllTextAsync(filePath, canonical, _document.SourceEncoding, cancellationToken).ConfigureAwait(false);
            await InvokeOnUIThreadAsync(() =>
            {
                FilePath = filePath;
                UpdateExplorerForFile(filePath);
                MarkSaved();
                _undoStack.TryPeek(out var topAction);
                _savedTopAction = topAction;
            }).ConfigureAwait(false);
            Notifications.ShowSuccess(
                Localization.GetString("Tools.IniEditor.Save.SuccessTitle"),
                Localization.GetString("Tools.IniEditor.Save.SuccessMessage", DocumentTitle),
                NotificationDurations.Medium);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogError(ex, "Failed to save INI file {Path}", filePath);
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Save.FailureTitle"),
                Localization.GetString("Tools.IniEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
        }
    }

    private void EnsureCultureSubscription()
    {
        if (_cultureSubscribed)
        {
            return;
        }

        _cultureSubscribed = true;
        Localization.PropertyChanged += OnLocalizationPropertyChanged;
    }

    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ILocalizationService.CurrentCulture))
        {
            return;
        }

        PostToUIThread(RefreshLocalizedStrings);
    }

    private void RefreshLocalizedStrings()
    {
        RebuildFieldRows();
        RebuildGlobalFieldRows();
        RebuildCanvasSummary();
        RebuildAssembledRows();
        FilterReferenceResults();
        RefreshEditorCommands();
    }

    partial void OnSelectedNodeChanged(IniTreeNodeViewModel? value)
    {
        if (_isRebuilding)
        {
            return;
        }

        RebuildFieldRows();
        RebuildCanvasSummary();
        RebuildAssembledRows();
        QueueThumbnailRefresh();
    }

    partial void OnBlockFilterChanged(string? value)
    {
        ScheduleDeferred(ref _filterCts, IniConstants.Editor.FilterDebounceMs, RebuildTree);
    }

    partial void OnReferenceFilterChanged(string? value)
    {
        FilterReferenceResults();
    }

    partial void OnReferenceTypeFilterChanged(string? value)
    {
        FilterReferenceResults();
    }

    partial void OnSelectedInstallationChanged(GameInstallationOption? value)
    {
        _textureThumbnails.Clear();
        QueueThumbnailRefresh();
    }
}
