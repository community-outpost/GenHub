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
public sealed partial class WndEditorViewModel(
    IWndDocumentService wndDocumentService,
    INotificationService notificationService,
    ILocalizationService localizationService,
    IDialogService dialogService,
    IGameInstallationService gameInstallationService,
    IWndEditorAssetService assetService,
    ILogger<WndEditorViewModel> logger) : ObservableObject, IDisposable
{
    private sealed record AssetRoots(string BaseRoot, string? OverrideRoot);

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
    private readonly object _previewSync = new();
    private WndDocument? _document;
    private double _canvasBaseWidth = WndConstants.Editor.MinCanvasWidth;
    private double _canvasBaseHeight = WndConstants.Editor.MinCanvasHeight;
    private WndCanvasItemViewModel? _dragItem;
    private Point _dragStart;
    private WndScreenRect? _dragOriginal;
    private IReadOnlyList<GameInstallation> _installations = [];
    private Dictionary<string, Bitmap> _previewBitmaps = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, byte[]> _previewPngs = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _resolvedStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, string> _schemeOverrides = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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
    private double _zoom = WndConstants.Editor.DefaultZoom;

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
    /// Gets or sets the active tab in the left sidebar (0 = Windows, 1 = Files).
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
    /// Moves the dragged canvas item.
    /// </summary>
    /// <param name="canvasPoint">The pointer position in canvas coordinates.</param>
    public void UpdateCanvasDrag(Point canvasPoint)
    {
        if (_dragItem == null || _dragOriginal == null)
        {
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
    /// Finishes dragging a canvas item and records the move as one undoable edit.
    /// </summary>
    public void EndCanvasDrag()
    {
        var item = _dragItem;
        var original = _dragOriginal;
        _dragItem = null;
        _dragOriginal = null;
        if (item == null || original == null)
        {
            return;
        }

        if (!item.Window.TryGetScreenRect(out var moved) || moved == null || moved.Equals(original))
        {
            SyncAfterEdit(item.Window);
            return;
        }

        var finalRect = moved;
        PushUndo(new WndEditAction(
            localizationService.GetString("Tools.WndEditor.History.MoveWindow"),
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

    private static TopLevel? GetTopLevel()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
        {
            return null;
        }

        return TopLevel.GetTopLevel(lifetime.MainWindow);
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

    private static IReadOnlyCollection<string> CollectPreviewImageNames(WndDocument document, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in EnumerateWindows(document.Windows))
        {
            foreach (var name in WndPreviewPlanner.Plan(window, overrides).ReferencedImages)
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static IReadOnlyCollection<string> CollectPreviewLabels(WndDocument document, IReadOnlyDictionary<string, string>? overrides = null)
    {
        var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var window in EnumerateWindows(document.Windows))
        {
            if (window.ControlType == WndControlType.EntryField)
            {
                continue;
            }

            var text = WndPreviewPlanner.Plan(window, overrides).Text;
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
    /// Saves the open document to its file, asking for a path when untitled.
    /// </summary>
    [RelayCommand]
    private async Task SaveFileAsync(CancellationToken cancellationToken = default)
    {
        if (_document == null)
        {
            return;
        }

        if (string.IsNullOrEmpty(FilePath))
        {
            await SaveFileAsWithDialogAsync(cancellationToken);
            return;
        }

        await WriteDocumentToFileAsync(FilePath, cancellationToken);
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
        IsModified = true;
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
        IsModified = true;
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
        _ = value;
        _resolvedStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        assetService.InvalidateCache();
        RefreshAssetPreviews();
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
            tempPath = Path.Combine(directory ?? Path.GetTempPath(), Path.GetRandomFileName());
            await File.WriteAllTextAsync(tempPath, text, cancellationToken).ConfigureAwait(false);
            File.Move(tempPath, filePath, overwrite: true);
            tempPath = null;
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
        _document = document;
        FilePath = filePath;
        HasDocument = true;
        IsModified = false;
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
        }
    }

    private void PushUndo(WndEditAction action)
    {
        PushUndoStack(action);
        _redoStack.Clear();
        IsModified = true;
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
        if (_document == null)
        {
            return;
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
        foreach (var window in EnumerateWindows(_document.Windows))
        {
            if (!window.TryGetScreenRect(out var rect) || rect == null)
            {
                continue;
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

        _canvasBaseWidth = maxWidth + (WndConstants.Editor.CanvasPadding * 2);
        _canvasBaseHeight = maxHeight + (WndConstants.Editor.CanvasPadding * 2);
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
    }

    private void RebuildProperties()
    {
        SelectedProperties = SelectedNode == null
            ? null
            : new WndWindowPropertiesViewModel(
                SelectedNode.Window,
                wndDocumentService,
                notificationService,
                localizationService,
                CommitPropertyEdit,
                RemovePropertyByKey,
                ReplaceSelectedProperties);
    }

    private void CommitPropertyEdit(string key, string value)
    {
        if (_document == null || SelectedNode == null)
        {
            return;
        }

        var window = SelectedNode.Window;
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

    private void RemovePropertyByKey(string key)
    {
        if (_document == null || SelectedNode == null)
        {
            return;
        }

        var window = SelectedNode.Window;
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

    private void ReplaceSelectedProperties(IReadOnlyList<WndProperty> properties)
    {
        if (_document == null || SelectedNode == null)
        {
            return;
        }

        var window = SelectedNode.Window;
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
        var plan = WndPreviewPlanner.Plan(window, _schemeOverrides);
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
            if (installation.HasGenerals && !string.IsNullOrEmpty(installation.GeneralsPath))
            {
                AvailableInstallations.Add(new GameInstallationOption
                {
                    DisplayName = $"Generals ({installation.InstallationType.GetDisplayName()})",
                    Path = installation.GeneralsPath,
                    IsZeroHour = false,
                });
            }

            if (installation.HasZeroHour && !string.IsNullOrEmpty(installation.ZeroHourPath))
            {
                AvailableInstallations.Add(new GameInstallationOption
                {
                    DisplayName = $"Zero Hour ({installation.InstallationType.GetDisplayName()})",
                    Path = installation.ZeroHourPath,
                    IsZeroHour = true,
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
        if (string.IsNullOrEmpty(FilePath) || AvailableInstallations.Count == 0)
        {
            return;
        }

        var containing = AvailableInstallations.FirstOrDefault(option => IsPathUnder(FilePath, option.Path));
        if (containing != null && !ReferenceEquals(containing, SelectedAssetInstallation))
        {
            SelectedAssetInstallation = containing;
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
                return new AssetRoots(installation.GeneralsPath, null);
            }
        }

        // If selection.Path is a Zero Hour folder without a matched installation, try finding Generals base
        var fallbackGenerals = _installations.FirstOrDefault(i => !string.IsNullOrEmpty(i.GeneralsPath) && i.HasGenerals);
        if (fallbackGenerals != null && !string.IsNullOrEmpty(fallbackGenerals.GeneralsPath)
            && (selection.IsZeroHour || IsZeroHourPath(selection)))
        {
            return new AssetRoots(fallbackGenerals.GeneralsPath, selection.Path);
        }

        return new AssetRoots(selection.Path, null);
    }

    private AssetRoots ResolveZeroHourAssetRoots(GameInstallation installation)
    {
        if (!string.IsNullOrEmpty(installation.GeneralsPath) && installation.HasGenerals)
        {
            return new AssetRoots(installation.GeneralsPath, installation.ZeroHourPath);
        }

        // If this installation does not directly link Generals (e.g. Steam standalone),
        // search for ANY detected Generals installation across all installations
        var anyGenerals = _installations.FirstOrDefault(i => !string.IsNullOrEmpty(i.GeneralsPath) && i.HasGenerals);
        if (anyGenerals != null && !string.IsNullOrEmpty(anyGenerals.GeneralsPath))
        {
            return new AssetRoots(anyGenerals.GeneralsPath, installation.ZeroHourPath);
        }

        // Also check sibling directory (e.g. Steam: Command & Conquer Generals)
        var siblingGenerals = FindSiblingGeneralsPath(installation.ZeroHourPath);
        if (siblingGenerals != null)
        {
            return new AssetRoots(siblingGenerals, installation.ZeroHourPath);
        }

        return new AssetRoots(installation.ZeroHourPath, null);
    }

    private static string? FindSiblingGeneralsPath(string zeroHourPath)
    {
        try
        {
            var parent = Directory.GetParent(zeroHourPath)?.FullName;
            if (!string.IsNullOrEmpty(parent))
            {
                var siblingGenerals = Path.Combine(parent, GameClientConstants.GeneralsRetailDirectoryName);
                if (Directory.Exists(siblingGenerals))
                {
                    return siblingGenerals;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException or UnauthorizedAccessException)
        {
            // Fall back to ZeroHourPath
        }

        return null;
    }

    private void RefreshItemPreview(WndCanvasItemViewModel item)
    {
        var plan = WndPreviewPlanner.Plan(item.Window, _schemeOverrides);
        item.FillOverlay = ToOverlayBrush(plan.FillColor);
        item.BorderOverlay = ToOverlayBrush(plan.BorderColor);
        item.ContentText = ResolveDisplayText(plan, item.Window);
        item.ContentTextBrush = ToOverlayBrush(plan.TextColor) ?? Brushes.White;
        item.ContentFontSize = Math.Max(WndConstants.Preview.MinContentFontSize, plan.FontSize * Zoom);
        item.ContentFontWeight = plan.FontBold ? FontWeight.Bold : FontWeight.Normal;
        item.ContentTextAlignment = plan.TextCentered ? TextAlignment.Center : TextAlignment.Left;
        item.CanvasOpacity = plan.IsHidden ? WndConstants.Preview.HiddenOpacity : 1.0;
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
        var upHeight = up == null ? 0 : up.PixelSize.Height * Zoom;
        var downHeight = down == null ? 0 : down.PixelSize.Height * Zoom;
        if (up != null)
        {
            var width = up.PixelSize.Width * Zoom;
            overlays.Add(new WndCanvasOverlayViewModel(up, item.Width - width, 0, width, upHeight));
        }

        if (down != null)
        {
            var width = down.PixelSize.Width * Zoom;
            overlays.Add(new WndCanvasOverlayViewModel(down, item.Width - width, item.Height - downHeight, width, downHeight));
        }

        if (thumb != null && item.Height - upHeight - downHeight > 0)
        {
            var width = thumb.PixelSize.Width * Zoom;
            overlays.Add(new WndCanvasOverlayViewModel(thumb, item.Width - width, upHeight, width, item.Height - upHeight - downHeight));
        }
    }

    private void AddComboButtonOverlay(WndPreviewSubImages sub, WndCanvasItemViewModel item, List<WndCanvasOverlayViewModel> overlays)
    {
        if (sub.ComboButton == null || !_previewBitmaps.TryGetValue(sub.ComboButton, out var button))
        {
            return;
        }

        var width = button.PixelSize.Width * Zoom;
        overlays.Add(new WndCanvasOverlayViewModel(button, item.Width - width, 0, width, item.Height));
    }

    private void AddSliderThumbOverlay(WndPreviewSubImages sub, WndCanvasItemViewModel item, List<WndCanvasOverlayViewModel> overlays)
    {
        if (sub.SliderThumb == null || !_previewBitmaps.TryGetValue(sub.SliderThumb, out var thumb))
        {
            return;
        }

        var width = thumb.PixelSize.Width * Zoom;
        var height = thumb.PixelSize.Height * Zoom;
        overlays.Add(new WndCanvasOverlayViewModel(thumb, (item.Width - width) / 2, (item.Height - height) / 2, width, height));
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
        if (plan.Text == null || window.ControlType == WndControlType.EntryField)
        {
            return plan.Text;
        }

        if (_resolvedStrings.TryGetValue(plan.Text, out var localized) && !string.IsNullOrEmpty(localized))
        {
            return WndGameText.StripHotkeyMarkers(localized);
        }

        return plan.Text;
    }

    private Bitmap? ResolvePlanImage(WndPreviewPlan plan, WndCanvasItemViewModel item)
    {
        if (TryResolveThreePiece(plan, item, out var composed))
        {
            return composed;
        }

        if (!plan.IsThreePiece && plan.SingleImage != null)
        {
            if (plan.UnderlayImage != null && TryResolveUnderlay(plan.UnderlayImage, plan.SingleImage, item, out var underlayComposed))
            {
                return underlayComposed;
            }

            if (_previewBitmaps.TryGetValue(plan.SingleImage, out var single))
            {
                return single;
            }
        }

        return null;
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

        using var stream = new MemoryStream(composed);
        bitmap = new Bitmap(stream);
        _composedBitmaps[key] = bitmap;
        return true;
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
        }

        CancelAndDisposeCts(toCancel);
        _ = LoadAssetPreviewsAsync(cts.Token, generation);
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

    private async Task LoadAssetPreviewsAsync(CancellationToken cancellationToken, int generation)
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
            var projectDirectory = ResolveProjectDirectory(FilePath, roots);
            var schemeOverrides = await Task.Run(() => ResolveSchemeOverrides(roots, projectDirectory, cancellationToken), cancellationToken).ConfigureAwait(false);
            _schemeOverrides = schemeOverrides;
            var names = CollectPreviewImageNames(document, _schemeOverrides);
            var labels = CollectPreviewLabels(document, _schemeOverrides);
            var images = await assetService.Images.GetImagesAsync(names, roots.BaseRoot, roots.OverrideRoot, projectDirectory, cancellationToken).ConfigureAwait(false);
            var strings = await assetService.Strings.GetStringsAsync(labels, roots.BaseRoot, roots.OverrideRoot, projectDirectory, cancellationToken).ConfigureAwait(false);
            if ((!images.Success && !strings.Success) || generation != _previewGeneration)
            {
                return;
            }

            var bitmaps = images.Success ? images.Data : null;
            var values = strings.Success ? strings.Data : null;
            await InvokeOnUIThreadAsync(() => ApplyPreviews(bitmaps, values, generation, labels)).ConfigureAwait(false);
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
        IReadOnlyCollection<string>? attemptedLabels = null)
    {
        if (generation != _previewGeneration)
        {
            return;
        }

        if (images != null)
        {
            var previous = _previewBitmaps;
            var bitmaps = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, png) in images)
            {
                using var stream = new MemoryStream(png);
                bitmaps[name] = new Bitmap(stream);
            }

            _previewBitmaps = bitmaps;
            _previewPngs = images;
            ClearComposedBitmaps();
            foreach (var old in previous.Values)
            {
                old.Dispose();
            }
        }

        if (strings != null || attemptedLabels != null)
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

        foreach (var item in CanvasItems)
        {
            RefreshItemPreview(item);
        }

        RefreshAssetStatus();
    }

    private void RefreshAssetStatus()
    {
        if (_document == null)
        {
            AssetStatusText = string.Empty;
            AssetStatusTooltip = null;
            return;
        }

        if (SelectedAssetInstallation == null)
        {
            AssetStatusText = localizationService.GetString("Tools.WndEditor.Assets.SelectorWatermark");
            AssetStatusTooltip = null;
            return;
        }

        var names = CollectPreviewImageNames(_document, _schemeOverrides);
        if (names.Count == 0)
        {
            AssetStatusText = localizationService.GetString("Tools.WndEditor.Assets.EmptyStatus");
            AssetStatusTooltip = null;
            return;
        }

        var missing = names.Where(name => !_previewBitmaps.ContainsKey(name)).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        AssetStatusText = localizationService.GetString("Tools.WndEditor.Assets.ResolvedStatus", names.Count - missing.Count, names.Count);
        AssetStatusTooltip = missing.Count == 0
            ? null
            : localizationService.GetString("Tools.WndEditor.Assets.MissingTooltip", FormatMissingNames(missing));
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

        var directory = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        if (IsPathUnder(filePath, roots.BaseRoot)
            || (roots.OverrideRoot != null && IsPathUnder(filePath, roots.OverrideRoot)))
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

    private IReadOnlyDictionary<string, string> ResolveSchemeOverrides(AssetRoots roots, string? projectDirectory, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WndConstants.ControlBarScheme.BackgroundMarkerKey] = "InGameUIAmericaBase",
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
            var fs = WndGameFileSystem.Open(roots.BaseRoot, roots.OverrideRoot, projectDirectory, logger, cancellationToken);
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
}
