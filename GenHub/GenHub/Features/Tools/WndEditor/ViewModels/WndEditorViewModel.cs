using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Editors;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Telemetry;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Tools.Common;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Core.Services.Tools.WndEditor;
using GenHub.Features.Tools.ModBuilder.Models;
using GenHub.Features.Tools.WndEditor.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// ViewModel for the WND editor tool. Edits window definition documents with undo support.
/// </summary>
[method: SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for WND editor tool orchestrator.")]
[SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Primary constructor injects required services for WND editor tool orchestrator.")]
public sealed partial class WndEditorViewModel(
    IWndDocumentService wndDocumentService,
    INotificationService notificationService,
    ILocalizationService localizationService,
    IDialogService dialogService,
    IGameInstallationService gameInstallationService,
    IWndEditorAssetService assetService,
    IWndTextureImportService textureImportService,
    IChallengeMedalService medalService,
    ILogger<WndEditorViewModel> logger,
    ITelemetryService? telemetryService = null) : EditorToolViewModelBase(notificationService, localizationService, dialogService)
{
    private const int MaxUndoHistory = 200;
    private const string NoSelectionTitleKey = "Tools.WndEditor.Apply.NoSelectionTitle";
    private const string NoSelectionMessageKey = "Tools.WndEditor.Apply.NoSelectionMessage";

    private static Cursor? _handCursor;

    private static Cursor HandCursor => _handCursor ??= new(StandardCursorType.Hand);

    private readonly Stack<WndEditAction> _undoStack = new();
    private readonly Stack<WndEditAction> _redoStack = new();
    private readonly Dictionary<string, Bitmap> _composedBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _thumbnailBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, WndWindow> _windowParents = new();
    private readonly object _previewSync = new();
    private readonly object _linkedAssetsSync = new();
    private readonly object _thumbnailSync = new();
    private FileExplorerViewModel? _fileExplorer;
    private WndWindow? _copiedWindow;
    private WndWindow? _copySourceWindow;
    private WndWindow? _lastPastedClone;
    private bool _isCutOperation;
    private int _historyVersion;
    private int _savedHistoryVersion;
    private WndDocument? _document;
    private double _canvasBaseWidth = WndConstants.Editor.MinCanvasWidth;
    private double _canvasBaseHeight = WndConstants.Editor.MinCanvasHeight;
    private WndCanvasItemViewModel? _dragItem;
    private Point _dragStart;
    private WndScreenRect? _dragOriginal;
    private bool _isResizing;
    private CanvasResizeDirection _resizeDirection = CanvasResizeDirection.None;
    private IReadOnlyList<GameInstallation> _installations = [];
    private Dictionary<string, Bitmap> _previewBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, byte[]> _previewPngs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _resolvedStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _schemeOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private WndRuntimeArt _runtimeArt = WndRuntimeArt.Empty;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _thumbnailCts;
    private int _previewGeneration;
    private bool _installationsLoaded;

    /// <summary>
    /// Gets the root tree nodes of the edited document.
    /// </summary>
    public ObservableCollection<WndTreeNodeViewModel> RootNodes { get; } = [];

    /// <summary>
    /// Gets the shared file explorer listing window definition files.
    /// </summary>
    public FileExplorerViewModel FileExplorer => _fileExplorer ??= CreateFileExplorer();

    /// <summary>
    /// Gets the window definition files listed in the explorer.
    /// </summary>
    public ObservableCollection<EditorFileTreeNodeViewModel> Files => FileExplorer.Nodes;

    /// <summary>
    /// Gets or sets the directory listed in the file explorer.
    /// </summary>
    public string? FilesDirectory
    {
        get => FileExplorer.Directory;
        set => FileExplorer.Directory = value;
    }

    /// <summary>
    /// Gets the canvas items rendered from window geometry.
    /// </summary>
    public ObservableCollection<WndCanvasItemViewModel> CanvasItems { get; } = [];

    /// <summary>
    /// Gets the game installations available as asset sources.
    /// </summary>
    public ObservableCollection<GameInstallationOption> AvailableInstallations { get; } = [];

    /// <summary>
    /// Gets the document title with a modification marker.
    /// </summary>
    public override string DocumentTitle
    {
        get
        {
            var name = FilePath == null
                ? Localization.GetString("Tools.WndEditor.Document.Untitled")
                : Path.GetFileName(FilePath);
            return IsModified ? $"*{name}" : name;
        }
    }

    /// <summary>
    /// Gets the canvas width in device-independent pixels.
    /// </summary>
    public double CanvasWidth => _canvasBaseWidth * Zoom;

    /// <summary>
    /// Gets the canvas height in device-independent pixels.
    /// </summary>
    public double CanvasHeight => _canvasBaseHeight * Zoom;

    /// <summary>
    /// Gets the canvas cursor, showing a hand while the pan tool is active.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public Cursor? CanvasCursor => IsPanMode ? HandCursor : null;

    /// <inheritdoc />
    public override double ZoomMax => WndConstants.Editor.MaxZoom;

    /// <summary>
    /// Gets the scroll offset showing content origin, framing the padded canvas on load and zoom reset.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public Vector CanvasContentOffset => new(WndConstants.Editor.CanvasPadding * Zoom, WndConstants.Editor.CanvasPadding * Zoom);

    /// <summary>
    /// Raised when the canvas should reframe on content origin (document opened or zoom reset).
    /// </summary>
    public event EventHandler? CanvasFramingRequested;

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
    /// Gets or sets the selected tree node.
    /// </summary>
    [ObservableProperty]
    private WndTreeNodeViewModel? _selectedNode;

    /// <summary>
    /// Gets or sets the virtual game screen width in game coordinates.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenGuideWidth))]
    [NotifyPropertyChangedFor(nameof(ScreenGuideLabel))]
    private int _virtualScreenWidth = (int)WndConstants.Editor.MinCanvasWidth;

    /// <summary>
    /// Gets or sets the virtual game screen height in game coordinates.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenGuideHeight))]
    [NotifyPropertyChangedFor(nameof(ScreenGuideLabel))]
    private int _virtualScreenHeight = (int)WndConstants.Editor.MinCanvasHeight;

    /// <summary>
    /// Gets the X coordinate of the virtual game screen guide.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound as an instance property in XAML and reads source-generated instance state.")]
    public double ScreenGuideX => WndConstants.Editor.CanvasPadding * Zoom;

    /// <summary>
    /// Gets the Y coordinate of the virtual game screen guide.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound as an instance property in XAML and reads source-generated instance state.")]
    public double ScreenGuideY => WndConstants.Editor.CanvasPadding * Zoom;

    /// <summary>
    /// Gets the width of the virtual game screen guide.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound as an instance property in XAML and reads source-generated instance state.")]
    public double ScreenGuideWidth => VirtualScreenWidth * Zoom;

    /// <summary>
    /// Gets the height of the virtual game screen guide.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound as an instance property in XAML and reads source-generated instance state.")]
    public double ScreenGuideHeight => VirtualScreenHeight * Zoom;

    /// <summary>
    /// Gets the resolution label of the virtual game screen guide.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound as an instance property in XAML and reads source-generated instance state.")]
    public string ScreenGuideLabel => $"{VirtualScreenWidth} × {VirtualScreenHeight}";

    /// <summary>
    /// Gets or sets whether the pan tool is active instead of window selection.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasCursor))]
    private bool _isPanMode;

    /// <summary>
    /// Gets or sets whether engine-hidden windows stay visible on the canvas.
    /// </summary>
    [ObservableProperty]
    private bool _showHiddenWindows;

    /// <summary>
    /// Gets or sets the installation used as the canvas asset source.
    /// </summary>
    [ObservableProperty]
    private GameInstallationOption? _selectedAssetInstallation;

    /// <summary>
    /// Gets or sets the typed editors for the selected window.
    /// </summary>
    [ObservableProperty]
    private WndWindowPropertiesViewModel? _selectedProperties;

    /// <summary>
    /// Gets or sets the window tree filter text.
    /// </summary>
    [ObservableProperty]
    private string _windowsFilter = string.Empty;

    /// <summary>
    /// Gets or sets the active tab in the left sidebar (0 = Windows, 1 = Files, 2 = Assets).
    /// </summary>
    [ObservableProperty]
    private int _leftSidebarTabIndex;

    private bool _hasDocument;

    /// <summary>
    /// Gets or sets the resolved/total asset preview status text.
    /// </summary>
    [ObservableProperty]
    private string _assetStatusText = string.Empty;

    /// <summary>
    /// Gets or sets the missing asset names tooltip, or null when nothing is missing.
    /// </summary>
    [ObservableProperty]
    private string? _assetStatusTooltip;

    /// <summary>
    /// Gets or sets an optional user-linked mod folder to load loose assets from.
    /// </summary>
    [ObservableProperty]
    private string? _linkedModFolder;

    /// <summary>
    /// Gets the collection of user-linked .BIG archives to load assets from.
    /// </summary>
    public ObservableCollection<string> LinkedBigFiles { get; } = [];

    /// <summary>
    /// Gets or sets the summary string of currently linked custom assets.
    /// </summary>
    [ObservableProperty]
    private string _linkedAssetsSummary = string.Empty;

    /// <summary>
    /// Gets or sets the tooltip showing details of linked custom assets.
    /// </summary>
    [ObservableProperty]
    private string? _linkedAssetsTooltip;

    /// <summary>
    /// Gets or sets a value indicating whether custom assets are currently linked.
    /// </summary>
    [ObservableProperty]
    private bool _hasLinkedAssets;

    /// <summary>
    /// Gets the mapped image names referenced by the document that resolved to no art.
    /// </summary>
    public ObservableCollection<string> MissingImageNames { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether any referenced image is missing art.
    /// </summary>
    [ObservableProperty]
    private bool _hasMissingImages;

    /// <summary>
    /// Gets or sets the known mapped image names offered by the art picker.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _knownImageNames = [];

    /// <summary>
    /// Gets or sets the known art library status text.
    /// </summary>
    [ObservableProperty]
    private string _knownImagesStatusText = string.Empty;

    /// <summary>
    /// Gets or sets the art library search filter.
    /// </summary>
    [ObservableProperty]
    private string _libraryFilter = string.Empty;

    /// <summary>
    /// Gets or sets the filtered art library rows shown in the Assets tab.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _filteredKnownImageNames = [];

    /// <summary>
    /// Gets or sets the filtered art library items displayed in the Assets tab.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<WndArtItemViewModel> _filteredArtItems = [];

    /// <summary>
    /// Gets or sets the art library truncation hint, or empty when everything fits.
    /// </summary>
    [ObservableProperty]
    private string _libraryStatusText = string.Empty;

    /// <summary>
    /// Gets or sets the active asset root display text (installation name and game).
    /// </summary>
    [ObservableProperty]
    private string _assetRootDisplayText = string.Empty;

    /// <summary>
    /// Gets or sets the active asset root tooltip (full game root path).
    /// </summary>
    [ObservableProperty]
    private string? _assetRootDisplayTooltip;

    /// <summary>
    /// Gets or sets a value indicating whether a document is open.
    /// </summary>
    public override bool HasDocument
    {
        get => _hasDocument;
        protected set => SetProperty(ref _hasDocument, value);
    }

    /// <summary>
    /// Gets a value indicating whether undo is available.
    /// </summary>
    public override bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether redo is available.
    /// </summary>
    public override bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the selected window can be copied.
    /// </summary>
    public override bool CanCopy => _document is not null && SelectedNode is not null;

    /// <summary>
    /// Gets a value indicating whether the selected window can be cut.
    /// </summary>
    public override bool CanCut => _document is not null && SelectedNode is not null;

    /// <summary>
    /// Gets a value indicating whether the clipboard can be pasted.
    /// </summary>
    public override bool CanPaste => true;

    /// <summary>
    /// Gets a value indicating whether the selected window can be duplicated.
    /// </summary>
    public override bool CanDuplicate => _document is not null && SelectedNode is not null;

    /// <summary>
    /// Gets a value indicating whether the selected window can be deleted.
    /// </summary>
    public override bool CanDelete => _document is not null && SelectedNode is not null;

    /// <summary>
    /// Gets a value indicating whether the document can be saved.
    /// </summary>
    public override bool CanSave => HasDocument;

    /// <summary>
    /// Gets a value indicating whether the document can be saved under a new path.
    /// </summary>
    public override bool CanSaveAs => HasDocument;

    /// <summary>
    /// Opens a window definition file, asking to discard unsaved changes first.
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

        filePath = NormalizeSourceFilePath(filePath);

        var result = await wndDocumentService.ParseFileAsync(filePath, cancellationToken);
        if (!result.Success || result.Data == null)
        {
            Notifications.ShowError(
                Localization.GetString("Tools.WndEditor.Open.FailureTitle"),
                Localization.GetString("Tools.WndEditor.Open.FailureMessage", result.FirstError ?? filePath),
                NotificationDurations.Long);
            return false;
        }

        await InvokeOnUIThreadAsync(() => AdoptDocument(result.Data, filePath)).ConfigureAwait(false);
        await EnsureInstallationsLoadedAsync(cancellationToken).ConfigureAwait(false);
        RefreshAssetPreviews();
        telemetryService?.TrackEvent(TelemetryConstants.Events.WndDocumentOpened, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.FilePath] = TelemetryConstants.WndEditor.AnonymousDocumentName,
            [TelemetryConstants.Properties.WindowCount] = result.Data.Windows.Count,
        });
        logger.LogInformation("Opened window definition file {Path}", filePath);
        return true;
    }

    /// <summary>
    /// Opens a folder in the file explorer and optionally loads the first window definition file.
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

        FilesDirectory = folderPath;
        return await AdoptExplorerDirectoryAsync(folderPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a document from text, replacing the open document.
    /// </summary>
    /// <param name="content">The raw file content.</param>
    /// <param name="filePath">The optional source path recorded on the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the content parsed successfully.</returns>
    public async Task<bool> LoadFromTextAsync(string content, string? filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var result = wndDocumentService.ParseText(content, filePath);
        if (!result.Success || result.Data == null)
        {
            Notifications.ShowError(
                Localization.GetString("Tools.WndEditor.Open.FailureTitle"),
                Localization.GetString("Tools.WndEditor.Open.FailureMessage", result.FirstError ?? string.Empty),
                NotificationDurations.Long);
            return false;
        }

        await InvokeOnUIThreadAsync(() => AdoptDocument(result.Data, filePath)).ConfigureAwait(false);
        await EnsureInstallationsLoadedAsync(cancellationToken).ConfigureAwait(false);
        RefreshAssetPreviews();
        return true;
    }

    /// <summary>
    /// Applies dropped texture file(s) to the window at the specified canvas position or the currently selected window.
    /// Imports the texture if not already present in the mod project.
    /// </summary>
    /// <param name="filePaths">The dropped file paths.</param>
    /// <param name="canvasPosition">Optional canvas coordinate where drop occurred.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when at least one texture was applied.</returns>
    public async Task<bool> ApplyDroppedFilesAsync(
        IReadOnlyList<string> filePaths,
        Point? canvasPosition = null,
        CancellationToken cancellationToken = default)
    {
        if (filePaths == null || filePaths.Count == 0)
        {
            return false;
        }

        var targetWindow = ResolveTargetWindowAt(canvasPosition) ?? SelectedNode?.Window;
        if (targetWindow == null)
        {
            Notifications.ShowWarning(
                Localization.GetString(NoSelectionTitleKey),
                Localization.GetString(NoSelectionMessageKey),
                NotificationDurations.Medium);
            return false;
        }

        var textureExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".tga", ".dds", ".png", ".jpg", ".jpeg", ".bmp",
        };

        var validPaths = filePaths.Where(p => textureExtensions.Contains(Path.GetExtension(p))).ToList();
        if (validPaths.Count == 0)
        {
            return false;
        }

        var roots = SelectedAssetInstallation != null ? ResolveAssetRoots(SelectedAssetInstallation) : null;
        var projectDirectory = ResolveImportProjectDirectory(LinkedModFolder, roots, FilePath, FilesDirectory);

        var mappedNameToApply = await ResolveMappedNameForDroppedPathsAsync(validPaths, projectDirectory, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(mappedNameToApply))
        {
            return false;
        }

        await InvokeOnUIThreadAsync(() =>
        {
            SelectWindow(targetWindow);
            ApplyImageToSelectedWindow(mappedNameToApply);
            assetService.InvalidateCache();
            RefreshAssetPreviews();
        }).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Applies a mapped image name dropped onto the canvas.
    /// </summary>
    /// <param name="imageName">The mapped image name.</param>
    /// <param name="canvasPosition">Optional canvas coordinate where drop occurred.</param>
    public void ApplyDroppedImageName(string imageName, Point? canvasPosition = null)
    {
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return;
        }

        var targetWindow = ResolveTargetWindowAt(canvasPosition) ?? SelectedNode?.Window;
        if (targetWindow == null)
        {
            Notifications.ShowWarning(
                Localization.GetString(NoSelectionTitleKey),
                Localization.GetString(NoSelectionMessageKey),
                NotificationDurations.Medium);
            return;
        }

        SelectWindow(targetWindow);
        ApplyImageToSelectedWindow(imageName.Trim());
    }

    /// <summary>
    /// Displays a localized drop failure notification.
    /// </summary>
    /// <param name="exception">Optional exception that caused the drop failure.</param>
    public void NotifyDropError(Exception? exception = null)
    {
        if (exception != null)
        {
            logger.LogError(exception, "Failed to process dropped item in WND editor");
        }

        var title = Localization.GetString("Tools.WndEditor.Drop.ErrorTitle");
        var message = exception?.Message is { Length: > 0 } msg
            ? Localization.GetString("Tools.WndEditor.Drop.ErrorMessage", msg)
            : Localization.GetString("Tools.WndEditor.Drop.ErrorMessage", string.Empty);
        Notifications.ShowError(title, message);
    }

    /// <summary>
    /// Pastes an asset from the system clipboard into the currently selected window.
    /// Supports raw image data (copied screenshot, browser image, bitmap/DIB), copied image/texture files, or copied mapped art names.
    /// </summary>
    /// <param name="clipboard">Optional clipboard instance. If null, the top-level window clipboard is used.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if an asset was successfully pasted and applied; otherwise false.</returns>
    public async Task<bool> PasteAssetFromClipboardAsync(
        IClipboard? clipboard = null,
        CancellationToken cancellationToken = default)
    {
        var targetWindow = SelectedNode?.Window;
        if (targetWindow == null)
        {
            Notifications.ShowWarning(
                Localization.GetString(NoSelectionTitleKey),
                Localization.GetString(NoSelectionMessageKey),
                NotificationDurations.Medium);
            return false;
        }

        if (IsTextInputFocused())
        {
            return false;
        }

        var topLevel = GetTopLevel();
        clipboard ??= topLevel?.Clipboard;
        if (clipboard == null)
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.WndEditor.Paste.UnavailableTitle"),
                Localization.GetString("Tools.WndEditor.Paste.UnavailableMessage"),
                NotificationDurations.Medium);
            return false;
        }

        // 1. Check for copied files (DataFormats.Files or "FileNames")
        var filePaths = await ExtractClipboardFilesAsync(clipboard).ConfigureAwait(false);
        if (filePaths.Count > 0)
        {
            return await ApplyDroppedFilesAsync(filePaths, canvasPosition: null, cancellationToken).ConfigureAwait(false);
        }

        // 2. Check for copied raw image bytes (PNG, JPEG, BMP, DIB, Bitmap)
        var imageBytes = await ExtractClipboardImageBytesAsync(clipboard).ConfigureAwait(false);
        if (imageBytes != null && imageBytes.Length > 0)
        {
            return await ApplyClipboardImageBytesAsync(imageBytes, targetWindow, cancellationToken).ConfigureAwait(false);
        }

        // 3. Check for copied text (file path or existing mapped image name)
        var text = await clipboard.GetTextAsync().ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(text))
        {
            var trimmedText = text.Trim().Trim('"', '\x27');
            if (File.Exists(trimmedText))
            {
                return await ApplyDroppedFilesAsync([trimmedText], canvasPosition: null, cancellationToken).ConfigureAwait(false);
            }

            if (KnownImageNames.Contains(trimmedText, StringComparer.OrdinalIgnoreCase))
            {
                await InvokeOnUIThreadAsync(() => ApplyDroppedImageName(trimmedText)).ConfigureAwait(false);
                return true;
            }
        }

        Notifications.ShowInfo(
            Localization.GetString("Tools.WndEditor.Paste.EmptyTitle"),
            Localization.GetString("Tools.WndEditor.Paste.EmptyMessage"),
            NotificationDurations.Medium);
        return false;
    }

    /// <summary>
    /// Selects the tree node backing a canvas item.
    /// </summary>
    /// <param name="item">The canvas item to select, or null to clear selection.</param>
    public void SelectCanvasItem(WndCanvasItemViewModel? item)
    {
        SelectWindow(item?.Window);
    }

    /// <summary>
    /// Starts dragging a canvas item.
    /// </summary>
    /// <param name="item">The dragged item.</param>
    /// <param name="canvasPoint">The pointer position in canvas coordinates.</param>
    public void BeginCanvasDrag(WndCanvasItemViewModel? item, Point canvasPoint)
    {
        _dragItem = null;
        _dragOriginal = null;
        _isResizing = false;
        _resizeDirection = CanvasResizeDirection.None;
        if (item == null || !item.Window.TryGetScreenRect(out var rect) || rect == null)
        {
            return;
        }

        SelectWindow(item.Window);
        _dragItem = item;
        _dragStart = canvasPoint;
        _dragOriginal = rect;
    }

    /// <summary>
    /// Starts resizing a canvas item in the specified direction.
    /// </summary>
    /// <param name="item">The resized item.</param>
    /// <param name="direction">The resize handle direction.</param>
    /// <param name="canvasPoint">The pointer position in canvas coordinates.</param>
    public void BeginCanvasResize(WndCanvasItemViewModel? item, CanvasResizeDirection direction, Point canvasPoint)
    {
        _dragItem = null;
        _dragOriginal = null;
        _isResizing = false;
        _resizeDirection = CanvasResizeDirection.None;

        if (item == null || direction == CanvasResizeDirection.None || !item.Window.TryGetScreenRect(out var rect) || rect == null)
        {
            return;
        }

        SelectWindow(item.Window);
        _dragItem = item;
        _dragStart = canvasPoint;
        _dragOriginal = rect;
        _isResizing = true;
        _resizeDirection = direction;
    }

    /// <summary>
    /// Moves or resizes the dragged canvas item.
    /// </summary>
    /// <param name="canvasPoint">The pointer position in canvas coordinates.</param>
    public void UpdateCanvasDrag(Point canvasPoint)
    {
        if (_dragItem == null || _dragOriginal == null)
        {
            return;
        }

        if (_isResizing)
        {
            UpdateCanvasResize(canvasPoint);
            return;
        }

        var deltaX = (int)Math.Round((canvasPoint.X - _dragStart.X) / Zoom);
        var deltaY = (int)Math.Round((canvasPoint.Y - _dragStart.Y) / Zoom);
        var moved = new WndScreenRect(
            _dragOriginal.UpperLeftX + deltaX,
            _dragOriginal.UpperLeftY + deltaY,
            _dragOriginal.BottomRightX + deltaX,
            _dragOriginal.BottomRightY + deltaY,
            _dragOriginal.CreationWidth,
            _dragOriginal.CreationHeight);
        _dragItem.Window.SetProperty(WndConstants.PropertyKeys.ScreenRect, moved.ToString());
        _dragItem.X = (moved.UpperLeftX + WndConstants.Editor.CanvasPadding) * Zoom;
        _dragItem.Y = (moved.UpperLeftY + WndConstants.Editor.CanvasPadding) * Zoom;
        _dragItem.Width = Math.Max(0, moved.Width) * Zoom;
        _dragItem.Height = Math.Max(0, moved.Height) * Zoom;
    }

    /// <summary>
    /// Finishes dragging or resizing a canvas item and records the edit as an undoable action.
    /// </summary>
    public void EndCanvasDrag()
    {
        var item = _dragItem;
        var original = _dragOriginal;
        var wasResizing = _isResizing;
        _dragItem = null;
        _dragOriginal = null;
        _isResizing = false;
        _resizeDirection = CanvasResizeDirection.None;

        if (item == null || original == null)
        {
            return;
        }

        if (!item.Window.TryGetScreenRect(out var changed) || changed == null || changed.Equals(original))
        {
            SyncAfterEdit(item.Window);
            return;
        }

        var finalRect = changed;
        var actionName = wasResizing
            ? Localization.GetString("Tools.WndEditor.History.ResizeWindow")
            : Localization.GetString("Tools.WndEditor.History.MoveWindow");

        PushUndo(new WndEditAction(
            actionName,
            () =>
            {
                item.Window.SetProperty(WndConstants.PropertyKeys.ScreenRect, finalRect.ToString());
                SyncAfterEdit(item.Window);
            },
            () =>
            {
                item.Window.SetProperty(WndConstants.PropertyKeys.ScreenRect, original.ToString());
                SyncAfterEdit(item.Window);
            }));
        SyncAfterEdit(item.Window);
    }

    /// <summary>
    /// Parses a ControlBarScheme INI file and populates the given dictionary with scheme image overrides.
    /// </summary>
    /// <param name="iniText">The INI file content.</param>
    /// <param name="result">The dictionary to populate with image overrides.</param>
    /// <param name="preferredScheme">The optional preferred scheme name to match.</param>
    internal static void ParseControlBarSchemeIni(string iniText, Dictionary<string, string> result, string? preferredScheme = WndConstants.ControlBarScheme.AmericaSchemeName)
    {
        WndControlBarSchemeParser.Parse(iniText, result, preferredScheme);
    }

    /// <summary>
    /// If the path points to a file within a ModBuilder build artifact directory (e.g. .Build/raw_bundle_items/...),
    /// maps it back to the corresponding project source file under GameFilesEdited.
    /// </summary>
    /// <param name="filePath">The file path to normalize.</param>
    /// <returns>The normalized source file path.</returns>
    internal static string NormalizeSourceFilePath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return filePath;
        }

        try
        {
            var normalized = filePath.Replace('\\', '/');
            return TryMapRawBundleItemPath(filePath, normalized)
                ?? TryMapBuildPath(filePath, normalized)
                ?? filePath;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            // Ignore path inspection errors and return original path
            return filePath;
        }
    }

    /// <summary>
    /// Attempts to map a raw bundle items directory file path back to its source location.
    /// </summary>
    /// <param name="filePath">The original file path.</param>
    /// <param name="normalized">The path with slashes normalized.</param>
    /// <returns>The candidate source path if found; otherwise, null.</returns>
    internal static string? TryMapRawBundleItemPath(string filePath, string normalized)
    {
        var rawMarker = "/" + ModBuilderConstants.RawBundleItemsSubdir + "/";
        var rawIndex = normalized.IndexOf(rawMarker, StringComparison.OrdinalIgnoreCase);
        if (rawIndex < 0)
        {
            return null;
        }

        var beforeMarker = filePath[..rawIndex];
        var afterMarker = filePath[(rawIndex + rawMarker.Length)..];

        var projectDir = Path.GetDirectoryName(beforeMarker);
        if (string.IsNullOrEmpty(projectDir))
        {
            return null;
        }

        var candidateSource = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, afterMarker);
        return File.Exists(candidateSource) ? candidateSource : null;
    }

    /// <summary>
    /// Attempts to map a default build directory file path back to its source location.
    /// </summary>
    /// <param name="filePath">The original file path.</param>
    /// <param name="normalized">The path with slashes normalized.</param>
    /// <returns>The candidate source path if found; otherwise, null.</returns>
    internal static string? TryMapBuildPath(string filePath, string normalized)
    {
        var buildMarker = "/" + ModBuilderConstants.DefaultBuildDir + "/";
        var buildIndex = normalized.IndexOf(buildMarker, StringComparison.OrdinalIgnoreCase);
        if (buildIndex < 0)
        {
            return null;
        }

        var projectDir = filePath[..buildIndex];
        var afterBuild = filePath[(buildIndex + buildMarker.Length)..];
        if (string.IsNullOrEmpty(projectDir))
        {
            return null;
        }

        var candidateSource = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, afterBuild);
        return File.Exists(candidateSource) ? candidateSource : null;
    }

    /// <summary>
    /// Builds runtime presentation facts for shell-managed windows.
    /// </summary>
    /// <param name="document">The layout document.</param>
    /// <param name="medals">The resolved medallions, if any.</param>
    /// <returns>The runtime presentation facts.</returns>
    internal static WndRuntimeArt BuildRuntimeArt(WndDocument document, ChallengeMedals? medals)
    {
        var medalImages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var windowName in EnumerateWindows(document.Windows)
                     .Select(w => w.Name)
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Cast<string>())
        {
            var decorated = WndDecoratedName.Parse(windowName);
            if (IsMapStartMarker(decorated.ShortName))
            {
                // Map start markers are repositioned by the shell once a map loads
                // (WOLGameSetupMenu positionStartSpots); at rest they sit at stacked
                // file positions, so the editor hides them like other runtime windows.
                hidden.Add(windowName);
                continue;
            }

            if (string.Equals(decorated.FileName, WndConstants.Challenge.FileName, StringComparison.OrdinalIgnoreCase))
            {
                ApplyChallengeRuntimeArt(windowName, decorated.ShortName, medals, medalImages, hidden);
            }
            else if (string.Equals(decorated.FileName, WndConstants.ShellRuntime.MainMenuFileName, StringComparison.OrdinalIgnoreCase))
            {
                ApplyMainMenuRuntimeArt(windowName, decorated.ShortName, hidden);
            }
        }

        return new WndRuntimeArt(medalImages, hidden);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed || !disposing)
        {
            return;
        }

        lock (_previewSync)
        {
            CancelAndDisposeCts(_previewCts);
            _previewCts = null;
        }

        lock (_thumbnailSync)
        {
            CancelAndDisposeCts(_thumbnailCts);
            _thumbnailCts = null;
        }

        ClearComposedBitmaps();
        foreach (var bitmap in _previewBitmaps.Values)
        {
            bitmap.Dispose();
        }

        _previewBitmaps.Clear();

        foreach (var bitmap in _thumbnailBitmaps.Values)
        {
            bitmap.Dispose();
        }

        _thumbnailBitmaps.Clear();
        base.Dispose(disposing);
    }

    /// <inheritdoc />
    protected override bool HasUnsavedChanges => IsModified;

    /// <inheritdoc />
    protected override string UnsavedChangesTitleKey => "Tools.WndEditor.UnsavedChanges.Title";

    /// <inheritdoc />
    protected override string UnsavedChangesMessageKey => "Tools.WndEditor.UnsavedChanges.Message";

    /// <inheritdoc />
    protected override string UnsavedChangesDiscardKey => "Tools.WndEditor.UnsavedChanges.Discard";

    /// <inheritdoc />
    protected override string UnsavedChangesCancelKey => "Tools.WndEditor.UnsavedChanges.Cancel";

    /// <inheritdoc />
    protected override void OnZoomChanged()
    {
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(CanvasContentOffset));
        OnPropertyChanged(nameof(ScreenGuideX));
        OnPropertyChanged(nameof(ScreenGuideY));
        OnPropertyChanged(nameof(ScreenGuideWidth));
        OnPropertyChanged(nameof(ScreenGuideHeight));
        RebuildCanvas();
    }

    /// <inheritdoc />
    protected override void OnZoomIn()
    {
        Zoom = Math.Min(Zoom * WndConstants.Editor.ZoomStepFactor, WndConstants.Editor.MaxZoom);
    }

    /// <inheritdoc />
    protected override void OnZoomOut()
    {
        Zoom = Math.Max(Zoom / WndConstants.Editor.ZoomStepFactor, WndConstants.Editor.MinZoom);
    }

    /// <inheritdoc />
    protected override void OnResetZoom()
    {
        Zoom = WndConstants.Editor.DefaultZoom;
        CanvasFramingRequested?.Invoke(this, EventArgs.Empty);
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
        _historyVersion--;
        IsModified = _historyVersion != _savedHistoryVersion;
        RefreshUndoCommands();
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
        PushUndoStack(action);
        _historyVersion++;
        IsModified = _historyVersion != _savedHistoryVersion;
        RefreshUndoCommands();
    }

    /// <inheritdoc />
    protected override void OnCopy()
    {
        if (IsTextInputFocused() || _document is null || SelectedNode is null)
        {
            return;
        }

        _copiedWindow = CloneWindow(SelectedNode.Window);
        _copySourceWindow = SelectedNode.Window;
        _lastPastedClone = null;
        _isCutOperation = false;
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnCut()
    {
        if (IsTextInputFocused() || _document is null || SelectedNode is null)
        {
            return;
        }

        _copiedWindow = CloneWindow(SelectedNode.Window);
        _copySourceWindow = null;
        _lastPastedClone = null;
        _isCutOperation = true;
        RemoveWindowWithUndo(SelectedNode, "Tools.WndEditor.History.CutWindow");
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override async Task OnPasteAsync(CancellationToken cancellationToken)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        if (_copiedWindow is not null && _document is not null)
        {
            PasteCopiedWindow();
            return;
        }

        await PasteAssetFromClipboardAsync(cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnDuplicate()
    {
        if (IsTextInputFocused() || _document is null || SelectedNode is null)
        {
            return;
        }

        var node = SelectedNode;
        var siblings = node.Parent is null ? _document.Windows : node.Parent.Window.Children;
        var index = siblings.IndexOf(node.Window);
        var clone = CloneWindow(node.Window);
        OffsetWindowRect(clone);
        siblings.Insert(Math.Min(index + 1, siblings.Count), clone);
        PushUndo(new WndEditAction(
            Localization.GetString("Tools.WndEditor.History.DuplicateWindow"),
            () =>
            {
                siblings.Insert(Math.Min(index + 1, siblings.Count), clone);
                RebuildAll();
                SelectWindow(clone);
            },
            () =>
            {
                siblings.Remove(clone);
                RebuildAll();
                SelectWindow(node.Window);
            }));
        RebuildAll();
        SelectWindow(clone);
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnDelete()
    {
        if (IsTextInputFocused() || _document is null || SelectedNode is null)
        {
            return;
        }

        RemoveWindowWithUndo(SelectedNode, "Tools.WndEditor.History.DeleteWindow");
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override async Task OnNewDocumentAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        var document = new WndDocument();
        document.Windows.Add(CreateDefaultWindow());
        await InvokeOnUIThreadAsync(() =>
        {
            AdoptDocument(document, null);
            IsModified = true;
        }).ConfigureAwait(false);
        await EnsureInstallationsLoadedAsync(cancellationToken).ConfigureAwait(false);
        RefreshAssetPreviews();
        logger.LogInformation("Created new window definition document");
    }

    /// <inheritdoc />
    protected override async Task OnOpenFolderAsync(CancellationToken cancellationToken)
    {
        var localPath = await PickFolderAsync(cancellationToken);
        if (!string.IsNullOrEmpty(localPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await OpenFolderAsync(localPath, cancellationToken);
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
            Title = Localization.GetString("Tools.WndEditor.FileDialog.OpenTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localization.GetString("Tools.WndEditor.FileDialog.FilterName"))
                {
                    Patterns = [ModBuilderConstants.FileNames.WndSearchPattern],
                },
            ],
        });
        if (files.Count > 0)
        {
            var localPath = files[0].TryGetLocalPath();
            if (!string.IsNullOrEmpty(localPath))
            {
                await OpenFileAsync(localPath, cancellationToken);
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

        SelectedProperties?.FlushPendingEdits();

        if (string.IsNullOrEmpty(FilePath))
        {
            await OnSaveAsAsync(cancellationToken);
            return;
        }

        var normalizedPath = NormalizeSourceFilePath(FilePath);
        var oldPath = FilePath;
        if (!string.Equals(normalizedPath, oldPath, StringComparison.OrdinalIgnoreCase))
        {
            FilePath = normalizedPath;
            SyncFilesDirectory(normalizedPath);
        }

        var saved = await WriteDocumentToFileAsync(FilePath, cancellationToken);
        if (saved && !string.Equals(normalizedPath, oldPath, StringComparison.OrdinalIgnoreCase) && File.Exists(oldPath))
        {
            try
            {
                await WriteDocumentToFileAsync(oldPath, cancellationToken, showFeedback: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Failed to mirror saved file to transient build path {Path}", oldPath);
            }
        }
    }

    /// <inheritdoc />
    protected override async Task OnSaveAsAsync(CancellationToken cancellationToken)
    {
        if (_document is null)
        {
            return;
        }

        SelectedProperties?.FlushPendingEdits();

        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localization.GetString("Tools.WndEditor.FileDialog.SaveTitle"),
            SuggestedFileName = FilePath is null ? null : Path.GetFileName(FilePath),
            FileTypeChoices =
            [
                new FilePickerFileType(Localization.GetString("Tools.WndEditor.FileDialog.FilterName"))
                {
                    Patterns = [ModBuilderConstants.FileNames.WndSearchPattern],
                },
            ],
        });
        var localPath = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            var saved = await WriteDocumentToFileAsync(localPath, cancellationToken);
            if (saved)
            {
                FilePath = localPath;
                SyncFilesDirectory(localPath);
            }
        }
    }

    private static bool IsMapStartMarker(string shortName)
    {
        return shortName.StartsWith(WndConstants.ShellRuntime.MapStartPositionPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyChallengeRuntimeArt(
        string name,
        string shortName,
        ChallengeMedals? medals,
        Dictionary<string, string> medalImages,
        HashSet<string> hidden)
    {
        // ChallengeMenuInit hides the biography panel and play button until a
        // general is selected; locked personas start hidden the same way.
        if (string.Equals(shortName, WndConstants.Challenge.BioParentShortName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortName, WndConstants.Challenge.ButtonPlayShortName, StringComparison.OrdinalIgnoreCase))
        {
            hidden.Add(name);
            return;
        }

        if (medals != null && TryParseGeneralPosition(shortName, out var position))
        {
            if (medals.HiddenPositions.Contains(position))
            {
                hidden.Add(name);
            }
            else if (medals.MedalsByPosition.TryGetValue(position, out var medal))
            {
                medalImages[name] = medal;
            }
        }
    }

    private static void ApplyMainMenuRuntimeArt(string name, string shortName, HashSet<string> hidden)
    {
        // MainMenu initialHide hides the faction flyouts, and
        // showSelectiveButtons(SHOW_NONE) hides every faction quick-load button
        // until a faction is picked.
        if (shortName.StartsWith(WndConstants.ShellRuntime.WinFactionPrefix, StringComparison.OrdinalIgnoreCase)
            || IsMainMenuFactionButton(shortName))
        {
            hidden.Add(name);
        }
    }

    private static bool IsMainMenuFactionButton(string shortName)
    {
        return string.Equals(shortName, WndConstants.ShellRuntime.ButtonUsaRecentSave, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortName, WndConstants.ShellRuntime.ButtonUsaLoadGame, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortName, WndConstants.ShellRuntime.ButtonGlaRecentSave, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortName, WndConstants.ShellRuntime.ButtonGlaLoadGame, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortName, WndConstants.ShellRuntime.ButtonChinaRecentSave, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shortName, WndConstants.ShellRuntime.ButtonChinaLoadGame, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateCanvasResize(Point canvasPoint)
    {
        if (_dragItem == null || _dragOriginal == null)
        {
            return;
        }

        var deltaX = (int)Math.Round((canvasPoint.X - _dragStart.X) / Zoom);
        var deltaY = (int)Math.Round((canvasPoint.Y - _dragStart.Y) / Zoom);

        var (left, top, right, bottom) = CanvasResizeHelper.Resize(
            new CanvasResizeEdges(
                _dragOriginal.UpperLeftX,
                _dragOriginal.UpperLeftY,
                _dragOriginal.BottomRightX,
                _dragOriginal.BottomRightY),
            _resizeDirection,
            deltaX,
            deltaY,
            WndConstants.Editor.MinResizeDimension);

        var resized = new WndScreenRect(
            left,
            top,
            right,
            bottom,
            _dragOriginal.CreationWidth,
            _dragOriginal.CreationHeight);
        _dragItem.Window.SetProperty(WndConstants.PropertyKeys.ScreenRect, resized.ToString());
        _dragItem.X = (resized.UpperLeftX + WndConstants.Editor.CanvasPadding) * Zoom;
        _dragItem.Y = (resized.UpperLeftY + WndConstants.Editor.CanvasPadding) * Zoom;
        _dragItem.Width = Math.Max(0, resized.Width) * Zoom;
        _dragItem.Height = Math.Max(0, resized.Height) * Zoom;
    }

    private static WndWindow CreateDefaultWindow()
    {
        var window = new WndWindow
        {
            ControlTypeName = WndConstants.Editor.DefaultNewWindowType,
        };
        window.SetProperty(WndConstants.PropertyKeys.WindowType, WndConstants.Editor.DefaultNewWindowType);
        window.SetProperty(
            WndConstants.PropertyKeys.ScreenRect,
            new WndScreenRect(
                0,
                0,
                WndConstants.Editor.DefaultNewWindowWidth,
                WndConstants.Editor.DefaultNewWindowHeight,
                (int)WndConstants.Editor.MinCanvasWidth,
                (int)WndConstants.Editor.MinCanvasHeight).ToString());
        window.SetProperty(WndConstants.PropertyKeys.Name, $"\"{WndConstants.Editor.DefaultNewWindowName}\"");
        return window;
    }

    private static WndTreeNodeViewModel? FindNodeRecursive(WndTreeNodeViewModel node, Guid id)
    {
        if (node.Window.Id == id)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindNodeRecursive(child, id);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void ExpandAncestors(WndTreeNodeViewModel node)
    {
        var parent = node.Parent;
        while (parent != null)
        {
            parent.IsExpanded = true;
            parent = parent.Parent;
        }
    }

    private static async Task InvokeOnUIThreadAsync(Action action)
    {
        if (Application.Current == null || Dispatcher.UIThread.CheckAccess())
        {
            action();
            await Task.CompletedTask.ConfigureAwait(false);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(action);
        }
    }

    private static IEnumerable<WndWindow> EnumerateWindows(IEnumerable<WndWindow> windows)
    {
        foreach (var window in windows)
        {
            yield return window;
            foreach (var child in EnumerateWindows(window.Children))
            {
                yield return child;
            }
        }
    }

    private static void SetNodesExpanded(IEnumerable<WndTreeNodeViewModel> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            node.IsExpanded = expanded;
            SetNodesExpanded(node.Children, expanded);
        }
    }

    private static bool MatchesWindowsFilter(WndWindow window, string filter)
    {
        if (window.ControlTypeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var name = WndDecoratedName.Parse(window.GetProperty(WndConstants.PropertyKeys.Name)).ShortName;
        return name.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SubtreeMatchesFilter(WndWindow window, string filter)
    {
        return MatchesWindowsFilter(window, filter) || window.Children.Any(child => SubtreeMatchesFilter(child, filter));
    }

    private static IReadOnlyCollection<string> CollectPreviewImageNames(
        WndDocument document,
        IReadOnlyDictionary<string, string>? overrides = null,
        WndRuntimeArt? runtimeArt = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in EnumerateWindows(document.Windows))
        {
            foreach (var name in WndPreviewPlanner.Plan(window, overrides, runtimeArt).ReferencedImages)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyCollection<string> CollectPreviewLabels(
        WndDocument document,
        IReadOnlyDictionary<string, string>? overrides = null,
        WndRuntimeArt? runtimeArt = null)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in EnumerateWindows(document.Windows))
        {
            if (window.ControlType == WndControlType.EntryField)
            {
                continue;
            }

            var text = WndPreviewPlanner.Plan(window, overrides, runtimeArt).Text;
            if (!string.IsNullOrWhiteSpace(text))
            {
                labels.Add(text);
            }
        }

        return labels;
    }

    private static IBrush? ToOverlayBrush(WndRgbaColor? color)
    {
        if (color == null || color.Alpha <= 0)
        {
            return null;
        }

        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Clamp(color.Alpha, 0, 255),
            (byte)Math.Clamp(color.Red, 0, 255),
            (byte)Math.Clamp(color.Green, 0, 255),
            (byte)Math.Clamp(color.Blue, 0, 255)));
    }

    private static void ApplyProperties(WndWindow window, IReadOnlyList<WndProperty> properties)
    {
        window.Properties.Clear();
        window.Properties.AddRange(properties);
        SyncWindowType(window);
    }

    private static void SyncWindowType(WndWindow window)
    {
        var declared = window.GetProperty(WndConstants.PropertyKeys.WindowType);
        if (declared != null)
        {
            window.ControlTypeName = declared.Trim();
        }
    }

    /// <summary>
    /// Pastes an asset from the clipboard into the currently selected window.
    /// </summary>
    [RelayCommand]
    private async Task PasteAssetFromClipboardAsync(CancellationToken cancellationToken)
    {
        await PasteAssetFromClipboardAsync(clipboard: null, cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ExtractClipboardFilesAsync(IClipboard clipboard)
    {
        try
        {
            var formats = await clipboard.GetFormatsAsync().ConfigureAwait(false);
            if (formats == null)
            {
                return [];
            }

            var fileFormat = formats.FirstOrDefault(f =>
                string.Equals(f, DataFormats.Files, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f, "FileNames", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(f, "FileName", StringComparison.OrdinalIgnoreCase));

            if (fileFormat == null)
            {
                return [];
            }

            var data = await clipboard.GetDataAsync(fileFormat).ConfigureAwait(false);
            if (data == null)
            {
                return [];
            }

            var paths = new List<string>();
            if (data is IEnumerable<IStorageItem> storageItems)
            {
                foreach (var item in storageItems)
                {
                    var local = item.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(local))
                    {
                        paths.Add(local);
                    }
                }
            }
            else if (data is IEnumerable<string> stringEnumerable)
            {
                paths.AddRange(stringEnumerable.Where(p => !string.IsNullOrWhiteSpace(p)));
            }
            else if (data is string singleString && !string.IsNullOrWhiteSpace(singleString))
            {
                paths.Add(singleString);
            }

            return paths;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to extract clipboard files");
            return [];
        }
    }

    private async Task<byte[]?> ExtractClipboardImageBytesAsync(IClipboard clipboard)
    {
        try
        {
            var formats = await clipboard.GetFormatsAsync().ConfigureAwait(false);
            if (formats == null)
            {
                return null;
            }

            var preferredBytes = await TryExtractPreferredImageBytesAsync(clipboard, formats).ConfigureAwait(false);
            if (preferredBytes != null)
            {
                return preferredBytes;
            }

            foreach (var fmt in formats)
            {
                if (fmt.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    var bytes = await TryExtractBytesForFormatAsync(clipboard, fmt).ConfigureAwait(false);
                    if (bytes != null)
                    {
                        return bytes;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to extract clipboard image bytes");
            return null;
        }
    }

    private static async Task<byte[]?> TryExtractPreferredImageBytesAsync(IClipboard clipboard, string[] formats)
    {
        var preferredFormats = new[]
        {
            "image/png", "PNG", "image/jpeg", "image/bmp", "DeviceIndependentBitmap", "CF_DIB",
        };

        foreach (var pref in preferredFormats)
        {
            var matchingFormat = formats.FirstOrDefault(f => string.Equals(f, pref, StringComparison.OrdinalIgnoreCase));
            if (matchingFormat != null)
            {
                var bytes = await TryExtractBytesForFormatAsync(clipboard, matchingFormat).ConfigureAwait(false);
                if (bytes != null)
                {
                    return bytes;
                }
            }
        }

        return null;
    }

    private static async Task<byte[]?> TryExtractBytesForFormatAsync(IClipboard clipboard, string format)
    {
        var data = await clipboard.GetDataAsync(format).ConfigureAwait(false);
        var bytes = ExtractBytesFromData(data);
        return bytes is { Length: > 0 } ? bytes : null;
    }

    private static byte[]? ExtractBytesFromData(object? data)
    {
        if (data == null)
        {
            return null;
        }

        if (data is byte[] bytes)
        {
            return bytes;
        }

        if (data is Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }

        if (data is Bitmap bitmap)
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms);
            return ms.ToArray();
        }

        return null;
    }

    private async Task<bool> ApplyClipboardImageBytesAsync(
        byte[] imageBytes,
        WndWindow targetWindow,
        CancellationToken cancellationToken)
    {
        var roots = SelectedAssetInstallation != null ? ResolveAssetRoots(SelectedAssetInstallation) : null;
        var projectDirectory = ResolveImportProjectDirectory(LinkedModFolder, roots, FilePath, FilesDirectory);
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.WndEditor.Paste.NoProjectTitle"),
                Localization.GetString("Tools.WndEditor.Paste.NoProjectMessage"),
                NotificationDurations.Medium);
            return false;
        }

        var rawName = !string.IsNullOrWhiteSpace(targetWindow.Name)
            ? targetWindow.Name
            : $"ImportedTexture_{DateTime.UtcNow:yyyyMMdd_HHmmss}";
        var candidateName = WndTextureImportService.SanitizeMappedName(rawName);

        var importResult = await textureImportService.ImportTextureFromBytesAsync(
            imageBytes,
            projectDirectory,
            candidateName,
            cancellationToken).ConfigureAwait(false);

        if (!importResult.Success || importResult.Data == null)
        {
            Notifications.ShowError(
                Localization.GetString("Tools.WndEditor.Paste.UnrecognizedTitle"),
                importResult.FirstError ?? Localization.GetString("Tools.WndEditor.Paste.UnrecognizedMessage"),
                NotificationDurations.Long);
            return false;
        }

        var mappedName = importResult.Data.MappedName;

        await InvokeOnUIThreadAsync(() =>
        {
            SelectWindow(targetWindow);
            ApplyImageToSelectedWindow(mappedName);
            assetService.InvalidateCache();
            RefreshAssetPreviews();
        }).ConfigureAwait(false);

        return true;
    }

    /// <summary>
    /// Links a mod root folder to load assets from.
    /// </summary>
    [RelayCommand]
    private async Task LinkModFolderWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.GetString("Tools.WndEditor.Assets.LinkModFolderTitle"),
            AllowMultiple = false,
        });

        if (folders.Count == 0)
        {
            return;
        }

        var localPath = folders[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            lock (_linkedAssetsSync)
            {
                if (string.Equals(LinkedModFolder, localPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                LinkedModFolder = localPath;
            }

            UpdateLinkedAssetsSummary();
            assetService.InvalidateCache();
            RefreshAssetPreviews();
        }
    }

    /// <summary>
    /// Links one or more .BIG archives to load assets from.
    /// </summary>
    [RelayCommand]
    private async Task LinkBigArchiveWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localization.GetString("Tools.WndEditor.Assets.LinkBigArchiveTitle"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Localization.GetString("Tools.WndEditor.Assets.BigArchiveFilter"))
                {
                    Patterns = ["*.big"],
                },
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        int initialCount;
        lock (_linkedAssetsSync)
        {
            initialCount = LinkedBigFiles.Count;
            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (!string.IsNullOrEmpty(localPath) && !LinkedBigFiles.Contains(localPath))
                {
                    LinkedBigFiles.Add(localPath);
                }
            }

            if (LinkedBigFiles.Count == initialCount)
            {
                return;
            }
        }

        UpdateLinkedAssetsSummary();
        assetService.InvalidateCache();
        RefreshAssetPreviews();
    }

    /// <summary>
    /// Clears all linked custom mod folders and .BIG archives.
    /// </summary>
    [RelayCommand]
    private void ClearLinkedAssets()
    {
        lock (_linkedAssetsSync)
        {
            LinkedModFolder = null;
            LinkedBigFiles.Clear();
        }

        UpdateLinkedAssetsSummary();
        assetService.InvalidateCache();
        RefreshAssetPreviews();
    }

    /// <summary>
    /// Removes a specific linked .BIG archive.
    /// </summary>
    [RelayCommand]
    private void RemoveLinkedBigFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        bool removed;
        lock (_linkedAssetsSync)
        {
            removed = LinkedBigFiles.Remove(path);
        }

        if (removed)
        {
            UpdateLinkedAssetsSummary();
            assetService.InvalidateCache();
            RefreshAssetPreviews();
        }
    }

    /// <summary>
    /// Reloads asset previews by invalidating cached indexes.
    /// </summary>
    [RelayCommand]
    private void ReloadAssets()
    {
        assetService.InvalidateCache();
        medalService.InvalidateCache();
        RefreshAssetPreviews();
    }

    /// <summary>
    /// Imports texture files into the mod project, registering each under its file stem.
    /// </summary>
    [RelayCommand]
    private async Task ImportTexturesWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var roots = SelectedAssetInstallation != null ? ResolveAssetRoots(SelectedAssetInstallation) : null;
        var projectDirectory = ResolveImportProjectDirectory(LinkedModFolder, roots, FilePath, FilesDirectory);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.WndEditor.Import.NoProjectTitle"),
                Localization.GetString("Tools.WndEditor.Import.NoProjectMessage"),
                NotificationDurations.Medium);
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localization.GetString("Tools.WndEditor.Import.DialogTitle"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(Localization.GetString("Tools.WndEditor.Import.FilterName"))
                {
                    Patterns = WndConstants.AssetImport.SourceExtensions.Select(extension => string.Concat("*", extension)).ToArray(),
                },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        var imported = 0;
        try
        {
            foreach (var file in files)
            {
                var localPath = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(localPath))
                {
                    continue;
                }

                var result = await textureImportService.ImportTextureAsync(localPath, projectDirectory, null, cancellationToken);
                if (result.Success && result.Data != null)
                {
                    imported++;
                    MissingImageNames.Remove(result.Data.MappedName);
                }
                else
                {
                    Notifications.ShowError(
                        Localization.GetString("Tools.WndEditor.Import.FailureTitle"),
                        Localization.GetString("Tools.WndEditor.Import.FailureMessage", Path.GetFileName(localPath), result.FirstError ?? string.Empty),
                        NotificationDurations.Long);
                }
            }
        }
        finally
        {
            foreach (var file in files)
            {
                file.Dispose();
            }
        }

        if (imported > 0)
        {
            telemetryService?.TrackEvent(TelemetryConstants.Events.WndTexturesImported, new Dictionary<string, object?>
            {
                [TelemetryConstants.Properties.TextureCount] = imported,
            });
            Notifications.ShowSuccess(
                Localization.GetString("Tools.WndEditor.Import.SuccessTitle"),
                Localization.GetString("Tools.WndEditor.Import.SuccessMessage", imported, projectDirectory),
                NotificationDurations.Medium);
            assetService.InvalidateCache();
            RefreshAssetPreviews();
        }
    }

    /// <summary>
    /// Imports a texture file registered under a specific missing mapped image name.
    /// </summary>
    /// <param name="mappedName">The missing mapped image name to provide art for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task ImportTextureForMissingAsync(string? mappedName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mappedName))
        {
            return;
        }

        var roots = SelectedAssetInstallation != null ? ResolveAssetRoots(SelectedAssetInstallation) : null;
        var projectDirectory = ResolveImportProjectDirectory(LinkedModFolder, roots, FilePath, FilesDirectory);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.WndEditor.Import.NoProjectTitle"),
                Localization.GetString("Tools.WndEditor.Import.NoProjectMessage"),
                NotificationDurations.Medium);
            return;
        }

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localization.GetString("Tools.WndEditor.Import.DialogTitleFor", mappedName),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localization.GetString("Tools.WndEditor.Import.FilterName"))
                {
                    Patterns = WndConstants.AssetImport.SourceExtensions.Select(extension => string.Concat("*", extension)).ToArray(),
                },
            ],
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            var localPath = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(localPath))
            {
                return;
            }

            var result = await textureImportService.ImportTextureAsync(localPath, projectDirectory, mappedName, cancellationToken);
            if (result.Success && result.Data != null)
            {
                Notifications.ShowSuccess(
                    Localization.GetString("Tools.WndEditor.Import.SuccessTitle"),
                    Localization.GetString("Tools.WndEditor.Import.SuccessMessage", 1, projectDirectory),
                    NotificationDurations.Medium);
                MissingImageNames.Remove(result.Data.MappedName);
                assetService.InvalidateCache();
                RefreshAssetPreviews();
            }
            else
            {
                Notifications.ShowError(
                    Localization.GetString("Tools.WndEditor.Import.FailureTitle"),
                    Localization.GetString("Tools.WndEditor.Import.FailureMessage", Path.GetFileName(localPath), result.FirstError ?? string.Empty),
                    NotificationDurations.Long);
            }
        }
        finally
        {
            foreach (var file in files)
            {
                file.Dispose();
            }
        }
    }

    /// <summary>
    /// Applies a mapped image to the selected window's enabled draw data.
    /// </summary>
    /// <param name="imageName">The mapped image name to apply.</param>
    [RelayCommand]
    private void ApplyImageToSelectedWindow(string? imageName)
    {
        if (string.IsNullOrWhiteSpace(imageName))
        {
            return;
        }

        var node = SelectedNode;
        if (node == null)
        {
            Notifications.ShowWarning(
                Localization.GetString(NoSelectionTitleKey),
                Localization.GetString(NoSelectionMessageKey),
                NotificationDurations.Medium);
            return;
        }

        var name = imageName.Trim();
        var entries = WndDrawDataSet.TryParse(node.Window.GetProperty(WndConstants.PropertyKeys.EnabledDrawData), out var parsed) && parsed != null
            ? parsed.Entries.ToList()
            : [];
        if (entries.Count == 0)
        {
            entries.Add(new WndDrawDataEntry(name, WndRgbaColor.White, WndRgbaColor.White));
        }
        else
        {
            var first = entries[0];
            entries[0] = new WndDrawDataEntry(name, first.Color, first.BorderColor);
        }

        var updated = new WndDrawDataSet(entries).ToString();
        if (string.Equals(node.Window.GetProperty(WndConstants.PropertyKeys.EnabledDrawData), updated, StringComparison.Ordinal))
        {
            return;
        }

        CommitPropertyEdit(node.Window, WndConstants.PropertyKeys.EnabledDrawData, updated);
        Notifications.ShowSuccess(
            Localization.GetString("Tools.WndEditor.Apply.SuccessTitle"),
            Localization.GetString("Tools.WndEditor.Apply.SuccessMessage", name, node.DisplayName),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Applies an art asset item to the currently selected window.
    /// </summary>
    /// <param name="item">The art item to apply.</param>
    [RelayCommand]
    private void ApplyArtItem(WndArtItemViewModel? item)
    {
        if (item != null)
        {
            ApplyImageToSelectedWindow(item.Name);
        }
    }

    private static string? ResolveImportProjectDirectory(
        string? linkedModFolder,
        AssetRoots? roots,
        string? filePath,
        string? filesDirectory)
    {
        if (!string.IsNullOrWhiteSpace(linkedModFolder))
        {
            return linkedModFolder;
        }

        if (roots != null)
        {
            var detected = ResolveProjectDirectory(filePath, roots) ?? ResolveProjectDirectory(filesDirectory, roots);
            if (!string.IsNullOrWhiteSpace(detected))
            {
                return detected;
            }
        }

        if (!string.IsNullOrWhiteSpace(filesDirectory) && Directory.Exists(filesDirectory))
        {
            return FindModRoot(filesDirectory) ?? filesDirectory;
        }

        if (!string.IsNullOrWhiteSpace(filePath))
        {
            var dir = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                return FindModRoot(dir) ?? dir;
            }
        }

        return null;
    }

    private void UpdateLinkedAssetsSummary()
    {
        string? modFolder;
        List<string> bigFiles;
        lock (_linkedAssetsSync)
        {
            modFolder = LinkedModFolder;
            bigFiles = LinkedBigFiles.ToList();
        }

        HasLinkedAssets = !string.IsNullOrEmpty(modFolder) || bigFiles.Count > 0;
        if (!HasLinkedAssets)
        {
            LinkedAssetsSummary = string.Empty;
            LinkedAssetsTooltip = null;
            return;
        }

        var parts = new List<string>();
        var tooltipLines = new List<string>();
        if (!string.IsNullOrEmpty(modFolder))
        {
            var folderName = Path.GetFileName(modFolder);
            parts.Add(Localization.GetString("Tools.WndEditor.Assets.LinkedModSummary", folderName));
            tooltipLines.Add(Localization.GetString("Tools.WndEditor.Assets.LinkedTooltipModPrefix", modFolder));
        }

        if (bigFiles.Count > 0)
        {
            parts.Add(Localization.GetString("Tools.WndEditor.Assets.LinkedBigSummary", bigFiles.Count));
            tooltipLines.AddRange(bigFiles.Select(b => Localization.GetString("Tools.WndEditor.Assets.LinkedTooltipBigPrefix", b)));
        }

        var separator = Localization.GetString("Tools.WndEditor.Assets.LinkedSummarySeparator");
        LinkedAssetsSummary = string.Join(separator, parts);
        LinkedAssetsTooltip = string.Join("\n", tooltipLines);
    }

    /// <summary>
    /// Validates the open document and reports the outcome.
    /// </summary>
    [RelayCommand]
    private void ValidateDocument()
    {
        if (_document == null)
        {
            return;
        }

        var target = FilePath ?? Localization.GetString("Tools.WndEditor.Document.Untitled");
        var result = wndDocumentService.ValidateDocument(_document, target);
        telemetryService?.TrackEvent(TelemetryConstants.Events.WndDocumentValidated, new Dictionary<string, object?>
        {
            [TelemetryConstants.Properties.IsValid] = result.IsValid,
            [TelemetryConstants.Properties.WindowCount] = _document.Windows.Count,
        });
        if (result.IsValid)
        {
            Notifications.ShowSuccess(
                Localization.GetString("Tools.WndEditor.Validate.SuccessTitle"),
                Localization.GetString("Tools.WndEditor.Validate.SuccessMessage"),
                NotificationDurations.Medium);
        }
        else
        {
            var first = result.Issues.Count > 0 ? result.Issues[0].Message : string.Empty;
            Notifications.ShowWarning(
                Localization.GetString("Tools.WndEditor.Validate.IssuesTitle"),
                Localization.GetString("Tools.WndEditor.Validate.IssuesMessage", result.CriticalIssueCount, result.WarningIssueCount, first),
                NotificationDurations.Long);
        }
    }

    /// <summary>
    /// Adds a child window to the selected window, or a top-level window when nothing is selected.
    /// </summary>
    [RelayCommand]
    private void AddChildWindow()
    {
        if (_document == null)
        {
            return;
        }

        var parent = SelectedNode;
        var window = CreateDefaultWindow();
        var siblings = parent == null ? _document.Windows : parent.Window.Children;
        siblings.Add(window);
        PushUndo(new WndEditAction(
            Localization.GetString("Tools.WndEditor.History.AddWindow"),
            () =>
            {
                siblings.Add(window);
                RebuildAll();
                SelectWindow(window);
            },
            () =>
            {
                siblings.Remove(window);
                RebuildAll();
                SelectWindow(parent?.Window);
            }));
        RebuildAll();
        SelectWindow(window);
        RefreshEditorCommands();
    }

    private static WndWindow CloneWindow(WndWindow source)
    {
        var clone = new WndWindow
        {
            ControlTypeName = source.ControlTypeName,
            FileName = source.FileName,
            HasEndAllChildren = source.HasEndAllChildren,
        };
        clone.Properties.AddRange(source.Properties);
        foreach (var child in source.Children)
        {
            clone.Children.Add(CloneWindow(child));
        }

        return clone;
    }

    private static void OffsetWindowRect(WndWindow window)
    {
        if (window.TryGetScreenRect(out var rect) && rect is not null)
        {
            var shifted = new WndScreenRect(
                rect.UpperLeftX + WndConstants.Editor.DuplicateOffset,
                rect.UpperLeftY + WndConstants.Editor.DuplicateOffset,
                rect.BottomRightX + WndConstants.Editor.DuplicateOffset,
                rect.BottomRightY + WndConstants.Editor.DuplicateOffset,
                rect.CreationWidth,
                rect.CreationHeight);
            window.SetProperty(WndConstants.PropertyKeys.ScreenRect, shifted.ToString());
        }
    }

    private void RemoveWindowWithUndo(WndTreeNodeViewModel node, string historyKey)
    {
        if (_document is null)
        {
            return;
        }

        var siblings = node.Parent is null ? _document.Windows : node.Parent.Window.Children;
        var index = siblings.IndexOf(node.Window);
        siblings.Remove(node.Window);
        PushUndo(new WndEditAction(
            Localization.GetString(historyKey),
            () =>
            {
                siblings.Remove(node.Window);
                RebuildAll();
                SelectWindow(node.Parent?.Window);
            },
            () =>
            {
                siblings.Insert(Math.Min(index, siblings.Count), node.Window);
                RebuildAll();
                SelectWindow(node.Window);
            }));
        RebuildAll();
        SelectWindow(node.Parent?.Window);
    }

    private void PasteCopiedWindow()
    {
        if (_document is null || _copiedWindow is null)
        {
            return;
        }

        var selected = SelectedNode;
        List<WndWindow> siblings;
        int insertIndex;
        if (!_isCutOperation && selected is not null && (ReferenceEquals(selected.Window, _copySourceWindow) || ReferenceEquals(selected.Window, _lastPastedClone)))
        {
            // Pasting onto the copied window itself (or the clone from the last
            // paste) duplicates beside it like OnDuplicate instead of nesting
            // the copy inside its own source.
            siblings = selected.Parent is null ? _document.Windows : selected.Parent.Window.Children;
            insertIndex = siblings.IndexOf(selected.Window) + 1;
        }
        else
        {
            siblings = selected is null ? _document.Windows : selected.Window.Children;
            insertIndex = siblings.Count;
        }

        var parent = selected;
        var clone = CloneWindow(_copiedWindow);
        if (!_isCutOperation)
        {
            OffsetWindowRect(clone);
        }

        siblings.Insert(Math.Min(insertIndex, siblings.Count), clone);
        PushUndo(new WndEditAction(
            Localization.GetString("Tools.WndEditor.History.PasteWindow"),
            () =>
            {
                siblings.Insert(Math.Min(insertIndex, siblings.Count), clone);
                RebuildAll();
                SelectWindow(clone);
            },
            () =>
            {
                siblings.Remove(clone);
                RebuildAll();
                SelectWindow(parent?.Window);
            }));
        RebuildAll();
        SelectWindow(clone);
        if (_isCutOperation)
        {
            _copiedWindow = null;
            _copySourceWindow = null;
            _lastPastedClone = null;
            _isCutOperation = false;
        }
        else
        {
            _lastPastedClone = clone;
        }

        RefreshEditorCommands();
    }

    /// <summary>
    /// Opens a file chosen in the explorer.
    /// </summary>
    /// <param name="file">The file tree node to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task OpenExplorerFileAsync(EditorFileTreeNodeViewModel? file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.IsDirectory)
        {
            return;
        }

        await OpenFileAsync(file.FullPath, cancellationToken);
    }

    private FileExplorerViewModel CreateFileExplorer()
    {
        var explorer = new FileExplorerViewModel(logger);
        explorer.FilePatterns = [ModBuilderConstants.FileNames.WndSearchPattern];
        explorer.ShowFileExtensions = false;
        explorer.ExcludedDirectoryNames = [ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.DefaultReleaseDir];
        explorer.NodeFactory = (name, fullPath, isDirectory, isCurrent, parent) => new WndFileTreeNodeViewModel(name, fullPath, isDirectory, isCurrent, parent);
        explorer.BrowseFolderAsync = PickFolderAsync;
        explorer.DirectoryAdoptedAsync = (f, ct) => AdoptExplorerDirectoryAsync(f, ct);
        explorer.FileActivated += OnExplorerFileActivated;
        return explorer;
    }

    private async Task<string?> PickFolderAsync(CancellationToken cancellationToken)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localization.GetString("Tools.WndEditor.FileDialog.FolderTitle"),
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

        var firstWnd = FileExplorer.FindFirstFile();
        if (!string.IsNullOrEmpty(firstWnd))
        {
            return await OpenFileAsync(firstWnd, cancellationToken).ConfigureAwait(false);
        }

        Notifications.ShowInfo(
            Localization.GetString("Tools.WndEditor.Files.NoWndFilesTitle"),
            Localization.GetString("Tools.WndEditor.Files.NoWndFilesMessage"),
            NotificationDurations.Medium);
        return true;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Routes to the source-generated OpenExplorerFileCommand instance member.")]
    private void OnExplorerFileActivated(object? sender, EditorFileTreeNodeViewModel node)
    {
        OpenExplorerFileCommand.Execute(node);
    }

    private static bool IsSubPathOf(string path, string basePath)
    {
        try
        {
            // Default macOS volumes are case-insensitive, so explorer containment must ignore
            // casing there; case-sensitive platforms keep ordinal semantics.
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

    /// <summary>
    /// Expands every window tree node.
    /// </summary>
    [RelayCommand]
    private void ExpandAllNodes()
    {
        SetNodesExpanded(RootNodes, true);
    }

    /// <summary>
    /// Collapses every window tree node.
    /// </summary>
    [RelayCommand]
    private void CollapseAllNodes()
    {
        SetNodesExpanded(RootNodes, false);
    }

    partial void OnSelectedNodeChanged(WndTreeNodeViewModel? value)
    {
        SyncCanvasSelection();
        RebuildProperties();
        RefreshEditorCommands();
    }

    partial void OnLibraryFilterChanged(string value)
    {
        RefreshLibrary();
    }

    partial void OnKnownImageNamesChanged(IReadOnlyList<string> value)
    {
        RefreshLibrary();
    }

    partial void OnWindowsFilterChanged(string value)
    {
        var selectedId = SelectedNode?.Window.Id;
        RebuildTree();
        SelectedNode = selectedId == null ? null : FindNode(selectedId.Value);
        SyncCanvasSelection();
    }

    partial void OnShowHiddenWindowsChanged(bool value)
    {
        foreach (var item in CanvasItems)
        {
            item.ShowHiddenWindows = value;
        }
    }

    partial void OnSelectedAssetInstallationChanged(GameInstallationOption? value)
    {
        _resolvedStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        assetService.InvalidateCache();
        medalService.InvalidateCache();
        RefreshAssetRootDisplay(value);
        RefreshAssetPreviews();
    }

    partial void OnLinkedModFolderChanged(string? value)
    {
        _ = value;
        UpdateLinkedAssetsSummary();
    }

    private void RefreshAssetRootDisplay(GameInstallationOption? selection)
    {
        if (selection == null)
        {
            AssetRootDisplayText = string.Empty;
            AssetRootDisplayTooltip = null;
            return;
        }

        var roots = ResolveAssetRoots(selection);
        AssetRootDisplayText = selection.DisplayName;
        AssetRootDisplayTooltip = roots.BaseRoot;
    }

    private async Task<bool> WriteDocumentToFileAsync(string filePath, CancellationToken cancellationToken, bool showFeedback = true)
    {
        if (_document == null)
        {
            return false;
        }

        try
        {
            var text = wndDocumentService.WriteDocument(_document);
            await AtomicFile.WriteAllTextAsync(filePath, text, cancellationToken).ConfigureAwait(false);
            if (showFeedback)
            {
                _savedHistoryVersion = _historyVersion;
                IsModified = false;
                telemetryService?.TrackEvent(TelemetryConstants.Events.WndDocumentSaved, new Dictionary<string, object?>
                {
                    [TelemetryConstants.Properties.FilePath] = TelemetryConstants.WndEditor.AnonymousDocumentName,
                    [TelemetryConstants.Properties.WindowCount] = _document.Windows.Count,
                    [TelemetryConstants.Properties.HasLinkedAssets] = HasLinkedAssets,
                });
                Notifications.ShowSuccess(
                    Localization.GetString("Tools.WndEditor.Save.SuccessTitle"),
                    Localization.GetString("Tools.WndEditor.Save.SuccessMessage", Path.GetFileName(filePath)),
                    NotificationDurations.Medium);
            }

            logger.LogInformation("Saved window definition file {Path}", filePath);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to save window definition file {Path}", filePath);
            if (showFeedback)
            {
                Notifications.ShowError(
                    Localization.GetString("Tools.WndEditor.Save.FailureTitle"),
                    Localization.GetString("Tools.WndEditor.Save.FailureMessage", ex.Message),
                    NotificationDurations.Long);
            }

            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied saving window definition file {Path}", filePath);
            if (showFeedback)
            {
                Notifications.ShowError(
                    Localization.GetString("Tools.WndEditor.Save.FailureTitle"),
                    Localization.GetString("Tools.WndEditor.Save.FailureMessage", ex.Message),
                    NotificationDurations.Long);
            }

            return false;
        }
    }

    private void AdoptDocument(WndDocument document, string? filePath)
    {
        if (!string.IsNullOrEmpty(filePath))
        {
            filePath = NormalizeSourceFilePath(filePath);
        }

        _document = document;
        FilePath = filePath;
        HasDocument = true;
        IsModified = false;
        _historyVersion = 0;
        _savedHistoryVersion = 0;
        _resolvedStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _undoStack.Clear();
        _redoStack.Clear();
        _copiedWindow = null;
        _isCutOperation = false;
        RefreshUndoCommands();
        ClearComposedBitmaps();
        SyncFilesDirectory(filePath);
        AutoSelectAssetInstallation();
        RebuildAll();
        RefreshAssetStatus();
        LeftSidebarTabIndex = 0;
        RefreshEditorCommands();
        CanvasFramingRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SyncFilesDirectory(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return;
        }

        filePath = NormalizeSourceFilePath(filePath);
        var directory = Path.GetDirectoryName(filePath);
        bool needsRoot = string.IsNullOrEmpty(FilesDirectory) || !Directory.Exists(FilesDirectory);
        if (!needsRoot && IsSubPathOf(filePath, FilesDirectory!))
        {
            FileExplorer.CurrentPath = filePath;
        }
        else if (needsRoot || !string.IsNullOrEmpty(directory))
        {
            FilesDirectory = directory;
            FileExplorer.CurrentPath = filePath;
        }
    }

    private void PushUndoStack(WndEditAction action)
    {
        _undoStack.Push(action);
        if (_undoStack.Count > MaxUndoHistory)
        {
            var kept = _undoStack.Take(MaxUndoHistory).Reverse().ToArray();
            _undoStack.Clear();
            foreach (var item in kept)
            {
                _undoStack.Push(item);
            }

            if (_savedHistoryVersion < _historyVersion - MaxUndoHistory)
            {
                _savedHistoryVersion = int.MinValue;
            }
        }
    }

    private void PushUndo(WndEditAction action)
    {
        PushUndoStack(action);
        if (_redoStack.Count > 0)
        {
            _savedHistoryVersion = int.MinValue;
            _redoStack.Clear();
        }

        _historyVersion++;
        IsModified = _historyVersion != _savedHistoryVersion;
        RefreshUndoCommands();
    }

    private void RefreshUndoCommands()
    {
        RefreshEditorCommands();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void RebuildAll()
    {
        var selectedId = SelectedNode?.Window.Id;
        RebuildTree();
        RebuildCanvas();
        RebuildProperties();
        SelectedNode = selectedId == null ? null : FindNode(selectedId.Value);
        SyncCanvasSelection();
    }

    private void RebuildTree()
    {
        var collapsedIds = new HashSet<Guid>();
        void CollectCollapsed(WndTreeNodeViewModel node)
        {
            if (!node.IsExpanded)
            {
                collapsedIds.Add(node.Window.Id);
            }

            foreach (var child in node.Children)
            {
                CollectCollapsed(child);
            }
        }

        foreach (var root in RootNodes)
        {
            CollectCollapsed(root);
        }

        RootNodes.Clear();
        _windowParents.Clear();
        if (_document == null)
        {
            return;
        }

        void IndexParents(WndWindow parent)
        {
            foreach (var child in parent.Children)
            {
                _windowParents[child.Id] = parent;
                IndexParents(child);
            }
        }

        foreach (var window in _document.Windows)
        {
            IndexParents(window);
        }

        var filter = WindowsFilter.Trim();
        foreach (var window in _document.Windows)
        {
            var node = BuildTreeNode(window, null, filter, collapsedIds);
            if (node != null)
            {
                RootNodes.Add(node);
            }
        }
    }

    private WndTreeNodeViewModel? BuildTreeNode(WndWindow window, WndTreeNodeViewModel? parent, string filter, HashSet<Guid> collapsedIds)
    {
        if (filter.Length > 0 && !SubtreeMatchesFilter(window, filter))
        {
            return null;
        }

        var node = new WndTreeNodeViewModel(window, parent);
        foreach (var child in window.Children)
        {
            var childNode = BuildTreeNode(child, node, filter, collapsedIds);
            if (childNode != null)
            {
                node.Children.Add(childNode);
            }
        }

        if (filter.Length > 0)
        {
            node.IsExpanded = true;
        }
        else if (collapsedIds.Contains(window.Id))
        {
            node.IsExpanded = false;
        }

        return node;
    }

    private void RebuildCanvas()
    {
        CanvasItems.Clear();
        if (_document == null)
        {
            return;
        }

        var maxWidth = WndConstants.Editor.MinCanvasWidth;
        var maxHeight = WndConstants.Editor.MinCanvasHeight;
        var selectedId = SelectedNode?.Window.Id;
        var screenW = 0;
        var screenH = 0;
        foreach (var window in EnumerateWindows(_document.Windows))
        {
            if (!window.TryGetScreenRect(out var rect) || rect == null)
            {
                continue;
            }

            if (screenW <= 0 && rect.CreationWidth > 0 && rect.CreationHeight > 0)
            {
                screenW = rect.CreationWidth;
                screenH = rect.CreationHeight;
            }

            var item = new WndCanvasItemViewModel(window)
            {
                IsSelected = window.Id == selectedId,
                ShowHiddenWindows = ShowHiddenWindows,
                X = (rect.UpperLeftX + WndConstants.Editor.CanvasPadding) * Zoom,
                Y = (rect.UpperLeftY + WndConstants.Editor.CanvasPadding) * Zoom,
                Width = Math.Max(0, rect.Width) * Zoom,
                Height = Math.Max(0, rect.Height) * Zoom,
            };
            RefreshItemPreview(item);
            CanvasItems.Add(item);
            maxWidth = Math.Max(maxWidth, rect.BottomRightX);
            maxHeight = Math.Max(maxHeight, rect.BottomRightY);
        }

        VirtualScreenWidth = (int)Math.Max(screenW, Math.Max(maxWidth, WndConstants.Editor.MinCanvasWidth));
        VirtualScreenHeight = (int)Math.Max(screenH, Math.Max(maxHeight, WndConstants.Editor.MinCanvasHeight));

        _canvasBaseWidth = Math.Max(maxWidth, VirtualScreenWidth) + (WndConstants.Editor.CanvasPadding * 2);
        _canvasBaseHeight = Math.Max(maxHeight, VirtualScreenHeight) + (WndConstants.Editor.CanvasPadding * 2);
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private void RebuildProperties()
    {
        // Flush in-flight color picker edits before discarding the panel: an open flyout
        // does not reliably raise Closed when its owner is rebuilt, which silently dropped
        // the edit on selection change.
        SelectedProperties?.FlushPendingEdits();

        var node = SelectedNode;
        SelectedProperties = node == null
            ? null
            : new WndWindowPropertiesViewModel(
                node.Window,
                wndDocumentService,
                Notifications,
                Localization,
                (key, value) => CommitPropertyEdit(node.Window, key, value),
                key => RemovePropertyByKey(node.Window, key),
                properties => ReplaceSelectedProperties(node.Window, properties));
        if (SelectedProperties != null)
        {
            SelectedProperties.ImageNameOptions = KnownImageNames;
        }
    }

    private void CommitPropertyEdit(WndWindow window, string key, string value)
    {
        if (_document == null)
        {
            return;
        }

        var oldValue = window.GetProperty(key);
        if (string.Equals(oldValue, value, StringComparison.Ordinal))
        {
            return;
        }

        window.SetProperty(key, value);
        SyncWindowType(window);
        PushUndo(new WndEditAction(
            Localization.GetString("Tools.WndEditor.History.EditProperty", key),
            () =>
            {
                window.SetProperty(key, value);
                SyncWindowType(window);
                SyncAfterEdit(window);
            },
            () =>
            {
                if (oldValue == null)
                {
                    window.RemoveProperty(key);
                }
                else
                {
                    window.SetProperty(key, oldValue);
                }

                SyncWindowType(window);
                SyncAfterEdit(window);
            }));
        SyncAfterEdit(window);
        NotifyIfNewlyHidden(key, oldValue, value);
    }

    private void RemovePropertyByKey(WndWindow window, string key)
    {
        if (_document == null)
        {
            return;
        }

        var index = window.Properties.FindIndex(p => string.Equals(p.Key, key, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        var removed = window.Properties[index];
        window.Properties.RemoveAt(index);
        SyncWindowType(window);
        PushUndo(new WndEditAction(
            Localization.GetString("Tools.WndEditor.History.DeleteProperty", key),
            () =>
            {
                window.RemoveProperty(key);
                SyncWindowType(window);
                SyncAfterEdit(window);
            },
            () =>
            {
                window.Properties.Insert(Math.Min(index, window.Properties.Count), removed);
                SyncWindowType(window);
                SyncAfterEdit(window);
            }));
        SyncAfterEdit(window);
    }

    private void ReplaceSelectedProperties(WndWindow window, IReadOnlyList<WndProperty> properties)
    {
        if (_document == null)
        {
            return;
        }

        var previous = window.Properties.ToList();
        ApplyProperties(window, properties);
        PushUndo(new WndEditAction(
            Localization.GetString("Tools.WndEditor.History.ApplyRawText"),
            () =>
            {
                ApplyProperties(window, properties);
                RebuildAll();
            },
            () =>
            {
                ApplyProperties(window, previous);
                RebuildAll();
            }));
        RebuildAll();
        NotifyIfNewlyHidden(
            WndConstants.PropertyKeys.Status,
            FindPropertyValue(previous, WndConstants.PropertyKeys.Status),
            FindPropertyValue(properties, WndConstants.PropertyKeys.Status));
    }

    private void NotifyIfNewlyHidden(string key, string? oldValue, string? newValue)
    {
        if (!string.Equals(key, WndConstants.PropertyKeys.Status, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (ContainsHiddenFlag(oldValue) || !ContainsHiddenFlag(newValue))
        {
            return;
        }

        Notifications.ShowInfo(
            Localize("Tools.WndEditor.Hidden.HiddenTitle", "Window hidden"),
            Localize("Tools.WndEditor.Hidden.HiddenMessage", "Select it in the Windows tree or enable Show hidden to edit it again."),
            NotificationDurations.Medium);
    }

    private static string? FindPropertyValue(IReadOnlyList<WndProperty> properties, string key)
    {
        return properties.FirstOrDefault(property => string.Equals(property.Key, key, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static bool ContainsHiddenFlag(string? statusValue)
    {
        if (string.IsNullOrWhiteSpace(statusValue))
        {
            return false;
        }

        return WndStatusValue.ParseStatus(statusValue).Flags.Contains(WndConstants.StatusFlags.Hidden, StringComparer.OrdinalIgnoreCase);
    }

    private void SyncAfterEdit(WndWindow window)
    {
        FindNode(window.Id)?.RefreshDisplay();
        if (SelectedNode?.Window.Id == window.Id)
        {
            SelectedProperties?.RefreshFromWindow();
        }

        RebuildCanvas();
        var plan = WndPreviewPlanner.Plan(window, _schemeOverrides, _runtimeArt);
        var missingImage = plan.ReferencedImages.Any(imageName => !_previewBitmaps.ContainsKey(imageName));
        var missingLabel = plan.Text != null
            && window.ControlType != WndControlType.EntryField
            && !_resolvedStrings.ContainsKey(plan.Text);
        if (missingImage || missingLabel)
        {
            RefreshAssetPreviews();
        }
    }

    private void SyncCanvasSelection()
    {
        var selectedId = SelectedNode?.Window.Id;
        foreach (var item in CanvasItems)
        {
            item.IsSelected = item.Window.Id == selectedId;
        }
    }

    private void SelectWindow(WndWindow? window)
    {
        if (window != null && FindNode(window.Id) == null && !string.IsNullOrWhiteSpace(WindowsFilter))
        {
            WindowsFilter = string.Empty;
        }

        SelectedNode = window == null ? null : FindNode(window.Id);
        if (SelectedNode != null)
        {
            ExpandAncestors(SelectedNode);
        }

        SyncCanvasSelection();
    }

    private WndTreeNodeViewModel? FindNode(Guid id)
    {
        foreach (var root in RootNodes)
        {
            var found = FindNodeRecursive(root, id);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private async Task EnsureInstallationsLoadedAsync(CancellationToken cancellationToken)
    {
        if (_installationsLoaded)
        {
            return;
        }

        try
        {
            var result = await gameInstallationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.Data == null)
            {
                logger.LogDebug("Game installation lookup failed: {Error}", result.FirstError);
                return;
            }

            _installations = result.Data;
            await InvokeOnUIThreadAsync(() => PopulateAssetInstallations(result.Data)).ConfigureAwait(false);
            _installationsLoaded = true;
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Game installation lookup was canceled");
        }
    }

    private void PopulateAssetInstallations(IReadOnlyList<GameInstallation> installations)
    {
        AvailableInstallations.Clear();
        foreach (var installation in installations)
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

        SelectedAssetInstallation = SelectBestAssetInstallation();
    }

    private bool IsZeroHourPath(GameInstallationOption option)
    {
        return _installations.Any(installation =>
            string.Equals(option.Path, installation.ZeroHourPath, PathHelper.PathComparison));
    }

    private GameInstallationOption? SelectBestAssetInstallation()
    {
        var containing = string.IsNullOrEmpty(FilePath) ? null : AvailableInstallations.FirstOrDefault(option => IsPathUnder(FilePath, option.Path));
        return containing
            ?? AvailableInstallations.FirstOrDefault(option => option.IsZeroHour)
            ?? AvailableInstallations.FirstOrDefault(IsZeroHourPath)
            ?? AvailableInstallations.FirstOrDefault();
    }

    private void AutoSelectAssetInstallation()
    {
        if (AvailableInstallations.Count == 0)
        {
            return;
        }

        var best = SelectBestAssetInstallation();
        if (best != null && !ReferenceEquals(best, SelectedAssetInstallation))
        {
            SelectedAssetInstallation = best;
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
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private AssetRoots ResolveAssetRoots(GameInstallationOption selection)
    {
        foreach (var installation in _installations)
        {
            if (!string.IsNullOrEmpty(installation.ZeroHourPath)
                && string.Equals(selection.Path, installation.ZeroHourPath, PathHelper.PathComparison))
            {
                return ResolveZeroHourAssetRoots(installation);
            }

            if (!string.IsNullOrEmpty(installation.GeneralsPath)
                && string.Equals(selection.Path, installation.GeneralsPath, PathHelper.PathComparison))
            {
                return new AssetRoots(installation.GeneralsPath, false);
            }
        }

        return new AssetRoots(selection.Path, selection.IsZeroHour || IsZeroHourPath(selection));
    }

    private static AssetRoots ResolveZeroHourAssetRoots(GameInstallation installation)
    {
        // Strict per-game isolation: a Zero Hour target resolves only the Zero Hour
        // install (plus mod project and linked assets). Zero Hour never reads the
        // Generals install folder, so no Generals fallback is attached.
        return new AssetRoots(installation.ZeroHourPath, true);
    }

    private static bool TryParseGeneralPosition(string shortName, out int position)
    {
        position = -1;
        var prefix = WndConstants.Challenge.GeneralPositionPrefix;
        if (!shortName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || shortName.Length <= prefix.Length)
        {
            return false;
        }

        return int.TryParse(shortName[prefix.Length..], out position);
    }

    private bool HasHiddenAncestor(WndWindow window)
    {
        var current = window;
        while (_windowParents.TryGetValue(current.Id, out var parent))
        {
            if (WndPreviewPlanner.Plan(parent, _schemeOverrides, _runtimeArt).IsHidden)
            {
                return true;
            }

            current = parent;
        }

        return false;
    }

    private void RefreshItemPreview(WndCanvasItemViewModel item)
    {
        var plan = WndPreviewPlanner.Plan(item.Window, _schemeOverrides, _runtimeArt);
        var hidden = plan.IsHidden || HasHiddenAncestor(item.Window);
        item.FillOverlay = ToOverlayBrush(plan.FillColor);
        item.BorderOverlay = ToOverlayBrush(plan.BorderColor);
        item.ContentText = ResolveDisplayText(plan, item.Window);
        item.ContentTextBrush = ToOverlayBrush(plan.TextColor) ?? Brushes.White;
        item.ContentFontSize = Math.Max(WndConstants.Preview.MinContentFontSize, plan.FontSize * Zoom);
        item.ContentFontWeight = plan.FontBold ? FontWeight.Bold : FontWeight.Normal;
        item.ContentTextAlignment = plan.TextCentered ? TextAlignment.Center : TextAlignment.Left;
        item.ContentFontFamily = ResolveFontFamily(plan.FontName);

        // Hidden windows are filtered out of the canvas entirely unless selected in the
        // window tree. When a hidden window is selected, IsPreviewHidden remains true but
        // CanvasOpacity renders it at a dimmed opacity so the author can inspect its layout.
        item.CanvasOpacity = hidden ? WndConstants.Preview.HiddenOpacity : 1.0;
        item.IsPreviewHidden = hidden;
        item.Image = ResolvePlanImage(plan, item);
        RefreshItemGlyph(item, plan);
        item.Overlays = ResolveOverlays(plan, item);
    }

    private IReadOnlyList<WndCanvasOverlayViewModel> ResolveOverlays(WndPreviewPlan plan, WndCanvasItemViewModel item)
    {
        if (plan.SubImages == null)
        {
            return [];
        }

        var overlays = new List<WndCanvasOverlayViewModel>();
        AddScrollOverlays(plan.SubImages, item, overlays);
        AddComboButtonOverlay(plan.SubImages, item, overlays);
        AddSliderThumbOverlay(plan.SubImages, item, overlays);
        return overlays;
    }

    private Bitmap? FindBitmap(string? name)
    {
        return name != null && _previewBitmaps.TryGetValue(name, out var bitmap) ? bitmap : null;
    }

    private void AddScrollOverlays(WndPreviewSubImages sub, WndCanvasItemViewModel item, List<WndCanvasOverlayViewModel> overlays)
    {
        var up = FindBitmap(sub.ScrollUp);
        var down = FindBitmap(sub.ScrollDown);
        var thumb = FindBitmap(sub.ScrollThumb);
        var upHeight = up == null ? 0 : Math.Min(up.PixelSize.Height * Zoom, item.Height);
        var downHeight = down == null ? 0 : Math.Min(down.PixelSize.Height * Zoom, Math.Max(0, item.Height - upHeight));
        var gutter = GutterWidth([up, down, thumb], item.Width, Zoom);
        if (up != null)
        {
            var width = Math.Min(up.PixelSize.Width * Zoom, item.Width);
            overlays.Add(new WndCanvasOverlayViewModel(up, Math.Max(0, item.Width - width), 0, width, upHeight));
        }

        if (down != null)
        {
            var width = Math.Min(down.PixelSize.Width * Zoom, item.Width);
            overlays.Add(new WndCanvasOverlayViewModel(down, Math.Max(0, item.Width - width), Math.Max(0, item.Height - downHeight), width, downHeight));
        }

        var trackHeight = item.Height - upHeight - downHeight;
        if (trackHeight > 0 && gutter > 0)
        {
            AddScrollTrackOverlay(sub, item, overlays, gutter, upHeight, trackHeight);
        }

        if (thumb != null && trackHeight > 0)
        {
            var width = Math.Min(thumb.PixelSize.Width * Zoom, item.Width);
            var height = Math.Min(thumb.PixelSize.Height * Zoom, trackHeight);
            overlays.Add(new WndCanvasOverlayViewModel(thumb, Math.Max(0, item.Width - width), upHeight + ((trackHeight - height) / 2), width, height));
        }
    }

    private void AddScrollTrackOverlay(WndPreviewSubImages sub, WndCanvasItemViewModel item, List<WndCanvasOverlayViewModel> overlays, double gutter, double top, double height)
    {
        if (!sub.HasScrollTrack || sub.ScrollTrackTop == null || sub.ScrollTrackCenter == null || sub.ScrollTrackBottom == null)
        {
            return;
        }

        var width = Math.Max(1, (int)Math.Round(gutter));
        var trackHeight = Math.Max(1, (int)Math.Round(height));
        if (TryGetComposedBitmap(sub.ScrollTrackTop, sub.ScrollTrackCenter, sub.ScrollTrackBottom, width, trackHeight, true, out var track) && track != null)
        {
            overlays.Add(new WndCanvasOverlayViewModel(track, Math.Max(0, item.Width - gutter), top, gutter, height));
        }
    }

    private static double GutterWidth(IReadOnlyList<Bitmap?> bitmaps, double maxWidth, double zoom)
    {
        var gutter = 0.0;
        foreach (var bitmap in bitmaps.Where(b => b != null))
        {
            gutter = Math.Max(gutter, bitmap!.PixelSize.Width * zoom);
        }

        return Math.Min(gutter, maxWidth);
    }

    private void AddComboButtonOverlay(WndPreviewSubImages sub, WndCanvasItemViewModel item, List<WndCanvasOverlayViewModel> overlays)
    {
        if (sub.ComboButton == null || !_previewBitmaps.TryGetValue(sub.ComboButton, out var button))
        {
            return;
        }

        var width = Math.Min(button.PixelSize.Width * Zoom, item.Width);
        overlays.Add(new WndCanvasOverlayViewModel(button, Math.Max(0, item.Width - width), 0, width, item.Height));
    }

    private void AddSliderThumbOverlay(WndPreviewSubImages sub, WndCanvasItemViewModel item, List<WndCanvasOverlayViewModel> overlays)
    {
        if (sub.SliderThumb == null || !_previewBitmaps.TryGetValue(sub.SliderThumb, out var thumb))
        {
            return;
        }

        var width = Math.Min(thumb.PixelSize.Width * Zoom, item.Width);
        var height = Math.Min(thumb.PixelSize.Height * Zoom, item.Height);
        overlays.Add(new WndCanvasOverlayViewModel(thumb, Math.Max(0, (item.Width - width) / 2), Math.Max(0, (item.Height - height) / 2), width, height));
    }

    private static FontFamily? ResolveFontFamily(string? fontName)
    {
        if (string.IsNullOrWhiteSpace(fontName))
        {
            return null;
        }

        try
        {
            return new FontFamily(fontName.Trim());
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private void RefreshItemGlyph(WndCanvasItemViewModel item, WndPreviewPlan plan)
    {
        if (plan.GlyphImage == null || !_previewBitmaps.TryGetValue(plan.GlyphImage, out var glyph))
        {
            item.GlyphImage = null;
            item.GlyphWidth = 0;
            item.GlyphHeight = 0;
            item.ContentTextPadding = new Thickness(4, 2);
            return;
        }

        item.GlyphImage = glyph;
        item.GlyphWidth = glyph.PixelSize.Width * Zoom;
        item.GlyphHeight = glyph.PixelSize.Height * Zoom;
        item.ContentTextPadding = new Thickness(
            WndConstants.Preview.GlyphMargin + item.GlyphWidth + WndConstants.Preview.GlyphTextGap,
            2,
            4,
            2);
    }

    private string? ResolveDisplayText(WndPreviewPlan plan, WndWindow window)
    {
        if (plan.Text == null && IsMapPreviewPlaceholder(window, plan))
        {
            return Localization.GetString("Tools.WndEditor.Canvas.MapPreviewPlaceholder");
        }

        if (plan.Text == null || window.ControlType == WndControlType.EntryField)
        {
            return plan.Text;
        }

        if (_resolvedStrings.TryGetValue(plan.Text, out var localized) && !string.IsNullOrEmpty(localized))
        {
            return WndGameText.StripHotkeyMarkers(localized);
        }

        // Labels carrying a category prefix (GUI:, TOOLTIP:, ...) that miss the string table
        // are runtime-populated slots (challenge biographies, player names). The game never
        // shows the raw label, so the preview leaves them blank instead of leaking internals.
        if (plan.Text.StartsWith("GUI:", StringComparison.OrdinalIgnoreCase) ||
            plan.Text.StartsWith("TOOLTIP:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return plan.Text;
    }

    private static bool IsMapPreviewPlaceholder(WndWindow window, WndPreviewPlan plan)
    {
        if (plan.SingleImage != null || plan.IsThreePiece)
        {
            return false;
        }

        var drawCallback = window.GetProperty(WndConstants.PropertyKeys.DrawCallback);
        return drawCallback != null
            && drawCallback.Contains(WndConstants.DrawCallbacks.MapPreview, StringComparison.OrdinalIgnoreCase);
    }

    private Bitmap? ResolvePlanImage(WndPreviewPlan plan, WndCanvasItemViewModel item)
    {
        if (TryResolveThreePiece(plan, item, out var composed))
        {
            return composed;
        }

        if (plan.SingleImage != null)
        {
            return ResolveSinglePlanImage(plan, item);
        }

        if (plan.IsThreePiece)
        {
            return ResolveThreePieceFallbackImage(plan);
        }

        return null;
    }

    private Bitmap? ResolveSinglePlanImage(WndPreviewPlan plan, WndCanvasItemViewModel item)
    {
        if (plan.UnderlayImage != null && TryResolveUnderlay(plan.UnderlayImage, plan.SingleImage!, item, out var underlayComposed))
        {
            return underlayComposed;
        }

        return FindBitmap(plan.SingleImage);
    }

    private Bitmap? ResolveThreePieceFallbackImage(WndPreviewPlan plan)
    {
        return FindBitmap(plan.LeftImage) ?? FindBitmap(plan.CenterImage) ?? FindBitmap(plan.RightImage);
    }

    private bool TryResolveUnderlay(string underlay, string overlay, WndCanvasItemViewModel item, out Bitmap? bitmap)
    {
        bitmap = null;
        if (!item.Window.TryGetScreenRect(out var rect) || rect == null || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        var key = string.Concat("underlay|", underlay, "|", overlay, "|", rect.Width, "x", rect.Height);
        if (_composedBitmaps.TryGetValue(key, out var cached))
        {
            bitmap = cached;
            return true;
        }

        if (!_previewPngs.TryGetValue(underlay, out var underlayPng) || !_previewPngs.TryGetValue(overlay, out var overlayPng))
        {
            return false;
        }

        var composed = WndPreviewImageComposer.ComposeUnderlay(underlayPng, overlayPng, rect.Width, rect.Height);
        if (composed == null)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(composed);
            bitmap = new Bitmap(stream);
            _composedBitmaps[key] = bitmap;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            logger.LogDebug(ex, "Failed to decode underlay bitmap {Underlay}+{Overlay}", underlay, overlay);
            return false;
        }
    }

    private bool TryResolveThreePiece(WndPreviewPlan plan, WndCanvasItemViewModel item, out Bitmap? bitmap)
    {
        bitmap = null;
        if (!plan.IsThreePiece || plan.LeftImage == null || plan.CenterImage == null || plan.RightImage == null)
        {
            return false;
        }

        if (!item.Window.TryGetScreenRect(out var rect) || rect == null || rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        return TryGetComposedBitmap(plan.LeftImage, plan.CenterImage, plan.RightImage, rect.Width, rect.Height, plan.IsVerticalBar, out bitmap);
    }

    private bool TryGetComposedBitmap(string left, string center, string right, int width, int height, bool vertical, out Bitmap? bitmap)
    {
        bitmap = null;
        var orientation = vertical ? "v" : "h";
        var key = string.Concat(orientation, "|", left, "|", center, "|", right, "|", width, "x", height);
        if (_composedBitmaps.TryGetValue(key, out var cached))
        {
            bitmap = cached;
            return true;
        }

        if (!_previewPngs.TryGetValue(left, out var leftPng)
            || !_previewPngs.TryGetValue(center, out var centerPng)
            || !_previewPngs.TryGetValue(right, out var rightPng))
        {
            return false;
        }

        var composed = vertical
            ? WndPreviewImageComposer.ComposeThreePieceVertical(leftPng, centerPng, rightPng, width, height)
            : WndPreviewImageComposer.ComposeThreePiece(leftPng, centerPng, rightPng, width, height);
        if (composed == null)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(composed);
            bitmap = new Bitmap(stream);
            _composedBitmaps[key] = bitmap;
            return true;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            logger.LogWarning(ex, "Failed to decode composed bitmap for {Key}", key);
            return false;
        }
    }

    private void ClearComposedBitmaps()
    {
        foreach (var bitmap in _composedBitmaps.Values)
        {
            bitmap.Dispose();
        }

        _composedBitmaps.Clear();
    }

    private void RefreshAssetPreviews()
    {
        CancellationTokenSource? toCancel;
        CancellationTokenSource cts;
        int generation;
        string? linkedModFolderSnapshot;
        List<string>? linkedBigFilesSnapshot;
        lock (_previewSync)
        {
            toCancel = _previewCts;
            _previewCts = null;
            if (_document == null || SelectedAssetInstallation == null)
            {
                CancelAndDisposeCts(toCancel);
                return;
            }

            cts = new CancellationTokenSource();
            _previewCts = cts;
            generation = ++_previewGeneration;
            lock (_linkedAssetsSync)
            {
                linkedModFolderSnapshot = LinkedModFolder;
                linkedBigFilesSnapshot = LinkedBigFiles.Count > 0 ? LinkedBigFiles.ToList() : null;
            }
        }

        CancelAndDisposeCts(toCancel);
        _ = LoadAssetPreviewsAsync(cts.Token, generation, linkedModFolderSnapshot, linkedBigFilesSnapshot);
    }

    private static void CancelAndDisposeCts(CancellationTokenSource? cts)
    {
        if (cts == null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Expected if CTS was already disposed by a concurrent cancellation.
        }
        finally
        {
            cts.Dispose();
        }
    }

    private async Task LoadAssetPreviewsAsync(
        CancellationToken cancellationToken,
        int generation,
        string? linkedModFolderSnapshot,
        IReadOnlyCollection<string>? linkedBigFilesSnapshot)
    {
        try
        {
            var document = _document;
            var selection = SelectedAssetInstallation;
            if (document == null || selection == null)
            {
                return;
            }

            var roots = ResolveAssetRoots(selection);
            var detectedProjectDir = ResolveProjectDirectory(FilePath, roots) ?? ResolveProjectDirectory(FilesDirectory, roots);
            var projectDirectory = CombineProjectDirectories(linkedModFolderSnapshot, detectedProjectDir);
            var linkedBigs = linkedBigFilesSnapshot;
            var schemeOverrides = await Task.Run(() => ResolveSchemeOverrides(roots, projectDirectory, cancellationToken, linkedBigs), cancellationToken).ConfigureAwait(false);
            if (generation != _previewGeneration)
            {
                return;
            }

            var medalsResult = await medalService.GetMedalsAsync(roots.BaseRoot, null, projectDirectory, linkedBigs, roots.IsZeroHour, cancellationToken).ConfigureAwait(false);
            if (generation != _previewGeneration)
            {
                return;
            }

            var medals = medalsResult.Success && medalsResult.Data != null ? medalsResult.Data : null;
            var runtimeArt = BuildRuntimeArt(document, medals);
            logger.LogDebug(
                "Runtime art for {File}: {Medals} medal images, {Hidden} shell-hidden windows",
                document.SourcePath == null ? "untitled" : Path.GetFileName(document.SourcePath),
                runtimeArt.MedalImages.Count,
                runtimeArt.HiddenWindows.Count);
            var names = CollectPreviewImageNames(document, schemeOverrides, runtimeArt);
            var labels = CollectPreviewLabels(document, schemeOverrides, runtimeArt);
            var images = await assetService.Images.GetImagesAsync(names, roots.BaseRoot, null, projectDirectory, linkedBigs, roots.IsZeroHour, cancellationToken).ConfigureAwait(false);
            var strings = await assetService.Strings.GetStringsAsync(labels, roots.BaseRoot, null, projectDirectory, linkedBigs, roots.IsZeroHour, cancellationToken).ConfigureAwait(false);
            if (generation != _previewGeneration)
            {
                return;
            }

            // The asset index is already built above, so listing known names is cheap.
            var known = await assetService.Images.GetKnownImageNamesAsync(roots.BaseRoot, null, projectDirectory, linkedBigs, roots.IsZeroHour, cancellationToken).ConfigureAwait(false);
            if (generation != _previewGeneration)
            {
                return;
            }

            var bitmaps = images.Success ? images.Data : null;
            var values = strings.Success ? strings.Data : null;
            var knownNames = known.Success && known.Data != null ? known.Data : null;
            await InvokeOnUIThreadAsync(() => ApplyPreviews(bitmaps, values, generation, labels, knownNames, runtimeArt, schemeOverrides)).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            logger.LogDebug(ex, "Asset preview load was superseded or canceled");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Asset preview load failed");
        }
    }

    private void ApplyPreviews(
        IReadOnlyDictionary<string, byte[]>? images,
        IReadOnlyDictionary<string, string>? strings,
        int generation,
        IReadOnlyCollection<string>? attemptedLabels = null,
        IReadOnlyList<string>? knownNames = null,
        WndRuntimeArt? runtimeArt = null,
        IReadOnlyDictionary<string, string>? schemeOverrides = null)
    {
        if (generation != _previewGeneration)
        {
            return;
        }

        if (schemeOverrides != null)
        {
            _schemeOverrides = schemeOverrides;
        }

        if (runtimeArt != null)
        {
            _runtimeArt = runtimeArt;
        }

        if (knownNames != null)
        {
            SyncKnownImageNames(knownNames);
        }

        if (images != null)
        {
            UpdatePreviewBitmaps(images);
        }

        if (strings != null || attemptedLabels != null)
        {
            UpdatePreviewStrings(strings, attemptedLabels);
        }

        foreach (var item in CanvasItems)
        {
            RefreshItemPreview(item);
        }

        RefreshAssetStatus(logMissing: images != null);
    }

    private void UpdatePreviewBitmaps(IReadOnlyDictionary<string, byte[]> images)
    {
        var previous = _previewBitmaps;
        var bitmaps = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var (name, png) in images)
            {
                using var stream = new MemoryStream(png);
                bitmaps[name] = new Bitmap(stream);
            }
        }
        catch
        {
            foreach (var bitmap in bitmaps.Values)
            {
                bitmap.Dispose();
            }

            throw;
        }

        _previewBitmaps = bitmaps;
        _previewPngs = images;
        ClearComposedBitmaps();
        foreach (var old in previous.Values)
        {
            old.Dispose();
        }
    }

    private void UpdatePreviewStrings(IReadOnlyDictionary<string, string>? strings, IReadOnlyCollection<string>? attemptedLabels)
    {
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (attemptedLabels != null)
        {
            foreach (var label in attemptedLabels)
            {
                resolved[label] = string.Empty;
            }
        }

        if (strings != null)
        {
            foreach (var (key, value) in strings)
            {
                resolved[key] = value;
            }
        }

        _resolvedStrings = resolved;
    }

    private void RefreshAssetStatus(bool logMissing = false)
    {
        if (_document == null)
        {
            AssetStatusText = string.Empty;
            AssetStatusTooltip = null;
            SyncMissingImageNames([]);
            return;
        }

        if (SelectedAssetInstallation == null)
        {
            AssetStatusText = Localization.GetString("Tools.WndEditor.Assets.SelectorWatermark");
            AssetStatusTooltip = null;
            SyncMissingImageNames([]);
            return;
        }

        var names = CollectPreviewImageNames(_document, _schemeOverrides, _runtimeArt);
        if (names.Count == 0)
        {
            AssetStatusText = Localization.GetString("Tools.WndEditor.Assets.EmptyStatus");
            AssetStatusTooltip = null;
            SyncMissingImageNames([]);
            return;
        }

        var missing = names.Where(name => !_previewBitmaps.ContainsKey(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        SyncMissingImageNames(missing);
        AssetStatusText = Localization.GetString("Tools.WndEditor.Assets.ResolvedStatus", names.Count - missing.Count, names.Count);
        AssetStatusTooltip = missing.Count == 0
            ? null
            : Localization.GetString("Tools.WndEditor.Assets.MissingTooltip", FormatMissingNames(missing));
        if (logMissing && missing.Count > 0)
        {
            logger.LogDebug(
                "WND preview for {File} is missing {Missing} of {Total} images: {Names}",
                FilePath ?? FilesDirectory ?? "unsaved document",
                missing.Count,
                names.Count,
                string.Join(", ", missing));
        }
    }

    private void SyncMissingImageNames(IReadOnlyList<string> missing)
    {
        MissingImageNames.Clear();
        foreach (var name in missing)
        {
            MissingImageNames.Add(name);
        }

        HasMissingImages = MissingImageNames.Count > 0;
    }

    private void SyncKnownImageNames(IReadOnlyList<string> known)
    {
        KnownImageNames = known;
        KnownImagesStatusText = Localization.GetString("Tools.WndEditor.Assets.KnownCount", known.Count);
        if (SelectedProperties != null)
        {
            SelectedProperties.ImageNameOptions = known;
        }
    }

    private void RefreshLibrary()
    {
        var filter = LibraryFilter.Trim();
        var matches = string.IsNullOrEmpty(filter)
            ? KnownImageNames
            : KnownImageNames.Where(name => name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0)
        {
            FilteredKnownImageNames = [];
            FilteredArtItems = [];
            LibraryStatusText = KnownImageNames.Count == 0
                ? string.Empty
                : Localization.GetString("Tools.WndEditor.Assets.LibraryEmpty");
            return;
        }

        var selected = matches.Take(WndConstants.Editor.MaxLibraryResults).ToList();
        FilteredKnownImageNames = selected;
        LibraryStatusText = matches.Count > selected.Count
            ? Localization.GetString("Tools.WndEditor.Assets.LibraryTruncated", selected.Count, matches.Count)
            : string.Empty;

        var tooltipTemplate = Localization.GetString("Tools.WndEditor.Assets.ClickToAdd");
        var items = new List<WndArtItemViewModel>(selected.Count);
        foreach (var name in selected)
        {
            var tooltip = !string.IsNullOrWhiteSpace(tooltipTemplate)
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture, tooltipTemplate, name)
                : name;
            _thumbnailBitmaps.TryGetValue(name, out var bmp);
            items.Add(new WndArtItemViewModel(name, tooltip, bmp));
        }

        FilteredArtItems = items;
        QueueArtItemThumbnailsLoad(items);
    }

    private WndWindow? ResolveTargetWindowAt(Point? canvasPosition)
    {
        if (canvasPosition == null)
        {
            return null;
        }

        var pos = canvasPosition.Value;
        for (var i = CanvasItems.Count - 1; i >= 0; i--)
        {
            var item = CanvasItems[i];
            if (!item.CanvasVisible)
            {
                continue;
            }

            if (pos.X >= item.X && pos.X <= item.X + item.Width &&
                pos.Y >= item.Y && pos.Y <= item.Y + item.Height)
            {
                return item.Window;
            }
        }

        return null;
    }

    private async Task<string?> ResolveMappedNameForDroppedPathsAsync(
        IReadOnlyList<string> paths,
        string? projectDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var path in paths)
        {
            var stem = Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrEmpty(projectDirectory))
            {
                var importResult = await textureImportService.ImportTextureAsync(path, projectDirectory, null, cancellationToken).ConfigureAwait(false);
                if (importResult.Success && importResult.Data != null)
                {
                    return importResult.Data.MappedName;
                }

                if (KnownImageNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
                {
                    return stem;
                }
            }
            else
            {
                return stem;
            }
        }

        return null;
    }

    private static Dictionary<string, Bitmap> DecodeThumbnails(IReadOnlyDictionary<string, byte[]> imageData)
    {
        var decoded = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        foreach (var (k, bytes) in imageData)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                decoded[k] = new Bitmap(ms);
            }
            catch
            {
                // Skip decode errors on corrupt bytes
            }
        }

        return decoded;
    }

    private void ApplyThumbnailsToItems(
        Dictionary<string, Bitmap> decoded,
        IReadOnlyList<WndArtItemViewModel> items)
    {
        foreach (var (k, bmp) in decoded)
        {
            if (!_thumbnailBitmaps.TryAdd(k, bmp))
            {
                bmp.Dispose();
            }
        }

        foreach (var item in items)
        {
            if (item.Thumbnail == null && _thumbnailBitmaps.TryGetValue(item.Name, out var bmp))
            {
                item.Thumbnail = bmp;
            }
        }
    }

    private async Task LoadThumbnailsBackgroundAsync(
        IReadOnlyList<string> missingNames,
        AssetRoots roots,
        string? projectDirectory,
        IReadOnlyList<string> linkedBigs,
        IReadOnlyList<WndArtItemViewModel> items,
        CancellationToken cancellationToken)
    {
        try
        {
            const int batchSize = 50;
            for (var i = 0; i < missingNames.Count; i += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = missingNames.Skip(i).Take(batchSize).ToList();

                var result = await assetService.Images.GetImagesAsync(
                    batch,
                    roots.BaseRoot,
                    null,
                    projectDirectory,
                    linkedBigs,
                    roots.IsZeroHour,
                    cancellationToken).ConfigureAwait(false);

                if (!result.Success || result.Data == null || result.Data.Count == 0 || cancellationToken.IsCancellationRequested)
                {
                    continue;
                }

                var decoded = DecodeThumbnails(result.Data);
                if (decoded.Count > 0 && !cancellationToken.IsCancellationRequested)
                {
                    await InvokeOnUIThreadAsync(() => ApplyThumbnailsToItems(decoded, items)).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected cancellation
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to load art item thumbnails in background");
        }
    }

    private void QueueArtItemThumbnailsLoad(IReadOnlyList<WndArtItemViewModel> items)
    {
        var missingNames = items
            .Where(item => item.Thumbnail == null)
            .Select(item => item.Name)
            .ToList();

        if (missingNames.Count == 0 || SelectedAssetInstallation == null)
        {
            return;
        }

        var roots = ResolveAssetRoots(SelectedAssetInstallation);
        var detectedProjectDir = ResolveProjectDirectory(FilePath, roots) ?? ResolveProjectDirectory(FilesDirectory, roots);
        var projectDirectory = CombineProjectDirectories(LinkedModFolder, detectedProjectDir);
        var linkedBigs = LinkedBigFiles.ToList();

        CancellationTokenSource cts = new();
        CancellationTokenSource? toCancel;
        lock (_thumbnailSync)
        {
            toCancel = _thumbnailCts;
            _thumbnailCts = cts;
        }

        CancelAndDisposeCts(toCancel);
        var cancellationToken = cts.Token;

        _ = Task.Run(() => LoadThumbnailsBackgroundAsync(missingNames, roots, projectDirectory, linkedBigs, items, cancellationToken), cancellationToken);
    }

    private string FormatMissingNames(IReadOnlyList<string> missing)
    {
        var shown = missing.Take(WndConstants.Preview.MaxMissingTooltipNames).ToList();
        var text = string.Join(", ", shown);
        if (missing.Count > shown.Count)
        {
            text = string.Concat(text, ", ", Localization.GetString("Tools.WndEditor.Assets.MissingMore", missing.Count - shown.Count));
        }

        return text;
    }

    private static string? ResolveProjectDirectory(string? filePath, AssetRoots roots)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            return null;
        }

        var directory = Directory.Exists(filePath) ? filePath : Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        if (IsPathUnder(filePath, roots.BaseRoot))
        {
            return directory;
        }

        return FindModRoot(directory) ?? directory;
    }

    private static string? FindModRoot(string directory)
    {
        var current = directory;
        string? candidateWithGameFolders = null;
        for (var depth = 0; depth < 8 && !string.IsNullOrEmpty(current); depth++)
        {
            try
            {
                if (Directory.GetFiles(current, "*.mbproj").Length > 0)
                {
                    return current;
                }

                if (Directory.Exists(Path.Combine(current, ModBuilderConstants.GameFilesEditedDir)))
                {
                    return current;
                }

                if (candidateWithGameFolders == null &&
                    (HasMarkerDirectory(current, WndConstants.StringTables.DataDirectory)
                    || HasMarkerDirectory(current, WndConstants.ModRoots.WindowFolder)
                    || HasMarkerDirectory(current, WndConstants.MappedImages.ArtFolder)
                    || HasMarkerDirectory(current, WndConstants.ControlBarScheme.IniDirectory)))
                {
                    candidateWithGameFolders = current;
                }
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }

            current = Path.GetDirectoryName(current) ?? string.Empty;
        }

        return candidateWithGameFolders;
    }

    private static bool HasMarkerDirectory(string parent, string name)
    {
        if (Directory.Exists(Path.Combine(parent, name)))
        {
            return true;
        }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(parent))
            {
                var folderName = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(folderName, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private IReadOnlyDictionary<string, string> ResolveSchemeOverrides(
        AssetRoots roots,
        string? projectDirectory,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? additionalBigFiles = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WndConstants.ControlBarScheme.BackgroundMarkerKey] = roots.IsZeroHour ? WndConstants.ControlBarScheme.DefaultAmericaBaseZeroHour : WndConstants.ControlBarScheme.DefaultAmericaBaseGenerals,
            [WndConstants.ControlBarScheme.RightHUDKey] = WndConstants.ControlBarScheme.DefaultRightHudImageName,
            [WndConstants.ControlBarScheme.ButtonOptionsKey] = WndConstants.ControlBarScheme.DefaultButtonOptionsImageName,
            [WndConstants.ControlBarScheme.ButtonIdleWorkerKey] = WndConstants.ControlBarScheme.DefaultButtonIdleWorkerImageName,
            [WndConstants.ControlBarScheme.ButtonChatKey] = WndConstants.ControlBarScheme.DefaultButtonChatImageName,
            [WndConstants.ControlBarScheme.ButtonPlaceBeaconKey] = WndConstants.ControlBarScheme.DefaultButtonPlaceBeaconImageName,
            [WndConstants.ControlBarScheme.ButtonGeneralKey] = WndConstants.ControlBarScheme.DefaultButtonGeneralImageName,
            [WndConstants.ControlBarScheme.ButtonUAttackKey] = WndConstants.ControlBarScheme.DefaultButtonUAttackImageName,
            [WndConstants.ControlBarScheme.ExpBarForegroundKey] = WndConstants.ControlBarScheme.DefaultExpBarForegroundImageName,
            [WndConstants.ControlBarScheme.QueueButtonImageKey] = WndConstants.ControlBarScheme.DefaultQueueButtonImageName,
        };

        try
        {
            var fs = WndGameFileSystem.Open(roots.BaseRoot, null, projectDirectory, logger, additionalBigFiles, roots.IsZeroHour, cancellationToken);
            var iniBytes = fs.Read(WndConstants.ControlBarScheme.DataIniPath) ?? fs.Read(WndConstants.ControlBarScheme.IniPath);
            if (iniBytes != null && iniBytes.Length > 0)
            {
                var text = Encoding.UTF8.GetString(iniBytes);
                ParseControlBarSchemeIni(text, result);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to read ControlBarScheme.ini; using standard fallback scheme");
        }

        return result;
    }

    private static string? CombineProjectDirectories(string? linkedDir, string? autoDetectedDir)
    {
        if (string.IsNullOrWhiteSpace(linkedDir))
        {
            return autoDetectedDir;
        }

        if (string.IsNullOrWhiteSpace(autoDetectedDir))
        {
            return linkedDir;
        }

        var p1 = Path.GetFullPath(linkedDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var p2 = Path.GetFullPath(autoDetectedDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(p1, p2, PathHelper.PathComparison))
        {
            return linkedDir;
        }

        return $"{linkedDir};{autoDetectedDir}";
    }
}
