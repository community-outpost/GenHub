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
    /// Gets the property rows of the selected window.
    /// </summary>
    public ObservableCollection<WndPropertyRowViewModel> PropertyRows { get; } = [];

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
    /// Gets or sets the key input for adding a property.
    /// </summary>
    [ObservableProperty]
    private string _newPropertyKey = string.Empty;

    /// <summary>
    /// Gets or sets the value input for adding a property.
    /// </summary>
    [ObservableProperty]
    private string _newPropertyValue = string.Empty;

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
            SyncAfterEdit(item.Window, WndConstants.PropertyKeys.ScreenRect);
            return;
        }

        var finalRect = moved;
        PushUndo(new WndEditAction(
            localizationService.GetString("Tools.WndEditor.History.MoveWindow"),
            () =>
            {
                item.Window.SetProperty(WndConstants.PropertyKeys.ScreenRect, finalRect.ToString());
                SyncAfterEdit(item.Window, WndConstants.PropertyKeys.ScreenRect);
            },
            () =>
            {
                item.Window.SetProperty(WndConstants.PropertyKeys.ScreenRect, original.ToString());
                SyncAfterEdit(item.Window, WndConstants.PropertyKeys.ScreenRect);
            }));
        SyncAfterEdit(item.Window, WndConstants.PropertyKeys.ScreenRect);
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
    /// Adds the entered property to the selected window.
    /// </summary>
    [RelayCommand]
    private void AddProperty()
    {
        if (_document == null || SelectedNode == null)
        {
            return;
        }

        var key = NewPropertyKey.Trim();
        if (key.Length == 0)
        {
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Property.EmptyKeyTitle"),
                localizationService.GetString("Tools.WndEditor.Property.EmptyKeyMessage"),
                NotificationDurations.Short);
            return;
        }

        var window = SelectedNode.Window;
        if (window.GetProperty(key) != null)
        {
            notificationService.ShowWarning(
                localizationService.GetString("Tools.WndEditor.Property.DuplicateKeyTitle"),
                localizationService.GetString("Tools.WndEditor.Property.DuplicateKeyMessage", key),
                NotificationDurations.Short);
            return;
        }

        var value = NewPropertyValue;
        window.SetProperty(key, value);
        PushUndo(new WndEditAction(
            localizationService.GetString("Tools.WndEditor.History.AddProperty", key),
            () =>
            {
                window.SetProperty(key, value);
                SyncAfterEdit(window, key);
            },
            () =>
            {
                window.RemoveProperty(key);
                SyncAfterEdit(window, key);
            }));
        NewPropertyKey = string.Empty;
        NewPropertyValue = string.Empty;
        SyncAfterEdit(window, key);
    }

    /// <summary>
    /// Deletes a property row from the selected window.
    /// </summary>
    /// <param name="row">The property row to delete.</param>
    [RelayCommand]
    private void DeleteProperty(WndPropertyRowViewModel? row)
    {
        if (_document == null || SelectedNode == null || row == null)
        {
            return;
        }

        var window = SelectedNode.Window;
        var index = window.Properties.FindIndex(p => string.Equals(p.Key, row.Key, StringComparison.Ordinal));
        if (index < 0)
        {
            return;
        }

        var removed = window.Properties[index];
        window.Properties.RemoveAt(index);
        PushUndo(new WndEditAction(
            localizationService.GetString("Tools.WndEditor.History.DeleteProperty", row.Key),
            () =>
            {
                window.RemoveProperty(row.Key);
                SyncAfterEdit(window, row.Key);
            },
            () =>
            {
                window.Properties.Insert(Math.Min(index, window.Properties.Count), removed);
                SyncAfterEdit(window, row.Key);
            }));
        SyncAfterEdit(window, row.Key);
    }

    partial void OnSelectedNodeChanged(WndTreeNodeViewModel? value)
    {
        SyncCanvasSelection();
        RebuildProperties();
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
        RebuildAll();
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

        foreach (var window in _document.Windows)
        {
            RootNodes.Add(BuildTreeNode(window, null));
        }
    }

    private WndTreeNodeViewModel BuildTreeNode(WndWindow window, WndTreeNodeViewModel? parent)
    {
        var node = new WndTreeNodeViewModel(window, parent);
        foreach (var child in window.Children)
        {
            node.Children.Add(BuildTreeNode(child, node));
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
        PropertyRows.Clear();
        if (SelectedNode == null)
        {
            return;
        }

        foreach (var property in SelectedNode.Window.Properties)
        {
            PropertyRows.Add(new WndPropertyRowViewModel(property.Key, property.Value, CommitPropertyEdit));
        }
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
        PushUndo(new WndEditAction(
            localizationService.GetString("Tools.WndEditor.History.EditProperty", key),
            () =>
            {
                window.SetProperty(key, value);
                SyncAfterEdit(window, key);
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

                SyncAfterEdit(window, key);
            }));
        SyncAfterEdit(window, key);
    }

    private void SyncAfterEdit(WndWindow window, string? propertyKey)
    {
        FindNode(window.Id)?.RefreshDisplay();
        if (propertyKey != null && SelectedNode?.Window.Id == window.Id)
        {
            var row = PropertyRows.FirstOrDefault(r => string.Equals(r.Key, propertyKey, StringComparison.Ordinal));
            row?.RefreshValue(window.GetProperty(propertyKey) ?? string.Empty);
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
