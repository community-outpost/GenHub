using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Tools.WndEditor;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
    ILogger<WndEditorViewModel> logger) : ObservableObject
{
    private readonly Stack<WndEditAction> _undoStack = new();
    private readonly Stack<WndEditAction> _redoStack = new();
    private WndDocument? _document;
    private double _canvasBaseWidth = WndConstants.Editor.MinCanvasWidth;
    private double _canvasBaseHeight = WndConstants.Editor.MinCanvasHeight;
    private WndCanvasItemViewModel? _dragItem;
    private Point _dragStart;
    private WndScreenRect? _dragOriginal;

    /// <summary>
    /// Gets the root tree nodes of the edited document.
    /// </summary>
    public ObservableCollection<WndTreeNodeViewModel> RootNodes { get; } = [];

    /// <summary>
    /// Gets the window definition files listed in the explorer.
    /// </summary>
    public ObservableCollection<WndFileEntryViewModel> Files { get; } = [];

    /// <summary>
    /// Gets the canvas items rendered from window geometry.
    /// </summary>
    public ObservableCollection<WndCanvasItemViewModel> CanvasItems { get; } = [];

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
    private double _zoom = WndConstants.Editor.DefaultZoom;

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
        logger.LogInformation("Opened window definition file {Path}", filePath);
        return true;
    }

    /// <summary>
    /// Loads a document from text, replacing the open document.
    /// </summary>
    /// <param name="content">The raw file content.</param>
    /// <param name="filePath">The optional source path recorded on the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the content parsed successfully.</returns>
    public Task<bool> LoadFromTextAsync(string content, string? filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = wndDocumentService.ParseText(content, filePath);
        if (!result.Success || result.Data == null)
        {
            notificationService.ShowError(
                localizationService.GetString("Tools.WndEditor.Open.FailureTitle"),
                localizationService.GetString("Tools.WndEditor.Open.FailureMessage", result.FirstError ?? string.Empty),
                NotificationDurations.Long);
            return Task.FromResult(false);
        }

        AdoptDocument(result.Data, filePath);
        return Task.FromResult(true);
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
        _dragItem.X = moved.UpperLeftX * Zoom;
        _dragItem.Y = moved.UpperLeftY * Zoom;
        _dragItem.Width = moved.Width * Zoom;
        _dragItem.Height = moved.Height * Zoom;
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
        logger.LogInformation("Created new window definition document");
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
            FilePath = localPath;
            SyncFilesDirectory(localPath);
            await WriteDocumentToFileAsync(localPath, cancellationToken);
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
        _undoStack.Push(action);
        IsModified = true;
        RefreshUndoCommands();
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
    /// <param name="file">The file entry to open.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task OpenExplorerFileAsync(WndFileEntryViewModel? file, CancellationToken cancellationToken = default)
    {
        if (file == null)
        {
            return;
        }

        await OpenFileAsync(file.FullPath, cancellationToken);
    }

    /// <summary>
    /// Chooses the directory listed in the file explorer.
    /// </summary>
    [RelayCommand]
    private async Task BrowseFilesDirectoryAsync(CancellationToken cancellationToken = default)
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
            FilesDirectory = localPath;
        }
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
            var currentPath = FilePath;
            var ordered = Directory
                .EnumerateFiles(FilesDirectory, ModBuilderConstants.FileNames.WndSearchPattern)
                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            foreach (var path in ordered)
            {
                var isCurrent = string.Equals(path, currentPath, StringComparison.OrdinalIgnoreCase);
                Files.Add(new WndFileEntryViewModel(Path.GetFileName(path), path, isCurrent));
            }
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to list window definition files in {Directory}", FilesDirectory);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Access denied listing window definition files in {Directory}", FilesDirectory);
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
        if (!string.IsNullOrEmpty(value))
        {
            RefreshFiles();
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

    private async Task WriteDocumentToFileAsync(string filePath, CancellationToken cancellationToken)
    {
        if (_document == null)
        {
            return;
        }

        try
        {
            var text = wndDocumentService.WriteDocument(_document);
            await File.WriteAllTextAsync(filePath, text, cancellationToken);
            IsModified = false;
            notificationService.ShowSuccess(
                localizationService.GetString("Tools.WndEditor.Save.SuccessTitle"),
                localizationService.GetString("Tools.WndEditor.Save.SuccessMessage", Path.GetFileName(filePath)),
                NotificationDurations.Medium);
            logger.LogInformation("Saved window definition file {Path}", filePath);
        }
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to save window definition file {Path}", filePath);
            notificationService.ShowError(
                localizationService.GetString("Tools.WndEditor.Save.FailureTitle"),
                localizationService.GetString("Tools.WndEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Access denied saving window definition file {Path}", filePath);
            notificationService.ShowError(
                localizationService.GetString("Tools.WndEditor.Save.FailureTitle"),
                localizationService.GetString("Tools.WndEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
        }
    }

    private void AdoptDocument(WndDocument document, string? filePath)
    {
        _document = document;
        FilePath = filePath;
        HasDocument = true;
        IsModified = false;
        _undoStack.Clear();
        _redoStack.Clear();
        RefreshUndoCommands();
        SyncFilesDirectory(filePath);
        RebuildAll();
    }

    private void SyncFilesDirectory(string? filePath)
    {
        var directory = string.IsNullOrEmpty(filePath) ? null : Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !string.Equals(directory, FilesDirectory, StringComparison.OrdinalIgnoreCase))
        {
            FilesDirectory = directory;
        }
        else
        {
            RefreshFiles();
        }
    }

    private void PushUndo(WndEditAction action)
    {
        _undoStack.Push(action);
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
        RootNodes.Clear();
        if (_document == null)
        {
            return;
        }

        var filter = WindowsFilter.Trim();
        foreach (var window in _document.Windows)
        {
            var node = BuildTreeNode(window, null, filter);
            if (node != null)
            {
                RootNodes.Add(node);
            }
        }
    }

    private WndTreeNodeViewModel? BuildTreeNode(WndWindow window, WndTreeNodeViewModel? parent, string filter)
    {
        if (filter.Length > 0 && !SubtreeMatchesFilter(window, filter))
        {
            return null;
        }

        var node = new WndTreeNodeViewModel(window, parent);
        foreach (var child in window.Children)
        {
            var childNode = BuildTreeNode(child, node, filter);
            if (childNode != null)
            {
                node.Children.Add(childNode);
            }
        }

        if (filter.Length > 0)
        {
            node.IsExpanded = true;
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
                X = rect.UpperLeftX * Zoom,
                Y = rect.UpperLeftY * Zoom,
                Width = rect.Width * Zoom,
                Height = rect.Height * Zoom,
            };
            CanvasItems.Add(item);
            maxWidth = Math.Max(maxWidth, rect.BottomRightX);
            maxHeight = Math.Max(maxHeight, rect.BottomRightY);
        }

        _canvasBaseWidth = maxWidth;
        _canvasBaseHeight = maxHeight;
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
}
