using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Extensions.GameInstallations;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.GameInstallations;
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
    ILogger<WndEditorViewModel> logger) : ObservableObject, IDisposable
{
    private const int MaxUndoHistory = 200;

    private static readonly EnumerationOptions SafeDirectoryEnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.ReparsePoint | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    private static Cursor? _handCursor;

    private static Cursor HandCursor => _handCursor ??= new(StandardCursorType.Hand);

    private readonly Stack<WndEditAction> _undoStack = new();
    private readonly Stack<WndEditAction> _redoStack = new();
    private readonly Dictionary<string, Bitmap> _composedBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, WndWindow> _windowParents = new();
    private readonly object _previewSync = new();
    private readonly object _linkedAssetsSync = new();
    private int _historyVersion;
    private int _savedHistoryVersion;
    private WndDocument? _document;
    private double _canvasBaseWidth = WndConstants.Editor.MinCanvasWidth;
    private double _canvasBaseHeight = WndConstants.Editor.MinCanvasHeight;
    private WndCanvasItemViewModel? _dragItem;
    private Point _dragStart;
    private WndScreenRect? _dragOriginal;
    private bool _isResizing;
    private WndResizeDirection _resizeDirection = WndResizeDirection.None;
    private IReadOnlyList<GameInstallation> _installations = [];
    private Dictionary<string, Bitmap> _previewBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Bitmap> _thumbnailBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, byte[]> _previewPngs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _resolvedStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _schemeOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private WndRuntimeArt _runtimeArt = WndRuntimeArt.Empty;
    private CancellationTokenSource? _previewCts;
    private int _previewGeneration;
    private bool _installationsLoaded;

    /// <summary>
    /// Gets the root tree nodes of the edited document.
    /// </summary>
    public ObservableCollection<WndTreeNodeViewModel> RootNodes { get; } = [];

    /// <summary>
    /// Gets the window definition files listed in the explorer.
    /// </summary>
    public ObservableCollection<WndFileTreeNodeViewModel> Files { get; } = [];

    /// <summary>
    /// Gets the directory name for display in the explorer.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Observable property dependent on FilesDirectory")]
    public string? FilesDirectoryName
    {
        get
        {
            if (string.IsNullOrEmpty(FilesDirectory))
            {
                return null;
            }

            var trimmed = FilesDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fileName = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(fileName) ? FilesDirectory : fileName;
        }
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
    public string DocumentTitle
    {
        get
        {
            var name = FilePath == null
                ? localizationService.GetString("Tools.WndEditor.Document.Untitled")
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
    /// Gets the zoom factor as a display percentage.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public string ZoomDisplayText => $"{Zoom:P0}";

    /// <summary>
    /// Gets the canvas cursor, showing a hand while the pan tool is active.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to UI in Avalonia XAML")]
    public Cursor? CanvasCursor => IsPanMode ? HandCursor : null;

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
    /// Gets or sets the canvas zoom factor.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasWidth))]
    [NotifyPropertyChangedFor(nameof(CanvasHeight))]
    [NotifyPropertyChangedFor(nameof(CanvasContentOffset))]
    [NotifyPropertyChangedFor(nameof(ZoomDisplayText))]
    [NotifyPropertyChangedFor(nameof(ScreenGuideX))]
    [NotifyPropertyChangedFor(nameof(ScreenGuideY))]
    [NotifyPropertyChangedFor(nameof(ScreenGuideWidth))]
    [NotifyPropertyChangedFor(nameof(ScreenGuideHeight))]
    private double _zoom = WndConstants.Editor.DefaultZoom;

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

    /// <summary>
    /// Gets or sets the directory listed in the file explorer.
    /// </summary>
    [ObservableProperty]
    private string? _filesDirectory;

    /// <summary>
    /// Gets or sets whether a document is open.
    /// </summary>
    [ObservableProperty]
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
    /// Gets a value indicating whether undo is available.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether redo is available.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

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
            notificationService.ShowError(
                localizationService.GetString("Tools.WndEditor.Open.FailureTitle"),
                localizationService.GetString("Tools.WndEditor.Open.FailureMessage", result.FirstError ?? filePath),
                NotificationDurations.Long);
            return false;
        }

        await InvokeOnUIThreadAsync(() => AdoptDocument(result.Data, filePath)).ConfigureAwait(false);
        await EnsureInstallationsLoadedAsync(cancellationToken).ConfigureAwait(false);
        RefreshAssetPreviews();
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
        LeftSidebarTabIndex = 1;

        if (HasDocument && !string.IsNullOrEmpty(FilePath) && IsSubPathOf(FilePath, folderPath))
        {
            UpdateCurrentFileNode(FilePath);
            return true;
        }

        var firstWnd = FindFirstWndFilePath(Files);
        if (!string.IsNullOrEmpty(firstWnd))
        {
            return await OpenFileAsync(firstWnd, cancellationToken).ConfigureAwait(false);
        }

        notificationService.ShowInfo(
            localizationService.GetString("Tools.WndEditor.Files.NoWndFilesTitle"),
            localizationService.GetString("Tools.WndEditor.Files.NoWndFilesMessage"),
            NotificationDurations.Medium);

        return true;
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
            notificationService.ShowError(
                localizationService.GetString("Tools.WndEditor.Open.FailureTitle"),
                localizationService.GetString("Tools.WndEditor.Open.FailureMessage", result.FirstError ?? string.Empty),
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
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Apply.NoSelectionTitle"),
                localizationService.GetString("Tools.WndEditor.Apply.NoSelectionMessage"),
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
        var projectDirectory = ResolveImportProjectDirectory(LinkedModFolder, SelectedAssetInstallation, roots, FilePath, FilesDirectory);

        string? mappedNameToApply = null;

        foreach (var path in validPaths)
        {
            var stem = Path.GetFileNameWithoutExtension(path);

            if (!string.IsNullOrEmpty(projectDirectory))
            {
                var importResult = await textureImportService.ImportTextureAsync(path, projectDirectory, null, cancellationToken).ConfigureAwait(false);
                if (importResult.Success && importResult.Data != null)
                {
                    mappedNameToApply = importResult.Data.MappedName;
                    MissingImageNames.Remove(mappedNameToApply);
                }
                else if (KnownImageNames.Contains(stem, StringComparer.OrdinalIgnoreCase))
                {
                    mappedNameToApply = stem;
                }
            }
            else
            {
                mappedNameToApply = stem;
            }

            if (!string.IsNullOrEmpty(mappedNameToApply))
            {
                break;
            }
        }

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
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Apply.NoSelectionTitle"),
                localizationService.GetString("Tools.WndEditor.Apply.NoSelectionMessage"),
                NotificationDurations.Medium);
            return;
        }

        SelectWindow(targetWindow);
        ApplyImageToSelectedWindow(imageName.Trim());
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
        _resizeDirection = WndResizeDirection.None;
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
    public void BeginCanvasResize(WndCanvasItemViewModel? item, WndResizeDirection direction, Point canvasPoint)
    {
        _dragItem = null;
        _dragOriginal = null;
        _isResizing = false;
        _resizeDirection = WndResizeDirection.None;

        if (item == null || direction == WndResizeDirection.None || !item.Window.TryGetScreenRect(out var rect) || rect == null)
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
        _resizeDirection = WndResizeDirection.None;

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
            ? localizationService.GetString("Tools.WndEditor.History.ResizeWindow")
            : localizationService.GetString("Tools.WndEditor.History.MoveWindow");

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

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_previewSync)
        {
            CancelAndDisposeCts(_previewCts);
            _previewCts = null;
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
            var rawMarker = "/" + ModBuilderConstants.RawBundleItemsSubdir + "/";
            var rawIndex = normalized.IndexOf(rawMarker, StringComparison.OrdinalIgnoreCase);
            if (rawIndex >= 0)
            {
                var beforeMarker = filePath[..rawIndex];
                var afterMarker = filePath[(rawIndex + rawMarker.Length)..];

                var projectDir = Path.GetDirectoryName(beforeMarker);
                if (!string.IsNullOrEmpty(projectDir))
                {
                    var candidateSource = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, afterMarker);
                    if (File.Exists(candidateSource) || Directory.Exists(Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir)))
                    {
                        return candidateSource;
                    }
                }
            }

            var buildMarker = "/" + ModBuilderConstants.DefaultBuildDir + "/";
            var buildIndex = normalized.IndexOf(buildMarker, StringComparison.OrdinalIgnoreCase);
            if (buildIndex >= 0)
            {
                var projectDir = filePath[..buildIndex];
                var afterBuild = filePath[(buildIndex + buildMarker.Length)..];
                if (!string.IsNullOrEmpty(projectDir))
                {
                    var candidateSource = Path.Combine(projectDir, ModBuilderConstants.GameFilesEditedDir, afterBuild);
                    if (File.Exists(candidateSource))
                    {
                        return candidateSource;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            // Ignore path inspection errors and return original path
        }

        return filePath;
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

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return null;
        }

        return TopLevel.GetTopLevel(lifetime.MainWindow);
    }

    private void UpdateCanvasResize(Point canvasPoint)
    {
        if (_dragItem == null || _dragOriginal == null)
        {
            return;
        }

        var deltaX = (int)Math.Round((canvasPoint.X - _dragStart.X) / Zoom);
        var deltaY = (int)Math.Round((canvasPoint.Y - _dragStart.Y) / Zoom);

        var minDimension = WndConstants.Editor.MinResizeDimension;
        var left = _dragOriginal.UpperLeftX;
        var top = _dragOriginal.UpperLeftY;
        var right = _dragOriginal.BottomRightX;
        var bottom = _dragOriginal.BottomRightY;

        switch (_resizeDirection)
        {
            case WndResizeDirection.East:
                right = Math.Max(left + minDimension, _dragOriginal.BottomRightX + deltaX);
                break;
            case WndResizeDirection.West:
                left = Math.Min(right - minDimension, _dragOriginal.UpperLeftX + deltaX);
                break;
            case WndResizeDirection.South:
                bottom = Math.Max(top + minDimension, _dragOriginal.BottomRightY + deltaY);
                break;
            case WndResizeDirection.North:
                top = Math.Min(bottom - minDimension, _dragOriginal.UpperLeftY + deltaY);
                break;
            case WndResizeDirection.SouthEast:
                right = Math.Max(left + minDimension, _dragOriginal.BottomRightX + deltaX);
                bottom = Math.Max(top + minDimension, _dragOriginal.BottomRightY + deltaY);
                break;
            case WndResizeDirection.NorthEast:
                right = Math.Max(left + minDimension, _dragOriginal.BottomRightX + deltaX);
                top = Math.Min(bottom - minDimension, _dragOriginal.UpperLeftY + deltaY);
                break;
            case WndResizeDirection.SouthWest:
                left = Math.Min(right - minDimension, _dragOriginal.UpperLeftX + deltaX);
                bottom = Math.Max(top + minDimension, _dragOriginal.BottomRightY + deltaY);
                break;
            case WndResizeDirection.NorthWest:
                left = Math.Min(right - minDimension, _dragOriginal.UpperLeftX + deltaX);
                top = Math.Min(bottom - minDimension, _dragOriginal.UpperLeftY + deltaY);
                break;
            default:
                break;
        }

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
    /// Creates a new untitled document.
    /// </summary>
    [RelayCommand]
    private async Task NewDocumentAsync(CancellationToken cancellationToken = default)
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

    /// <summary>
    /// Opens a folder containing window definition files and loads its tree into the file explorer.
    /// </summary>
    [RelayCommand]
    private async Task OpenFolderWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = localizationService.GetString("Tools.WndEditor.FileDialog.FolderTitle"),
            AllowMultiple = false,
        });
        if (folders.Count == 0)
        {
            return;
        }

        var localPath = folders[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(localPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await OpenFolderAsync(localPath, cancellationToken);
        }
    }

    /// <summary>
    /// Opens a window definition file chosen with a file dialog.
    /// </summary>
    [RelayCommand]
    private async Task OpenFileWithDialogAsync(CancellationToken cancellationToken = default)
    {
        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = localizationService.GetString("Tools.WndEditor.FileDialog.OpenTitle"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WndEditor.FileDialog.FilterName"))
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
            Title = localizationService.GetString("Tools.WndEditor.Assets.LinkModFolderTitle"),
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
            Title = localizationService.GetString("Tools.WndEditor.Assets.LinkBigArchiveTitle"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WndEditor.Assets.BigArchiveFilter"))
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
        var projectDirectory = ResolveImportProjectDirectory(LinkedModFolder, SelectedAssetInstallation, roots, FilePath, FilesDirectory);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Import.NoProjectTitle"),
                localizationService.GetString("Tools.WndEditor.Import.NoProjectMessage"),
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
            Title = localizationService.GetString("Tools.WndEditor.Import.DialogTitle"),
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WndEditor.Import.FilterName"))
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
                    notificationService.ShowError(
                        localizationService.GetString("Tools.WndEditor.Import.FailureTitle"),
                        localizationService.GetString("Tools.WndEditor.Import.FailureMessage", Path.GetFileName(localPath), result.FirstError ?? string.Empty),
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
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WndEditor.Import.SuccessTitle"),
                localizationService.GetString("Tools.WndEditor.Import.SuccessMessage", imported, projectDirectory),
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
        var projectDirectory = ResolveImportProjectDirectory(LinkedModFolder, SelectedAssetInstallation, roots, FilePath, FilesDirectory);
        if (string.IsNullOrEmpty(projectDirectory))
        {
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Import.NoProjectTitle"),
                localizationService.GetString("Tools.WndEditor.Import.NoProjectMessage"),
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
            Title = localizationService.GetString("Tools.WndEditor.Import.DialogTitleFor", mappedName),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WndEditor.Import.FilterName"))
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
                notificationService.ShowSuccess(
                    localizationService.GetString("Tools.WndEditor.Import.SuccessTitle"),
                    localizationService.GetString("Tools.WndEditor.Import.SuccessMessage", 1, projectDirectory),
                    NotificationDurations.Medium);
                MissingImageNames.Remove(result.Data.MappedName);
                assetService.InvalidateCache();
                RefreshAssetPreviews();
            }
            else
            {
                notificationService.ShowError(
                    localizationService.GetString("Tools.WndEditor.Import.FailureTitle"),
                    localizationService.GetString("Tools.WndEditor.Import.FailureMessage", Path.GetFileName(localPath), result.FirstError ?? string.Empty),
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
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Apply.NoSelectionTitle"),
                localizationService.GetString("Tools.WndEditor.Apply.NoSelectionMessage"),
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
        notificationService.ShowSuccess(
            localizationService.GetString("Tools.WndEditor.Apply.SuccessTitle"),
            localizationService.GetString("Tools.WndEditor.Apply.SuccessMessage", name, node.DisplayName),
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
        GameInstallationOption? selection,
        AssetRoots? roots,
        string? filePath,
        string? filesDirectory)
    {
        if (!string.IsNullOrWhiteSpace(linkedModFolder))
        {
            return linkedModFolder;
        }

        if (selection == null || roots == null)
        {
            return null;
        }

        return ResolveProjectDirectory(filePath, roots) ?? ResolveProjectDirectory(filesDirectory, roots);
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
            parts.Add(localizationService.GetString("Tools.WndEditor.Assets.LinkedModSummary", folderName));
            tooltipLines.Add(localizationService.GetString("Tools.WndEditor.Assets.LinkedTooltipModPrefix", modFolder));
        }

        if (bigFiles.Count > 0)
        {
            parts.Add(localizationService.GetString("Tools.WndEditor.Assets.LinkedBigSummary", bigFiles.Count));
            tooltipLines.AddRange(bigFiles.Select(b => localizationService.GetString("Tools.WndEditor.Assets.LinkedTooltipBigPrefix", b)));
        }

        var separator = localizationService.GetString("Tools.WndEditor.Assets.LinkedSummarySeparator");
        LinkedAssetsSummary = string.Join(separator, parts);
        LinkedAssetsTooltip = string.Join("\n", tooltipLines);
    }

    /// <summary>
    /// Saves the open document to its file, asking for a path when untitled.
    /// </summary>
    [RelayCommand]
    private async Task SaveFileAsync(CancellationToken cancellationToken = default)
    {
        if (_document == null)
        {
            return;
        }

        SelectedProperties?.FlushPendingEdits();

        if (string.IsNullOrEmpty(FilePath))
        {
            await SaveFileAsWithDialogAsync(cancellationToken);
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
                await WriteDocumentToFileAsync(oldPath, cancellationToken);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                logger.LogDebug(ex, "Failed to mirror saved file to transient build path {Path}", oldPath);
            }
        }
    }

    /// <summary>
    /// Saves the open document to a path chosen with a file dialog.
    /// </summary>
    [RelayCommand]
    private async Task SaveFileAsWithDialogAsync(CancellationToken cancellationToken = default)
    {
        if (_document == null)
        {
            return;
        }

        SelectedProperties?.FlushPendingEdits();

        var topLevel = GetTopLevel();
        if (topLevel == null)
        {
            return;
        }

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = localizationService.GetString("Tools.WndEditor.FileDialog.SaveTitle"),
            SuggestedFileName = FilePath == null ? null : Path.GetFileName(FilePath),
            FileTypeChoices =
            [
                new FilePickerFileType(localizationService.GetString("Tools.WndEditor.FileDialog.FilterName"))
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

        var target = FilePath ?? localizationService.GetString("Tools.WndEditor.Document.Untitled");
        var result = wndDocumentService.ValidateDocument(_document, target);
        if (result.IsValid)
        {
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WndEditor.Validate.SuccessTitle"),
                localizationService.GetString("Tools.WndEditor.Validate.SuccessMessage"),
                NotificationDurations.Medium);
        }
        else
        {
            var first = result.Issues.Count > 0 ? result.Issues[0].Message : string.Empty;
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Validate.IssuesTitle"),
                localizationService.GetString("Tools.WndEditor.Validate.IssuesMessage", result.CriticalIssueCount, result.WarningIssueCount, first),
                NotificationDurations.Long);
        }
    }

    /// <summary>
    /// Undoes the most recent edit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
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

    /// <summary>
    /// Redoes the most recently undone edit.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
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

    /// <summary>
    /// Zooms the canvas in one step.
    /// </summary>
    [RelayCommand]
    private void ZoomIn()
    {
        Zoom = Math.Min(Zoom * WndConstants.Editor.ZoomStepFactor, WndConstants.Editor.MaxZoom);
    }

    /// <summary>
    /// Zooms the canvas out one step.
    /// </summary>
    [RelayCommand]
    private void ZoomOut()
    {
        Zoom = Math.Max(Zoom / WndConstants.Editor.ZoomStepFactor, WndConstants.Editor.MinZoom);
    }

    /// <summary>
    /// Resets the canvas zoom to the default factor.
    /// </summary>
    [RelayCommand]
    private void ResetZoom()
    {
        Zoom = WndConstants.Editor.DefaultZoom;
        CanvasFramingRequested?.Invoke(this, EventArgs.Empty);
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
            localizationService.GetString("Tools.WndEditor.History.AddWindow"),
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
    }

    /// <summary>
    /// Deletes the selected window.
    /// </summary>
    [RelayCommand]
    private void DeleteSelectedWindow()
    {
        if (_document == null || SelectedNode == null)
        {
            return;
        }

        var node = SelectedNode;
        var siblings = node.Parent == null ? _document.Windows : node.Parent.Window.Children;
        var index = siblings.IndexOf(node.Window);
        siblings.Remove(node.Window);
        PushUndo(new WndEditAction(
            localizationService.GetString("Tools.WndEditor.History.DeleteWindow"),
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

    /// <summary>
    /// Opens a file chosen in the explorer.
    /// </summary>
    /// <param name="file">The file tree node to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task OpenExplorerFileAsync(WndFileTreeNodeViewModel? file, CancellationToken cancellationToken = default)
    {
        if (file == null || file.IsDirectory)
        {
            return;
        }

        await OpenFileAsync(file.FullPath, cancellationToken);
    }

    /// <summary>
    /// Toggles the expanded state of a directory node.
    /// </summary>
    /// <param name="node">The directory node.</param>
    [RelayCommand]
    private void ToggleFileDirectory(WndFileTreeNodeViewModel? node)
    {
        if (node != null && node.IsDirectory)
        {
            node.IsExpanded = !node.IsExpanded;
        }
    }

    /// <summary>
    /// Expands every file tree node.
    /// </summary>
    [RelayCommand]
    private void ExpandAllFiles()
    {
        SetFileNodesExpanded(Files, true);
    }

    /// <summary>
    /// Collapses every file tree node.
    /// </summary>
    [RelayCommand]
    private void CollapseAllFiles()
    {
        SetFileNodesExpanded(Files, false);
    }

    /// <summary>
    /// Chooses the directory listed in the file explorer.
    /// </summary>
    [RelayCommand]
    private async Task BrowseFilesDirectoryAsync(CancellationToken cancellationToken = default)
    {
        await OpenFolderWithDialogAsync(cancellationToken);
    }

    private static string? FindFirstWndFilePath(IEnumerable<WndFileTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile)
            {
                return node.FullPath;
            }

            var childFile = FindFirstWndFilePath(node.Children);
            if (childFile != null)
            {
                return childFile;
            }
        }

        return null;
    }

    /// <summary>
    /// Refreshes the file explorer listing.
    /// </summary>
    [RelayCommand]
    private void RefreshFiles()
    {
        Files.Clear();
        if (string.IsNullOrEmpty(FilesDirectory) || !Directory.Exists(FilesDirectory))
        {
            return;
        }

        try
        {
            var rootDirInfo = new DirectoryInfo(FilesDirectory);
            var rootNode = BuildDirectoryNode(rootDirInfo, FilePath, null);
            if (rootNode != null)
            {
                Files.Add(rootNode);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Failed to list window definition files in {Directory}", FilesDirectory);
        }
    }

    /// <summary>
    /// Recursively builds a file tree node for the given directory, skipping directories with no .wnd files.
    /// </summary>
    private WndFileTreeNodeViewModel? BuildDirectoryNode(
        DirectoryInfo directoryInfo,
        string? currentPath,
        WndFileTreeNodeViewModel? parent,
        int depth = 0)
    {
        if (depth > 20)
        {
            return null;
        }

        if (parent != null &&
            (directoryInfo.Name.StartsWith('.') ||
             string.Equals(directoryInfo.Name, ModBuilderConstants.DefaultBuildDir, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(directoryInfo.Name, ModBuilderConstants.DefaultReleaseDir, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var node = new WndFileTreeNodeViewModel(
            directoryInfo.Name,
            directoryInfo.FullName,
            isDirectory: true,
            isCurrent: false,
            parent: parent);

        try
        {
            var subDirectories = directoryInfo
                .EnumerateDirectories("*", SafeDirectoryEnumerationOptions)
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var subDir in subDirectories)
            {
                var childNode = BuildDirectoryNode(subDir, currentPath, node, depth + 1);
                if (childNode != null && childNode.Children.Count > 0)
                {
                    node.AddChild(childNode);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "Access denied enumerating subdirectories in {Path}", directoryInfo.FullName);
        }

        try
        {
            var files = directoryInfo
                .EnumerateFiles(ModBuilderConstants.FileNames.WndSearchPattern)
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase);

            foreach (var file in files)
            {
                var isCurrent = string.Equals(file.FullName, currentPath, StringComparison.OrdinalIgnoreCase);
                var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file.Name);
                var fileNode = new WndFileTreeNodeViewModel(
                    fileNameWithoutExtension,
                    file.FullName,
                    isDirectory: false,
                    isCurrent: isCurrent,
                    parent: node);

                node.AddChild(fileNode);
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            logger.LogWarning(ex, "Access denied enumerating files in {Path}", directoryInfo.FullName);
        }

        if (node.Children.Count == 0)
        {
            return null;
        }

        return node;
    }

    private static void SetFileNodesExpanded(IEnumerable<WndFileTreeNodeViewModel> nodes, bool expanded)
    {
        foreach (var node in nodes)
        {
            if (node.IsDirectory)
            {
                node.IsExpanded = expanded;
                SetFileNodesExpanded(node.Children, expanded);
            }
        }
    }

    private void UpdateCurrentFileNode(string? currentPath)
    {
        UpdateNodesCurrentState(Files, currentPath);
    }

    private static void UpdateNodesCurrentState(IEnumerable<WndFileTreeNodeViewModel> nodes, string? currentPath)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile)
            {
                node.IsCurrent = string.Equals(node.FullPath, currentPath, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                UpdateNodesCurrentState(node.Children, currentPath);
            }
        }
    }

    private static bool IsSubPathOf(string path, string basePath)
    {
        try
        {
            var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return normalizedPath.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalizedPath, normalizedBase, StringComparison.OrdinalIgnoreCase);
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

    partial void OnFilesDirectoryChanged(string? value)
    {
        OnPropertyChanged(nameof(FilesDirectoryName));
        if (!string.IsNullOrEmpty(value))
        {
            RefreshFiles();
        }
        else
        {
            Files.Clear();
        }
    }

    partial void OnZoomChanged(double value)
    {
        if (value < WndConstants.Editor.MinZoom || value > WndConstants.Editor.MaxZoom)
        {
            Zoom = Math.Clamp(value, WndConstants.Editor.MinZoom, WndConstants.Editor.MaxZoom);
            return;
        }

        RebuildCanvas();
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

    private async Task<bool> ConfirmDiscardUnsavedAsync(CancellationToken cancellationToken)
    {
        if (!IsModified || !HasDocument)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await dialogService.ShowConfirmationAsync(
            localizationService.GetString("Tools.WndEditor.UnsavedChanges.Title"),
            localizationService.GetString("Tools.WndEditor.UnsavedChanges.Message"),
            localizationService.GetString("Tools.WndEditor.UnsavedChanges.Discard"),
            localizationService.GetString("Tools.WndEditor.UnsavedChanges.Cancel"));
    }

    private async Task<bool> WriteDocumentToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_document == null)
        {
            return false;
        }

        string? tempPath = null;
        try
        {
            var text = wndDocumentService.WriteDocument(_document);
            var directory = Path.GetDirectoryName(filePath);
            tempPath = Path.Combine(string.IsNullOrEmpty(directory) ? Path.GetTempPath() : directory, Path.GetRandomFileName());
            await File.WriteAllTextAsync(tempPath, text, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
            tempPath = null;
            _savedHistoryVersion = _historyVersion;
            IsModified = false;
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WndEditor.Save.SuccessTitle"),
                localizationService.GetString("Tools.WndEditor.Save.SuccessMessage", Path.GetFileName(filePath)),
                NotificationDurations.Medium);
            logger.LogInformation("Saved window definition file {Path}", filePath);
            return true;
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to save window definition file {Path}", filePath);
            notificationService.ShowError(
                localizationService.GetString("Tools.WndEditor.Save.FailureTitle"),
                localizationService.GetString("Tools.WndEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied saving window definition file {Path}", filePath);
            notificationService.ShowError(
                localizationService.GetString("Tools.WndEditor.Save.FailureTitle"),
                localizationService.GetString("Tools.WndEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
            return false;
        }
        finally
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    logger.LogDebug(ex, "Failed to clean up temporary file {Path}", tempPath);
                }
            }
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
        RefreshUndoCommands();
        ClearComposedBitmaps();
        SyncFilesDirectory(filePath);
        AutoSelectAssetInstallation();
        RebuildAll();
        RefreshAssetStatus();
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
        if (string.IsNullOrEmpty(FilesDirectory) || !Directory.Exists(FilesDirectory))
        {
            FilesDirectory = directory;
        }
        else if (IsSubPathOf(filePath, FilesDirectory))
        {
            UpdateCurrentFileNode(filePath);
        }
        else if (!string.IsNullOrEmpty(directory))
        {
            FilesDirectory = directory;
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
        _redoStack.Clear();
        _historyVersion++;
        IsModified = _historyVersion != _savedHistoryVersion;
        RefreshUndoCommands();
    }

    private void RefreshUndoCommands()
    {
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
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
                notificationService,
                localizationService,
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
            localizationService.GetString("Tools.WndEditor.History.EditProperty", key),
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
            localizationService.GetString("Tools.WndEditor.History.DeleteProperty", key),
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
            localizationService.GetString("Tools.WndEditor.History.ApplyRawText"),
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
            string.Equals(option.Path, installation.ZeroHourPath, StringComparison.OrdinalIgnoreCase));
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
            return fullFile.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
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
                && string.Equals(selection.Path, installation.ZeroHourPath, StringComparison.OrdinalIgnoreCase))
            {
                return ResolveZeroHourAssetRoots(installation);
            }

            if (!string.IsNullOrEmpty(installation.GeneralsPath)
                && string.Equals(selection.Path, installation.GeneralsPath, StringComparison.OrdinalIgnoreCase))
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
            return localizationService.GetString("Tools.WndEditor.Canvas.MapPreviewPlaceholder");
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
            _schemeOverrides = schemeOverrides;
            var medalsResult = await medalService.GetMedalsAsync(roots.BaseRoot, null, projectDirectory, linkedBigs, roots.IsZeroHour, cancellationToken).ConfigureAwait(false);
            var medals = medalsResult.Success && medalsResult.Data != null ? medalsResult.Data : null;
            var runtimeArt = BuildRuntimeArt(document, medals);
            logger.LogDebug(
                "Runtime art for {File}: {Medals} medal images, {Hidden} shell-hidden windows",
                document.SourcePath == null ? "untitled" : Path.GetFileName(document.SourcePath),
                runtimeArt.MedalImages.Count,
                runtimeArt.HiddenWindows.Count);
            var names = CollectPreviewImageNames(document, _schemeOverrides, runtimeArt);
            var labels = CollectPreviewLabels(document, _schemeOverrides, runtimeArt);
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
            await InvokeOnUIThreadAsync(() => ApplyPreviews(bitmaps, values, generation, labels, knownNames, runtimeArt)).ConfigureAwait(false);
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
        WndRuntimeArt? runtimeArt = null)
    {
        if (generation != _previewGeneration)
        {
            return;
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
            AssetStatusText = localizationService.GetString("Tools.WndEditor.Assets.SelectorWatermark");
            AssetStatusTooltip = null;
            SyncMissingImageNames([]);
            return;
        }

        var names = CollectPreviewImageNames(_document, _schemeOverrides, _runtimeArt);
        if (names.Count == 0)
        {
            AssetStatusText = localizationService.GetString("Tools.WndEditor.Assets.EmptyStatus");
            AssetStatusTooltip = null;
            SyncMissingImageNames([]);
            return;
        }

        var missing = names.Where(name => !_previewBitmaps.ContainsKey(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        SyncMissingImageNames(missing);
        AssetStatusText = localizationService.GetString("Tools.WndEditor.Assets.ResolvedStatus", names.Count - missing.Count, names.Count);
        AssetStatusTooltip = missing.Count == 0
            ? null
            : localizationService.GetString("Tools.WndEditor.Assets.MissingTooltip", FormatMissingNames(missing));
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
        KnownImagesStatusText = localizationService.GetString("Tools.WndEditor.Assets.KnownCount", known.Count);
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
                : localizationService.GetString("Tools.WndEditor.Assets.LibraryEmpty");
            return;
        }

        var selected = matches.Take(WndConstants.Editor.MaxLibraryResults).ToList();
        FilteredKnownImageNames = selected;
        LibraryStatusText = matches.Count > selected.Count
            ? localizationService.GetString("Tools.WndEditor.Assets.LibraryTruncated", selected.Count, matches.Count)
            : string.Empty;

        var tooltipTemplate = localizationService.GetString("Tools.WndEditor.Assets.ClickToAdd");
        if (string.IsNullOrWhiteSpace(tooltipTemplate))
        {
            tooltipTemplate = "Click to add {0}";
        }

        var items = new List<WndArtItemViewModel>(selected.Count);
        foreach (var name in selected)
        {
            var tooltip = string.Format(System.Globalization.CultureInfo.InvariantCulture, tooltipTemplate, name);
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

    private void QueueArtItemThumbnailsLoad(IReadOnlyList<WndArtItemViewModel> items)
    {
        var missingNames = items
            .Where(item => item.Thumbnail == null)
            .Select(item => item.Name)
            .Take(50)
            .ToList();

        if (missingNames.Count == 0 || SelectedAssetInstallation == null)
        {
            return;
        }

        var roots = ResolveAssetRoots(SelectedAssetInstallation);
        var detectedProjectDir = ResolveProjectDirectory(FilePath, roots) ?? ResolveProjectDirectory(FilesDirectory, roots);
        var projectDirectory = CombineProjectDirectories(LinkedModFolder, detectedProjectDir);
        var linkedBigs = LinkedBigFiles.ToList();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await assetService.Images.GetImagesAsync(
                    missingNames,
                    roots.BaseRoot,
                    null,
                    projectDirectory,
                    linkedBigs,
                    roots.IsZeroHour,
                    CancellationToken.None).ConfigureAwait(false);

                if (result.Success && result.Data != null && result.Data.Count > 0)
                {
                    var decoded = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
                    foreach (var (k, bytes) in result.Data)
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

                    if (decoded.Count > 0)
                    {
                        await InvokeOnUIThreadAsync(() =>
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
                        }).ConfigureAwait(false);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to load art item thumbnails in background");
            }
        });
    }

    private string FormatMissingNames(IReadOnlyList<string> missing)
    {
        var shown = missing.Take(WndConstants.Preview.MaxMissingTooltipNames).ToList();
        var text = string.Join(", ", shown);
        if (missing.Count > shown.Count)
        {
            text = string.Concat(text, ", ", localizationService.GetString("Tools.WndEditor.Assets.MissingMore", missing.Count - shown.Count));
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
                    (Directory.Exists(Path.Combine(current, WndConstants.StringTables.DataDirectory))
                    || Directory.Exists(Path.Combine(current, "Window"))
                    || Directory.Exists(Path.Combine(current, "window"))
                    || Directory.Exists(Path.Combine(current, "Art"))
                    || Directory.Exists(Path.Combine(current, "INI"))))
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

    private IReadOnlyDictionary<string, string> ResolveSchemeOverrides(
        AssetRoots roots,
        string? projectDirectory,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? additionalBigFiles = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WndConstants.ControlBarScheme.BackgroundMarkerKey] = roots.IsZeroHour ? WndConstants.ControlBarScheme.DefaultAmericaBaseZeroHour : WndConstants.ControlBarScheme.DefaultAmericaBaseGenerals,
            [WndConstants.ControlBarScheme.RightHUDKey] = "SALogo",
            [WndConstants.ControlBarScheme.ButtonOptionsKey] = "SAOptions",
            [WndConstants.ControlBarScheme.ButtonIdleWorkerKey] = "SAWorker",
            [WndConstants.ControlBarScheme.ButtonChatKey] = "SAChat",
            [WndConstants.ControlBarScheme.ButtonPlaceBeaconKey] = "SABeacon",
            [WndConstants.ControlBarScheme.ButtonGeneralKey] = "SAGeneral",
            [WndConstants.ControlBarScheme.ButtonUAttackKey] = "SAUAttackI",
            [WndConstants.ControlBarScheme.ExpBarForegroundKey] = "SAExpBar",
            [WndConstants.ControlBarScheme.QueueButtonImageKey] = "SCBigButton",
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

        if (string.Equals(p1, p2, StringComparison.OrdinalIgnoreCase))
        {
            return linkedDir;
        }

        return $"{linkedDir};{autoDetectedDir}";
    }
}
