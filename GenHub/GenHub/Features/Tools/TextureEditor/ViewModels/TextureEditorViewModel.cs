using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Editors;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Tools.Common;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.Checksum;
using GenHub.Features.Tools.TextureEditor.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.TextureEditor.ViewModels;

/// <summary>
/// View model for the Texture Editor tool.
/// </summary>
public sealed partial class TextureEditorViewModel(
    ISageMappedImageParser parser,
    IMappedImageRegistry registry,
    IAtlasPackingService packingService,
    ITextureImageLoader imageLoader,
    TextureBitmapService bitmapService,
    INotificationService notificationService,
    ILogger<TextureEditorViewModel> logger,
    ILocalizationService localizationService,
    IDialogService dialogService,
    IGameInstallationService? gameInstallationService = null)
    : EditorToolViewModelBase(notificationService, localizationService, dialogService)
{
    private const string SavedMessageFallback = "Saved {0}.";
    private const string PackFailedTitleFallback = "Auto-pack failed";
    private const string ScanFailedTitleKey = "TextureEditor.Notify.ScanFailed.Title";
    private const string ScanFailedTitleFallback = "Scan failed";
    private const string ImportFailedTitleKey = "TextureEditor.Notify.ImportFailed.Title";
    private const string ImportFailedTitleFallback = "Import failed";
    private const string ExportInvalidTitleKey = "TextureEditor.Notify.ExportInvalid.Title";
    private const string ExportInvalidTitleFallback = "Cannot export slices";
    private const string OpenFailedTitleKey = "TextureEditor.Notify.OpenFailed.Title";
    private const string OpenFailedTitleFallback = "Failed to open atlas";
    private const string CopyFailedTitleKey = "TextureEditor.Notify.CopyFailed.Title";
    private const string CopyFailedTitleFallback = "Copy failed";

    private readonly ConcurrentDictionary<string, Dictionary<string, BigArchiveEntry>> _archiveIndexCache = new(StringComparer.OrdinalIgnoreCase);

    private readonly Stack<TextureEditAction> _undoStack = new();
    private readonly Stack<TextureEditAction> _redoStack = new();

    private DecodedTexture? _atlasDecoded;
    private FileExplorerViewModel? _fileExplorer;
    private MappedImageDefinition? _copiedSlice;
    private bool _isCutOperation;
    private string? _savedIniPath;
    private TextureSliceViewModel? _dragSlice;
    private (int Left, int Top, int Right, int Bottom)? _dragOriginal;
    private bool _isResizing;
    private CanvasResizeDirection _resizeDirection = CanvasResizeDirection.None;
    private Point _dragStart;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAtlas))]
    [NotifyPropertyChangedFor(nameof(AtlasDimensions))]
    [NotifyPropertyChangedFor(nameof(AtlasPixelWidth))]
    [NotifyPropertyChangedFor(nameof(AtlasPixelHeight))]
    [NotifyPropertyChangedFor(nameof(DisplayWidth))]
    [NotifyPropertyChangedFor(nameof(DisplayHeight))]
    [NotifyPropertyChangedFor(nameof(PickerThumbnailProvider))]
    private Bitmap? _atlasBitmap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AtlasFileName))]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    [NotifyPropertyChangedFor(nameof(PickerThumbnailProvider))]
    private string _atlasPath = string.Empty;

    [ObservableProperty]
    private TextureSliceViewModel? _selectedSlice;

    [ObservableProperty]
    private bool _isPanMode;

    /// <summary>
    /// Gets a value indicating whether the current atlas is a synthesized placeholder canvas.
    /// </summary>
    [ObservableProperty]
    private bool _isPlaceholder;

    /// <summary>
    /// Gets the shared file explorer listing textures and MappedImages INI files.
    /// </summary>
    public FileExplorerViewModel FileExplorer => _fileExplorer ??= CreateFileExplorer();

    /// <summary>
    /// Gets the mapped images indexed from MappedImages INI registries.
    /// </summary>
    public ObservableCollection<MappedImageDefinition> RegistryImages { get; private set; } = [];

    /// <summary>
    /// Gets the editable slices for the open atlas.
    /// </summary>
    public ObservableCollection<TextureSliceViewModel> Slices { get; } = [];

    /// <summary>
    /// Gets the file name of the open atlas.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasPath instance state and is bound from XAML.")]
    public string AtlasFileName
    {
        get
        {
            if (string.IsNullOrEmpty(AtlasPath))
            {
                return string.Empty;
            }

            int hashIndex = AtlasPath.IndexOf('#');
            string clean = hashIndex > 0 && AtlasPath[..hashIndex].EndsWith(".big", StringComparison.OrdinalIgnoreCase)
                ? AtlasPath.Substring(hashIndex + 1)
                : AtlasPath;
            return clean.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? string.Empty;
        }
    }

    /// <summary>
    /// Gets a value indicating whether an atlas is open.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasBitmap instance state and is bound from XAML.")]
    public bool HasAtlas => AtlasBitmap is not null;

    /// <summary>
    /// Gets the display width of the atlas on the canvas.
    /// </summary>
    public double DisplayWidth => AtlasBitmap is null ? 0 : AtlasBitmap.PixelSize.Width * Zoom;

    /// <summary>
    /// Gets the display height of the atlas on the canvas.
    /// </summary>
    public double DisplayHeight => AtlasBitmap is null ? 0 : AtlasBitmap.PixelSize.Height * Zoom;

    /// <summary>
    /// Gets the atlas dimensions display text.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasBitmap instance state and is bound from XAML.")]
    public string AtlasDimensions => AtlasBitmap is null ? string.Empty : $"{AtlasBitmap.PixelSize.Width} x {AtlasBitmap.PixelSize.Height}";

    /// <summary>
    /// Gets the pixel width of the open atlas.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasBitmap instance state and is bound from XAML.")]
    public int AtlasPixelWidth => AtlasBitmap?.PixelSize.Width ?? 0;

    /// <summary>
    /// Gets the pixel height of the open atlas.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasBitmap instance state and is bound from XAML.")]
    public int AtlasPixelHeight => AtlasBitmap?.PixelSize.Height ?? 0;

    /// <summary>
    /// Gets the thumbnail provider for registry entries.
    /// </summary>
    public Func<MappedImageDefinition, Avalonia.Media.IImage?> PickerThumbnailProvider => CreatePickerThumbnail;

    /// <summary>
    /// Gets the document title with a modification marker.
    /// </summary>
    public override string? DocumentTitle
    {
        get
        {
            if (!HasAtlas)
            {
                return null;
            }

            return IsDirty ? $"*{AtlasFileName}" : AtlasFileName;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the slices can be saved.
    /// </summary>
    public override bool CanSave => HasAtlas;

    /// <summary>
    /// Gets a value indicating whether the slices can be saved under a new path.
    /// </summary>
    public override bool CanSaveAs => HasAtlas;

    /// <summary>
    /// Gets a value indicating whether undo is available.
    /// </summary>
    public override bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether redo is available.
    /// </summary>
    public override bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the selected slice can be copied.
    /// </summary>
    public override bool CanCopy => SelectedSlice is not null;

    /// <summary>
    /// Gets a value indicating whether the selected slice can be cut.
    /// </summary>
    public override bool CanCut => SelectedSlice is not null;

    /// <summary>
    /// Gets a value indicating whether a slice can be pasted.
    /// </summary>
    public override bool CanPaste => HasAtlas;

    /// <summary>
    /// Gets a value indicating whether the selected slice can be duplicated.
    /// </summary>
    public override bool CanDuplicate => SelectedSlice is not null;

    /// <summary>
    /// Gets a value indicating whether the selected slice can be deleted.
    /// </summary>
    public override bool CanDelete => SelectedSlice is not null;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasPath instance state.")]
    private string DefaultIniPath
    {
        get
        {
            string cleanPath = StripArchiveFragment(AtlasPath);
            string dir = Path.GetDirectoryName(cleanPath) ?? string.Empty;
            if (string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(FileExplorer.Directory))
            {
                dir = FileExplorer.Directory;
            }

            string baseName = Path.GetFileNameWithoutExtension(AtlasFileName);
            return Path.Combine(dir, baseName + TextureEditorConstants.MappedImagesExtension);
        }
    }

    /// <summary>
    /// Loads a registry entry into the slice list for editing.
    /// The library catalogs entries for every scanned texture, so an entry that
    /// targets another atlas opens its texture first and then loads.
    /// Texture names match ignoring the file extension, so a <c>.tga</c> entry
    /// loads onto its <c>.dds</c> variant like the SAGE engine.
    /// </summary>
    /// <param name="definition">The mapped image definition.</param>
    /// <param name="explicitOpen">True when the entry was just opened with its texture, bypassing the directory guard.</param>
    public void LoadRegistryEntry(MappedImageDefinition definition, bool explicitOpen = false)
    {
        _ = LoadRegistryEntryAsync(definition, explicitOpen);
    }

    /// <summary>
    /// Asynchronously loads a mapped image definition from the registry into the editor.
    /// </summary>
    /// <param name="definition">The mapped image definition.</param>
    /// <param name="explicitOpen">True when the entry was just opened with its texture, bypassing the directory guard.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task LoadRegistryEntryAsync(MappedImageDefinition definition, bool explicitOpen = false)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (IsBusy)
        {
            return;
        }

        if (AtlasBitmap is null)
        {
            await HandleRegistryEntryWithoutAtlasAsync(definition, explicitOpen).ConfigureAwait(true);
            return;
        }

        bool sameDirectory = IsDefinitionInSameDirectory(definition);
        if (TryAdoptMatchingAtlasEntry(definition, explicitOpen, sameDirectory))
        {
            return;
        }

        await ResolveExternalAtlasEntryAsync(definition, explicitOpen, sameDirectory).ConfigureAwait(true);
    }

    /// <summary>
    /// Starts dragging a slice across the canvas.
    /// </summary>
    /// <param name="slice">The slice being dragged.</param>
    /// <param name="canvasPoint">The initial pointer position in canvas coordinates.</param>
    public void BeginSliceDrag(TextureSliceViewModel? slice, Point canvasPoint)
    {
        _dragSlice = null;
        _dragOriginal = null;
        _isResizing = false;
        _resizeDirection = CanvasResizeDirection.None;
        if (slice is null)
        {
            return;
        }

        SelectedSlice = slice;
        _dragSlice = slice;
        _dragStart = canvasPoint;
        _dragOriginal = (slice.Left, slice.Top, slice.Right, slice.Bottom);
    }

    /// <summary>
    /// Starts resizing a slice in the specified handle direction.
    /// </summary>
    /// <param name="slice">The resized slice.</param>
    /// <param name="direction">The resize handle direction.</param>
    /// <param name="canvasPoint">The pointer position in canvas coordinates.</param>
    public void BeginSliceResize(TextureSliceViewModel? slice, CanvasResizeDirection direction, Point canvasPoint)
    {
        _dragSlice = null;
        _dragOriginal = null;
        _isResizing = false;
        _resizeDirection = CanvasResizeDirection.None;
        if (slice is null || direction == CanvasResizeDirection.None)
        {
            return;
        }

        SelectedSlice = slice;
        _dragSlice = slice;
        _dragStart = canvasPoint;
        _dragOriginal = (slice.Left, slice.Top, slice.Right, slice.Bottom);
        _isResizing = true;
        _resizeDirection = direction;
    }

    /// <summary>
    /// Moves or resizes the dragged slice.
    /// </summary>
    /// <param name="canvasPoint">The pointer position in canvas coordinates.</param>
    public void UpdateSliceDrag(Point canvasPoint)
    {
        if (_dragSlice is null || _dragOriginal is null)
        {
            return;
        }

        double zoom = Math.Max(EditorConstants.ZoomMin, Zoom);
        int deltaX = (int)Math.Round((canvasPoint.X - _dragStart.X) / zoom);
        int deltaY = (int)Math.Round((canvasPoint.Y - _dragStart.Y) / zoom);
        if (_isResizing)
        {
            var (left, top, right, bottom) = CanvasResizeHelper.Resize(
                new CanvasResizeEdges(
                    _dragOriginal.Value.Left,
                    _dragOriginal.Value.Top,
                    _dragOriginal.Value.Right,
                    _dragOriginal.Value.Bottom),
                _resizeDirection,
                deltaX,
                deltaY,
                TextureEditorConstants.MinSliceDimension);
            _dragSlice.Left = Math.Max(0, left);
            _dragSlice.Top = Math.Max(0, top);
            _dragSlice.Right = Math.Max(_dragSlice.Left + TextureEditorConstants.MinSliceDimension, right);
            _dragSlice.Bottom = Math.Max(_dragSlice.Top + TextureEditorConstants.MinSliceDimension, bottom);
            return;
        }

        int width = _dragOriginal.Value.Right - _dragOriginal.Value.Left;
        int height = _dragOriginal.Value.Bottom - _dragOriginal.Value.Top;
        _dragSlice.Left = Math.Clamp(_dragOriginal.Value.Left + deltaX, 0, Math.Max(0, AtlasPixelWidth - width));
        _dragSlice.Top = Math.Clamp(_dragOriginal.Value.Top + deltaY, 0, Math.Max(0, AtlasPixelHeight - height));
        _dragSlice.Right = _dragSlice.Left + width;
        _dragSlice.Bottom = _dragSlice.Top + height;
    }

    /// <summary>
    /// Finishes dragging or resizing a slice.
    /// </summary>
    public void EndSliceDrag()
    {
        if (_dragSlice is not null && _dragOriginal is not null)
        {
            var orig = _dragOriginal.Value;
            var slice = _dragSlice;
            if (slice.Left != orig.Left || slice.Top != orig.Top || slice.Right != orig.Right || slice.Bottom != orig.Bottom)
            {
                int targetLeft = slice.Left;
                int targetTop = slice.Top;
                int targetRight = slice.Right;
                int targetBottom = slice.Bottom;
                PushUndo(new TextureEditAction(
                    _isResizing
                        ? Localize("TextureEditor.History.ResizeSlice", "Resize slice")
                        : Localize("TextureEditor.History.MoveSlice", "Move slice"),
                    () =>
                    {
                        slice.Left = targetLeft;
                        slice.Top = targetTop;
                        slice.Right = targetRight;
                        slice.Bottom = targetBottom;
                        MarkDirty();
                    },
                    () =>
                    {
                        slice.Left = orig.Left;
                        slice.Top = orig.Top;
                        slice.Right = orig.Right;
                        slice.Bottom = orig.Bottom;
                        MarkDirty();
                    }));
                MarkDirty();
            }
        }

        _dragSlice = null;
        _dragOriginal = null;
        _isResizing = false;
        _resizeDirection = CanvasResizeDirection.None;
    }

    /// <summary>
    /// Pushes an undoable action to the undo stack, clearing the redo stack.
    /// </summary>
    /// <param name="action">The edit action to record.</param>
    public void PushUndo(TextureEditAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _undoStack.Push(action);
        if (_undoStack.Count > TextureEditorConstants.MaxHistoryDepth)
        {
            var items = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = TextureEditorConstants.MaxHistoryDepth - 1; i >= 0; i--)
            {
                _undoStack.Push(items[i]);
            }
        }

        _redoStack.Clear();
        RefreshEditorCommands();
    }

    /// <summary>
    /// Nudges the selected slice by the specified offsets in texture coordinates.
    /// </summary>
    /// <param name="deltaX">The horizontal offset in pixels.</param>
    /// <param name="deltaY">The vertical offset in pixels.</param>
    public void NudgeSelectedSlice(int deltaX, int deltaY)
    {
        if (SelectedSlice is null || AtlasBitmap is null)
        {
            return;
        }

        var slice = SelectedSlice;
        int width = slice.Width;
        int height = slice.Height;
        int newLeft = Math.Clamp(slice.Left + deltaX, 0, Math.Max(0, AtlasPixelWidth - width));
        int newTop = Math.Clamp(slice.Top + deltaY, 0, Math.Max(0, AtlasPixelHeight - height));
        int newRight = newLeft + width;
        int newBottom = newTop + height;

        if (newLeft == slice.Left && newTop == slice.Top)
        {
            return;
        }

        int origLeft = slice.Left;
        int origTop = slice.Top;
        int origRight = slice.Right;
        int origBottom = slice.Bottom;

        slice.Left = newLeft;
        slice.Top = newTop;
        slice.Right = newRight;
        slice.Bottom = newBottom;

        PushUndo(new TextureEditAction(
            Localize("TextureEditor.History.NudgeSlice", "Nudge slice"),
            () =>
            {
                slice.Left = newLeft;
                slice.Top = newTop;
                slice.Right = newRight;
                slice.Bottom = newBottom;
                MarkDirty();
            },
            () =>
            {
                slice.Left = origLeft;
                slice.Top = origTop;
                slice.Right = origRight;
                slice.Bottom = origBottom;
                MarkDirty();
            }));

        MarkDirty();
    }

    /// <inheritdoc />
    protected override string UnsavedChangesTitleKey => "TextureEditor.Dialog.UnsavedChanges.Title";

    /// <inheritdoc />
    protected override string UnsavedChangesMessageKey => "TextureEditor.Dialog.UnsavedChanges.Message";

    /// <inheritdoc />
    protected override string UnsavedChangesDiscardKey => "TextureEditor.Dialog.UnsavedChanges.Discard";

    /// <inheritdoc />
    protected override string UnsavedChangesCancelKey => "TextureEditor.Dialog.UnsavedChanges.Cancel";

    /// <inheritdoc />
    protected override void OnZoomChanged()
    {
        foreach (var slice in Slices)
        {
            slice.UpdateZoom(Zoom);
        }

        OnPropertyChanged(nameof(DisplayWidth));
        OnPropertyChanged(nameof(DisplayHeight));
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
        MarkDirty();
        RefreshEditorCommands();
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
        MarkDirty();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnCopy()
    {
        if (IsTextInputFocused() || SelectedSlice is null)
        {
            return;
        }

        var def = SelectedSlice.ToDefinition();
        string serialized = string.Empty;
        try
        {
            serialized = parser.Serialize([def]);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Failed to serialize slice {Name} for clipboard copy", def.Name);
            Notifications.ShowWarning(
                Localize(CopyFailedTitleKey, CopyFailedTitleFallback),
                ex.Message,
                NotificationDurations.Medium);
            return;
        }

        _copiedSlice = def;
        _isCutOperation = false;
        _ = CopyTextToClipboardAsync(serialized);
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnCut()
    {
        if (IsTextInputFocused() || SelectedSlice is null)
        {
            return;
        }

        var slice = SelectedSlice;
        var def = slice.ToDefinition();
        string serialized = string.Empty;
        try
        {
            serialized = parser.Serialize([def]);
        }
        catch (ArgumentException ex)
        {
            logger.LogWarning(ex, "Failed to serialize slice {Name} for clipboard cut", def.Name);
            Notifications.ShowWarning(
                Localize(CopyFailedTitleKey, CopyFailedTitleFallback),
                ex.Message,
                NotificationDurations.Medium);
            return;
        }

        _copiedSlice = def;
        _isCutOperation = true;
        _ = CopyTextToClipboardAsync(serialized);

        int index = Slices.IndexOf(slice);
        DeleteSlice(slice);
        PushUndo(new TextureEditAction(
            Localize("TextureEditor.History.CutSlice", "Cut slice"),
            () =>
            {
                DeleteSlice(slice);
                MarkDirty();
            },
            () =>
            {
                RestoreSlice(slice, index);
                SelectedSlice = slice;
                MarkDirty();
            }));

        MarkDirty();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override async Task OnPasteAsync(CancellationToken cancellationToken)
    {
        if (IsTextInputFocused() || AtlasBitmap is null)
        {
            return;
        }

        if (_copiedSlice is not null)
        {
            PasteInternalCopiedSlice();
            return;
        }

        await PasteFromClipboardAsync(cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override void OnDuplicate()
    {
        if (IsTextInputFocused() || SelectedSlice is null || AtlasBitmap is null)
        {
            return;
        }

        var copy = InsertSliceCopy(SelectedSlice.ToDefinition());
        if (copy is not null)
        {
            PushUndo(new TextureEditAction(
                Localize("TextureEditor.History.DuplicateSlice", "Duplicate slice"),
                () =>
                {
                    if (!Slices.Contains(copy))
                    {
                        TrackSlice(copy);
                    }

                    SelectedSlice = copy;
                    MarkDirty();
                },
                () =>
                {
                    UntrackSlice(copy);
                    MarkDirty();
                }));
        }

        MarkDirty();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnDelete()
    {
        if (IsTextInputFocused() || SelectedSlice is null)
        {
            return;
        }

        var slice = SelectedSlice;
        int index = Slices.IndexOf(slice);
        DeleteSlice(slice);
        PushUndo(new TextureEditAction(
            Localize("TextureEditor.History.DeleteSlice", "Delete slice"),
            () =>
            {
                DeleteSlice(slice);
                MarkDirty();
            },
            () =>
            {
                RestoreSlice(slice, index);
                SelectedSlice = slice;
                MarkDirty();
            }));

        MarkDirty();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override async Task OnNewDocumentAsync(CancellationToken cancellationToken)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        ClearAtlas();
        MarkSaved();
        logger.LogInformation("Created new texture atlas document");
    }

    /// <inheritdoc />
    protected override async Task OnOpenFolderAsync(CancellationToken cancellationToken)
    {
        string? folder = await BrowseExplorerFolderAsync(cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        FileExplorer.Directory = folder;
        await OnExplorerDirectoryAdoptedAsync(folder, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override async Task OnOpenFileAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Localize("TextureEditor.Dialog.OpenAtlas", "Open texture atlas or INI"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localize("TextureEditor.Dialog.AllSupportedFiles", "Supported files (*.tga, *.dds, *.png, *.ini)"))
                {
                    Patterns = TextureEditorConstants.TextureExtensions.Concat([TextureEditorConstants.MappedImagesExtension]).Select(extension => "*" + extension).ToArray(),
                },
                new FilePickerFileType(Localize("TextureEditor.Dialog.TextureFiles", "Texture files"))
                {
                    Patterns = TextureEditorConstants.TextureExtensions.Select(extension => "*" + extension).ToArray(),
                },
                new FilePickerFileType(Localize("TextureEditor.Dialog.IniFiles", "MappedImages INI files (*.ini)"))
                {
                    Patterns = ["*" + TextureEditorConstants.MappedImagesExtension],
                },
            ],
        }).ConfigureAwait(true);

        if (files.Count == 0)
        {
            return;
        }

        string localPath = files[0].Path.LocalPath;
        if (Path.GetExtension(localPath).Equals(TextureEditorConstants.MappedImagesExtension, StringComparison.OrdinalIgnoreCase))
        {
            await OpenIniFileAsync(localPath).ConfigureAwait(true);
        }
        else
        {
            await LoadAtlasAsync(localPath).ConfigureAwait(true);
        }
    }

    /// <inheritdoc />
    protected override async Task OnSaveAsync(CancellationToken cancellationToken)
    {
        if (AtlasBitmap is null || string.IsNullOrEmpty(AtlasPath))
        {
            return;
        }

        if (_savedIniPath is not null && _savedIniPath.Contains('#'))
        {
            await OnSaveAsAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        string path = _savedIniPath ?? DefaultIniPath;
        var operation = BeginOperation();
        try
        {
            if (await TryWriteMappedImagesAsync(path, operation.Token).ConfigureAwait(true))
            {
                _savedIniPath = path;
                MarkSaved();
                Notifications.ShowSuccess(
                    Localize("TextureEditor.Notify.SaveComplete.Title", "Slices saved"),
                    Localize("TextureEditor.Notify.SaveComplete.Message", SavedMessageFallback, Path.GetFileName(path)),
                    NotificationDurations.Medium);
            }
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Slice save failed");
            Notifications.ShowError(
                Localize("TextureEditor.Notify.SaveFailed.Title", "Save failed"),
                ex.Message,
                NotificationDurations.Long);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    /// <inheritdoc />
    protected override async Task OnSaveAsAsync(CancellationToken cancellationToken)
    {
        // Save As adopts the picked path as the working file for later saves,
        // while ExportIniAsync writes a copy and leaves the working file alone.
        if (AtlasBitmap is null)
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
            Title = Localize("TextureEditor.Dialog.SaveIni", "Save MappedImages INI"),
            SuggestedFileName = Path.GetFileName(DefaultIniPath),
            FileTypeChoices =
            [
                new FilePickerFileType(Localize("TextureEditor.Dialog.IniFiles", "INI files"))
                {
                    Patterns = [TextureEditorConstants.MappedImagesFilePattern],
                },
            ],
        }).ConfigureAwait(true);

        if (file is null)
        {
            return;
        }

        string path = file.Path.LocalPath;
        var operation = BeginOperation();
        try
        {
            if (await TryWriteMappedImagesAsync(path, operation.Token).ConfigureAwait(true))
            {
                _savedIniPath = path;
                MarkSaved();
                Notifications.ShowSuccess(
                    Localize("TextureEditor.Notify.SaveComplete.Title", "Slices saved"),
                    Localize("TextureEditor.Notify.SaveComplete.Message", SavedMessageFallback, Path.GetFileName(path)),
                    NotificationDurations.Medium);
            }
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Slice save failed");
            Notifications.ShowError(
                Localize("TextureEditor.Notify.SaveFailed.Title", "Save failed"),
                ex.Message,
                NotificationDurations.Long);
        }
        finally
        {
            EndOperation(operation);
        }
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (IsDisposed || !disposing)
        {
            return;
        }

        foreach (var slice in Slices)
        {
            slice.PropertyChanged -= OnSlicePropertyChanged;
            DisposeThumbnail(slice.Thumbnail);
        }

        Slices.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        _archiveIndexCache.Clear();
        AtlasBitmap?.Dispose();
        AtlasBitmap = null;
        base.Dispose(disposing);
    }

    private static void DisposeThumbnail(Avalonia.Media.IImage? thumbnail)
    {
        // CroppedBitmap is a resourceless view over the shared AtlasBitmap, so
        // crops are intentionally left for the GC: disposing a crop would also
        // dispose its Source and kill the atlas every thumbnail is cut from.
        if (thumbnail is IDisposable disposable && thumbnail is not CroppedBitmap)
        {
            disposable.Dispose();
        }
    }

    private static string? FindFirstTextureFile(IEnumerable<EditorFileTreeNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile && TextureEditorConstants.TextureExtensions.Contains(Path.GetExtension(node.FullPath), StringComparer.OrdinalIgnoreCase))
            {
                return node.FullPath;
            }

            string? child = FindFirstTextureFile(node.Children);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static string? FindTextureInNodes(IEnumerable<EditorFileTreeNodeViewModel> nodes, string textureFileName)
    {
        foreach (var node in nodes)
        {
            if (node.IsFile && MappedImageTextureMatcher.Matches(Path.GetFileName(node.FullPath), textureFileName))
            {
                return node.FullPath;
            }

            string? child = FindTextureInNodes(node.Children, textureFileName);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static string StripArchiveFragment(string path) =>
        path.Contains('#') ? path.Split('#')[0] : path;

    private static string GetEntryFileName(string path)
    {
        int lastSep = path.LastIndexOfAny(['/', '\\']);
        return lastSep >= 0 ? path[(lastSep + 1)..] : path;
    }

    private static bool IsSameDirectory(string left, string right)
    {
        try
        {
            // Default macOS volumes are case-insensitive, so directory equality must
            // ignore casing there; case-sensitive platforms keep ordinal semantics.
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string cleanLeft = StripArchiveFragment(left);
            string cleanRight = StripArchiveFragment(right);
            string leftDirectory = Path.GetFullPath(Path.GetDirectoryName(cleanLeft) ?? cleanLeft);
            string rightDirectory = Path.GetFullPath(Path.GetDirectoryName(cleanRight) ?? cleanRight);
            return string.Equals(leftDirectory, rightDirectory, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException)
        {
            return false;
        }
    }

    private static bool IsUnderDirectory(string path, string root)
    {
        try
        {
            var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            string cleanPath = StripArchiveFragment(path);
            string fullPath = Path.GetFullPath(cleanPath);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullPath.StartsWith(fullRoot, comparison);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or SecurityException)
        {
            return false;
        }
    }

    private static HashSet<string> GetTextureCandidates(string textureFileName)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { textureFileName };
        string baseName = MappedImageTextureMatcher.Normalize(textureFileName);
        foreach (string extension in TextureEditorConstants.TextureExtensions)
        {
            candidates.Add(baseName + extension);
        }

        return candidates;
    }

    private static int CompareBigArchiveTexturePriority(string a, string b)
    {
        bool aIsZh = a.Contains(TextureEditorConstants.ZeroHourMarker, StringComparison.OrdinalIgnoreCase);
        bool bIsZh = b.Contains(TextureEditorConstants.ZeroHourMarker, StringComparison.OrdinalIgnoreCase);
        if (aIsZh != bIsZh)
        {
            return aIsZh ? -1 : 1;
        }

        bool aIsTextureOrWindow = a.Contains(TextureEditorConstants.WindowFolder, StringComparison.OrdinalIgnoreCase) ||
                                  a.Contains(TextureEditorConstants.TextureFolder, StringComparison.OrdinalIgnoreCase);
        bool bIsTextureOrWindow = b.Contains(TextureEditorConstants.WindowFolder, StringComparison.OrdinalIgnoreCase) ||
                                  b.Contains(TextureEditorConstants.TextureFolder, StringComparison.OrdinalIgnoreCase);
        if (aIsTextureOrWindow != bIsTextureOrWindow)
        {
            return aIsTextureOrWindow ? -1 : 1;
        }

        return StringComparer.OrdinalIgnoreCase.Compare(a, b);
    }

    private string? FindTextureInBigArchives(string directory, HashSet<string> candidates, bool recurse = false)
    {
        if (!Directory.Exists(directory))
        {
            return null;
        }

        var enumOptions = new EnumerationOptions
        {
            RecurseSubdirectories = recurse,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        string[] bigFiles;
        try
        {
            bigFiles = Directory.GetFiles(directory, TextureEditorConstants.BigArchiveSearchPattern, enumOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        if (bigFiles.Length == 0)
        {
            return null;
        }

        Array.Sort(bigFiles, CompareBigArchiveTexturePriority);

        foreach (var bigFile in bigFiles)
        {
            if (!_archiveIndexCache.TryGetValue(bigFile, out var entries))
            {
                if (!BigArchiveReader.TryReadIndex(bigFile, out entries))
                {
                    continue;
                }

                _archiveIndexCache[bigFile] = entries;
            }

            var matching = entries.Values
                .Select(entry => entry.Path)
                .FirstOrDefault(path => candidates.Contains(GetEntryFileName(path)));

            if (matching is not null)
            {
                return TextureEditorConstants.FormatArchiveReference(bigFile, matching);
            }
        }

        return null;
    }

    partial void OnAtlasBitmapChanged(Bitmap? value)
    {
        HasDocument = value is not null;
        AddSliceCommand.NotifyCanExecuteChanged();
        ExportIniCommand.NotifyCanExecuteChanged();
        ExportSheetCommand.NotifyCanExecuteChanged();
        RefreshEditorCommands();

        // Picker thumbnails are crops of AtlasBitmap: rebuild them so no item
        // references a disposed atlas or keeps thumbnails for the previous texture.
        RefreshRegistryImages();
    }

    partial void OnSelectedSliceChanged(TextureSliceViewModel? value)
    {
        foreach (var slice in Slices)
        {
            slice.IsSelected = ReferenceEquals(slice, value);
        }

        RefreshEditorCommands();
    }

    [RelayCommand]
    private async Task ScanRegistryAsync()
    {
        string? directory = FileExplorer.Directory;
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            var topLevel = GetTopLevel();
            if (topLevel is null)
            {
                return;
            }

            var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Localize("TextureEditor.Dialog.ScanFolder", "Select MappedImages folder"),
                AllowMultiple = false,
            }).ConfigureAwait(true);

            if (folders.Count == 0)
            {
                return;
            }

            directory = folders[0].Path.LocalPath;
            FileExplorer.Directory = directory;
        }

        try
        {
            await RunOperationAsync(async operationToken =>
            {
                bool scanned = await ScanDirectoryCoreAsync(directory, operationToken).ConfigureAwait(true);
                if (scanned && Slices.Count == 0)
                {
                    LoadSlicesForAtlas();
                    MarkSaved();
                }
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Registry scan failed");
            Notifications.ShowError(
                Localize(ScanFailedTitleKey, ScanFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private async Task<bool> ScanDirectoryCoreAsync(string folder, CancellationToken cancellationToken)
    {
        _archiveIndexCache.Clear();
        var result = await registry.ScanDirectoryAsync(folder, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();
        RefreshRegistryImages();
        if (result.Data is null)
        {
            Notifications.ShowError(
                Localize(ScanFailedTitleKey, ScanFailedTitleFallback),
                result.FirstError ?? Localize("TextureEditor.Notify.ScanFailed.Message", "Failed to scan MappedImages folder."),
                NotificationDurations.Long);
            return false;
        }

        if (result.Success)
        {
            Notifications.ShowSuccess(
                Localize("TextureEditor.Notify.ScanComplete.Title", "Scan complete"),
                Localize("TextureEditor.Notify.ScanComplete.Message", "Indexed {0} mapped images from {1} files.", result.Data.ImagesIndexed, result.Data.FilesScanned),
                NotificationDurations.Medium);
            return true;
        }

        // A partial scan still commits the valid entries, so follow-up work
        // runs and the failures surface as a warning instead of an error.
        Notifications.ShowWarning(
            Localize("TextureEditor.Notify.ScanPartial.Title", "Scan completed with errors"),
            Localize("TextureEditor.Notify.ScanPartial.Message", "Indexed {0} mapped images from {1} files, but {2} entries failed: {3}.", result.Data.ImagesIndexed, result.Data.FilesScanned, result.Errors.Count, result.FirstError ?? string.Empty),
            NotificationDurations.Long);
        return true;
    }

    [RelayCommand]
    private async Task AutoPackAsync()
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localize("TextureEditor.Dialog.PackFolder", "Select folder with loose images"),
            AllowMultiple = false,
        }).ConfigureAwait(true);

        if (folders.Count == 0)
        {
            return;
        }

        if (!await ConfirmDiscardUnsavedAsync(CancellationToken.None).ConfigureAwait(true))
        {
            return;
        }

        string sourceDir = folders[0].Path.LocalPath;
        try
        {
            await RunOperationAsync(async operationToken =>
            {
                var request = new TextureAtlasBuildRequest(
                    sourceDir,
                    Path.Combine(sourceDir, TextureEditorConstants.PackedAtlasTextureFileName),
                    Path.Combine(sourceDir, TextureEditorConstants.PackedAtlasIniFileName));
                var built = await packingService.BuildAtlasAsync(request, imageLoader, operationToken).ConfigureAwait(true);
                operationToken.ThrowIfCancellationRequested();
                if (built.Failed || built.Data is null)
                {
                    Notifications.ShowError(
                        Localize("TextureEditor.Notify.PackFailed.Title", PackFailedTitleFallback),
                        built.FirstError ?? string.Empty,
                        NotificationDurations.Long);
                    return;
                }

                if (!await ConfirmOverwriteAsync(request, operationToken).ConfigureAwait(true))
                {
                    return;
                }

                if (!await WritePackOutputsAsync(request, built.Data.TextureBytes, built.Data.IniContent, operationToken).ConfigureAwait(true))
                {
                    return;
                }

                if (!OpenDecodedAtlas(built.Data.Sheet, request.TargetTexture))
                {
                    return;
                }

                ReplaceSlices(built.Data.MappedImages);
                registry.ImportDefinitions(built.Data.MappedImages);
                RefreshRegistryImages();
                FileExplorer.CurrentPath = request.TargetTexture;
                MarkSaved();
                Notifications.ShowSuccess(
                    Localize("TextureEditor.Notify.PackComplete.Title", "Atlas packed"),
                    Localize("TextureEditor.Notify.PackComplete.Message", "Packed {0} sprites into a {1}x{2} sheet.", built.Data.MappedImages.Count, built.Data.Sheet.Width, built.Data.Sheet.Height),
                    NotificationDurations.Medium);
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, PackFailedTitleFallback);
            Notifications.ShowError(
                Localize("TextureEditor.Notify.PackFailed.Title", PackFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    [RelayCommand(CanExecute = nameof(HasAtlas))]
    private async Task ExportIniAsync()
    {
        // Export writes a copy without adopting the path or clearing dirty state,
        // while OnSaveAsAsync adopts the picked path as the working file.
        if (AtlasBitmap is null)
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
            Title = Localize("TextureEditor.Dialog.ExportIni", "Export MappedImages INI"),
            SuggestedFileName = Path.GetFileNameWithoutExtension(AtlasFileName) + TextureEditorConstants.MappedImagesExtension,
            FileTypeChoices =
            [
                new FilePickerFileType(Localize("TextureEditor.Dialog.IniFiles", "INI files"))
                {
                    Patterns = [TextureEditorConstants.MappedImagesFilePattern],
                },
            ],
        }).ConfigureAwait(true);

        if (file is null)
        {
            return;
        }

        string path = file.Path.LocalPath;
        try
        {
            await RunOperationAsync(async operationToken =>
            {
                if (await TryWriteMappedImagesAsync(path, operationToken).ConfigureAwait(true))
                {
                    Notifications.ShowSuccess(
                        Localize("TextureEditor.Notify.ExportComplete.Title", "Export complete"),
                        Localize("TextureEditor.Notify.ExportComplete.Message", SavedMessageFallback, Path.GetFileName(path)),
                        NotificationDurations.Medium);
                }
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "INI export failed");
            Notifications.ShowError(
                Localize("TextureEditor.Notify.ExportFailed.Title", "Export failed"),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    [RelayCommand(CanExecute = nameof(HasAtlas))]
    private async Task ExportSheetAsync()
    {
        if (AtlasBitmap is null || _atlasDecoded is null)
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
            Title = Localize("TextureEditor.Dialog.ExportSheet", "Export texture sheet"),
            SuggestedFileName = Path.GetFileNameWithoutExtension(AtlasFileName),
            FileTypeChoices =
            [
                new FilePickerFileType("TGA")
                {
                    Patterns = ["*.tga"],
                },
                new FilePickerFileType("PNG")
                {
                    Patterns = ["*.png"],
                },
            ],
        }).ConfigureAwait(true);

        if (file is null)
        {
            return;
        }

        string path = file.Path.LocalPath;
        try
        {
            await RunOperationAsync(async operationToken =>
            {
                var saved = Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                    ? await bitmapService.SavePngAsync(_atlasDecoded, path, operationToken).ConfigureAwait(true)
                    : await bitmapService.SaveTgaAsync(_atlasDecoded, path, operationToken).ConfigureAwait(true);

                if (saved.Success)
                {
                    Notifications.ShowSuccess(
                        Localize("TextureEditor.Notify.ExportComplete.Title", "Export complete"),
                        Localize("TextureEditor.Notify.ExportComplete.Message", SavedMessageFallback, Path.GetFileName(path)),
                        NotificationDurations.Medium);
                }
                else
                {
                    Notifications.ShowError(
                        Localize("TextureEditor.Notify.ExportFailed.Title", "Export failed"),
                        saved.FirstError ?? string.Empty,
                        NotificationDurations.Long);
                }
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sheet export failed");
            Notifications.ShowError(
                Localize("TextureEditor.Notify.ExportFailed.Title", "Export failed"),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    [RelayCommand(CanExecute = nameof(HasAtlas))]
    private void AddSlice()
    {
        if (AtlasBitmap is null)
        {
            return;
        }

        var slice = new TextureSliceViewModel(new MappedImageDefinition(
            UniqueSliceName($"Slice{Slices.Count + 1}"),
            AtlasFileName,
            AtlasBitmap.PixelSize.Width,
            AtlasBitmap.PixelSize.Height,
            0,
            0,
            Math.Min(TextureEditorConstants.CameoLargeWidth, AtlasBitmap.PixelSize.Width),
            Math.Min(TextureEditorConstants.CameoLargeHeight, AtlasBitmap.PixelSize.Height)));
        slice.UpdateZoom(Zoom);
        TrackSlice(slice);
        SelectedSlice = slice;
        PushUndo(new TextureEditAction(
            Localize("TextureEditor.History.AddSlice", "Add slice"),
            () =>
            {
                if (!Slices.Contains(slice))
                {
                    TrackSlice(slice);
                }

                SelectedSlice = slice;
                MarkDirty();
            },
            () =>
            {
                UntrackSlice(slice);
                MarkDirty();
            }));
        MarkDirty();
    }

    [RelayCommand]
    private void ApplyPreset(string? preset)
    {
        if (SelectedSlice is null)
        {
            return;
        }

        var (width, height) = preset switch
        {
            TextureEditorConstants.PresetLargeCameo => (TextureEditorConstants.CameoLargeWidth, TextureEditorConstants.CameoLargeHeight),
            TextureEditorConstants.PresetSmallCameo => (TextureEditorConstants.CameoSmallWidth, TextureEditorConstants.CameoSmallHeight),
            TextureEditorConstants.PresetHudButton => (TextureEditorConstants.HudButtonWidth, TextureEditorConstants.HudButtonHeight),
            TextureEditorConstants.Preset128 => (TextureEditorConstants.PresetMediumSize, TextureEditorConstants.PresetMediumSize),
            TextureEditorConstants.Preset256 => (TextureEditorConstants.PresetLargeSize, TextureEditorConstants.PresetLargeSize),
            TextureEditorConstants.PresetFillX => (AtlasPixelWidth - SelectedSlice.Left, SelectedSlice.Height),
            TextureEditorConstants.PresetFillY => (SelectedSlice.Width, AtlasPixelHeight - SelectedSlice.Top),
            TextureEditorConstants.PresetFill => (AtlasPixelWidth - SelectedSlice.Left, AtlasPixelHeight - SelectedSlice.Top),
            _ => (SelectedSlice.Width, SelectedSlice.Height),
        };

        int maxWidth = AtlasPixelWidth > 0 ? Math.Max(1, AtlasPixelWidth - SelectedSlice.Left) : width;
        int maxHeight = AtlasPixelHeight > 0 ? Math.Max(1, AtlasPixelHeight - SelectedSlice.Top) : height;
        width = Math.Clamp(width, 1, maxWidth);
        height = Math.Clamp(height, 1, maxHeight);

        int origRight = SelectedSlice.Right;
        int origBottom = SelectedSlice.Bottom;
        var slice = SelectedSlice;

        SelectedSlice.Right = SelectedSlice.Left + width;
        SelectedSlice.Bottom = SelectedSlice.Top + height;

        int newRight = SelectedSlice.Right;
        int newBottom = SelectedSlice.Bottom;

        PushUndo(new TextureEditAction(
            Localize("TextureEditor.History.ApplyPreset", "Apply size preset"),
            () =>
            {
                slice.Right = newRight;
                slice.Bottom = newBottom;
                MarkDirty();
            },
            () =>
            {
                slice.Right = origRight;
                slice.Bottom = origBottom;
                MarkDirty();
            }));

        MarkDirty();
    }

    private FileExplorerViewModel CreateFileExplorer()
    {
        var explorer = new FileExplorerViewModel(logger);
        explorer.FilePatterns = TextureEditorConstants.ExplorerFilePatterns;
        explorer.ShowFileExtensions = true;
        explorer.ExcludedDirectoryNames = [ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.DefaultReleaseDir];
        explorer.BrowseFolderAsync = BrowseExplorerFolderAsync;
        explorer.DirectoryAdoptedAsync = OnExplorerDirectoryAdoptedAsync;
        explorer.FileActivated += OnExplorerFileActivated;
        return explorer;
    }

    private async Task<string?> BrowseExplorerFolderAsync(CancellationToken cancellationToken)
    {
        var topLevel = GetTopLevel();
        if (topLevel is null)
        {
            return null;
        }

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Localize("TextureEditor.Dialog.ExplorerFolder", "Select project folder"),
            AllowMultiple = false,
        }).ConfigureAwait(true);
        if (folders.Count == 0)
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return folders[0].TryGetLocalPath();
    }

    private async Task OnExplorerDirectoryAdoptedAsync(string folder, CancellationToken cancellationToken)
    {
        try
        {
            bool scanSucceeded = await RunOperationAsync(operationToken => ScanDirectoryCoreAsync(folder, operationToken)).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();

            if (scanSucceeded)
            {
                string? texture = FindFirstTextureFile(FileExplorer.Nodes);
                if (texture is not null)
                {
                    await LoadAtlasAsync(texture).ConfigureAwait(true);
                }
                else if (RegistryImages.Count > 0)
                {
                    var firstEntry = RegistryImages[0];
                    await LoadRegistryEntryAsync(firstEntry, explicitOpen: true).ConfigureAwait(true);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Folder scan failed for {Folder}", folder);
            Notifications.ShowError(
                Localize(ScanFailedTitleKey, ScanFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private void OnExplorerFileActivated(object? sender, EditorFileTreeNodeViewModel node)
    {
        // Fire-and-forget activation bypasses command re-entrancy protection,
        // so ignore activations while another operation owns the editor state.
        if (node.IsDirectory || IsBusy)
        {
            return;
        }

        string extension = Path.GetExtension(node.FullPath);
        if (extension.Equals(TextureEditorConstants.MappedImagesExtension, StringComparison.OrdinalIgnoreCase))
        {
            _ = OpenIniFileAsync(node.FullPath);
            return;
        }

        if (TextureEditorConstants.TextureExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            _ = LoadAtlasAsync(node.FullPath);
        }
    }

    private async Task<bool> LoadAtlasAsync(string path)
    {
        bool loaded = false;
        try
        {
            await RunOperationAsync(async operationToken =>
            {
                // The busy guard engages synchronously on entry, so the confirm
                // dialog runs under it: a second activation during the prompt
                // sees IsBusy and is ignored instead of loading concurrently.
                if (!await ConfirmDiscardUnsavedAsync(operationToken).ConfigureAwait(true))
                {
                    return;
                }

                var decoded = await bitmapService.LoadDecodedAsync(path, operationToken).ConfigureAwait(true);
                operationToken.ThrowIfCancellationRequested();
                if (decoded.Failed || decoded.Data is null)
                {
                    Notifications.ShowError(
                        Localize(OpenFailedTitleKey, OpenFailedTitleFallback),
                        decoded.FirstError ?? string.Empty,
                        NotificationDurations.Long);
                    return;
                }

                if (!OpenDecodedAtlas(decoded.Data, path))
                {
                    return;
                }

                await ImportSiblingIniAsync(path, operationToken).ConfigureAwait(true);
                operationToken.ThrowIfCancellationRequested();
                LoadSlicesForAtlas();
                FileExplorer.CurrentPath = path;
                MarkSaved();
                loaded = true;
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open atlas {Path}", path);
            Notifications.ShowError(
                Localize(OpenFailedTitleKey, OpenFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
        }

        return loaded;
    }

    private async Task ImportSiblingIniAsync(string atlasPath, CancellationToken cancellationToken)
    {
        if (atlasPath.Contains('#'))
        {
            return;
        }

        string sibling = Path.Combine(
            Path.GetDirectoryName(atlasPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(atlasPath) + TextureEditorConstants.MappedImagesExtension);
        if (!File.Exists(sibling))
        {
            return;
        }

        var parsed = await parser.ParseFileAsync(sibling, cancellationToken).ConfigureAwait(true);
        if (parsed.Data is null)
        {
            logger.LogWarning("Sibling INI {Path} failed to parse: {Error}", sibling, parsed.FirstError ?? "unknown");
            Notifications.ShowWarning(
                Localize(ImportFailedTitleKey, ImportFailedTitleFallback),
                parsed.FirstError ?? Localize("TextureEditor.Notify.ImportFailed.Message", "No mapped images found."),
                NotificationDurations.Medium);
            return;
        }

        if (parsed.Data.Count == 0)
        {
            logger.LogWarning("Sibling INI {Path} holds no mapped images.", sibling);
            Notifications.ShowWarning(
                Localize(ImportFailedTitleKey, ImportFailedTitleFallback),
                Localize("TextureEditor.Notify.ImportFailed.Message", "No mapped images found."),
                NotificationDurations.Medium);
            return;
        }

        registry.ImportDefinitions(parsed.Data);
        RefreshRegistryImages();
    }

    private async Task OpenIniFileAsync(string path)
    {
        try
        {
            await RunOperationAsync(async operationToken =>
            {
                var parsed = await parser.ParseFileAsync(path, operationToken).ConfigureAwait(true);
                operationToken.ThrowIfCancellationRequested();
                if (parsed.Data is null || parsed.Data.Count == 0)
                {
                    Notifications.ShowError(
                        Localize(ImportFailedTitleKey, ImportFailedTitleFallback),
                        parsed.FirstError ?? Localize("TextureEditor.Notify.ImportFailed.Message", "No mapped images found."),
                        NotificationDurations.Long);
                    return;
                }

                var groups = parsed.Data
                    .GroupBy(image => image.TextureFileName, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(group => group.Count())
                    .ToList();

                if (await TryOpenCurrentAtlasGroupAsync(groups, path, parsed.Data, operationToken).ConfigureAwait(true))
                {
                    return;
                }

                if (await TryOpenTargetTextureGroupAsync(groups, path, parsed.Data, operationToken).ConfigureAwait(true))
                {
                    return;
                }

                if (await TryOpenPlaceholderGroupAsync(groups[0], path, parsed.Data, groups.Count, operationToken).ConfigureAwait(true))
                {
                    return;
                }

                CommitParsedDefinitions(parsed.Data);
                Notifications.ShowWarning(
                    Localize("TextureEditor.Notify.ImportComplete.Title", "Imported"),
                    Localize("TextureEditor.Notify.ImportComplete.LibraryOnly", "Imported {0} mapped images into Library.", parsed.Data.Count),
                    NotificationDurations.Medium);
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open INI {Path}", path);
            Notifications.ShowError(
                Localize(ImportFailedTitleKey, ImportFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private void CommitParsedDefinitions(IReadOnlyList<MappedImageDefinition> definitions)
    {
        registry.ImportDefinitions(definitions);
        RefreshRegistryImages();
    }

    private async Task<bool> TryOpenCurrentAtlasGroupAsync(
        List<IGrouping<string, MappedImageDefinition>> groups,
        string path,
        IReadOnlyList<MappedImageDefinition> allDefinitions,
        CancellationToken operationToken)
    {
        bool isSourceCompatible = !string.IsNullOrEmpty(AtlasPath) &&
            (IsSameDirectory(path, AtlasPath) ||
             (!string.IsNullOrEmpty(FileExplorer.Directory) &&
              IsUnderDirectory(path, FileExplorer.Directory) &&
              IsUnderDirectory(AtlasPath, FileExplorer.Directory)));

        var currentMatchGroup = (AtlasBitmap is not null && isSourceCompatible)
            ? groups.FirstOrDefault(group => MappedImageTextureMatcher.Matches(group.Key, AtlasFileName))
            : null;

        if (currentMatchGroup is null)
        {
            return false;
        }

        if (HasUnsavedChanges && !await ConfirmDiscardUnsavedAsync(operationToken).ConfigureAwait(true))
        {
            return true;
        }

        CommitParsedDefinitions(allDefinitions);
        ReplaceSlices(currentMatchGroup.ToList());
        _savedIniPath = path;
        FileExplorer.CurrentPath = path;
        MarkSaved();

        string extraInfo = groups.Count > 1
            ? Localize("TextureEditor.Notify.ImportExtraTextures", " ({0} other textures in Library)", groups.Count - 1)
            : string.Empty;
        Notifications.ShowSuccess(
            Localize("TextureEditor.Notify.ImportComplete.Title", "Loaded INI"),
            Localize("TextureEditor.Notify.ImportComplete.LoadedCurrentAtlas", "Loaded {0} slices for {1} from {2}{3}.", currentMatchGroup.Count(), AtlasFileName, Path.GetFileName(path), extraInfo),
            NotificationDurations.Medium);
        return true;
    }

    private async Task<bool> TryOpenTargetTextureGroupAsync(
        List<IGrouping<string, MappedImageDefinition>> groups,
        string path,
        IReadOnlyList<MappedImageDefinition> allDefinitions,
        CancellationToken operationToken)
    {
        if (gameInstallationService is not null && gameInstallationService.CachedInstallations is null)
        {
            await EnsureGameInstallationsLoadedAsync(operationToken).ConfigureAwait(true);
        }

        string? targetTexturePath = null;
        IGrouping<string, MappedImageDefinition>? targetGroup = null;

        foreach (var group in groups)
        {
            string? candidate = FindTextureFile(group.Key, path);
            if (candidate is not null)
            {
                targetTexturePath = candidate;
                targetGroup = group;
                break;
            }
        }

        if (targetTexturePath is null)
        {
            return false;
        }

        Debug.Assert(targetGroup is not null, "Target group is guaranteed when target texture path is found.");

        if (HasUnsavedChanges && !await ConfirmDiscardUnsavedAsync(operationToken).ConfigureAwait(true))
        {
            return true;
        }

        var decoded = await bitmapService.LoadDecodedAsync(targetTexturePath, operationToken).ConfigureAwait(true);
        operationToken.ThrowIfCancellationRequested();
        if (decoded.Failed || decoded.Data is null || !OpenDecodedAtlas(decoded.Data, targetTexturePath))
        {
            Notifications.ShowError(
                Localize(OpenFailedTitleKey, OpenFailedTitleFallback),
                decoded.FirstError ?? Localize("TextureEditor.Notify.OpenFailed.Message", "Failed to open target texture."),
                NotificationDurations.Long);
            return true;
        }

        CommitParsedDefinitions(allDefinitions);
        ReplaceSlices(targetGroup.ToList());
        _savedIniPath = path;
        FileExplorer.CurrentPath = path;
        MarkSaved();

        string extraInfo = groups.Count > 1
            ? Localize("TextureEditor.Notify.ImportExtraTextures", " ({0} other textures in Library)", groups.Count - 1)
            : string.Empty;
        Notifications.ShowSuccess(
            Localize("TextureEditor.Notify.ImportComplete.Title", "Loaded INI"),
            Localize("TextureEditor.Notify.ImportComplete.LoadedTargetAtlas", "Loaded {0} slices for {1} from {2}{3}.", targetGroup.Count(), Path.GetFileName(targetTexturePath), Path.GetFileName(path), extraInfo),
            NotificationDurations.Medium);
        return true;
    }

    private async Task<bool> TryOpenPlaceholderGroupAsync(
        IGrouping<string, MappedImageDefinition> group,
        string path,
        IReadOnlyList<MappedImageDefinition> allDefinitions,
        int totalGroupsCount,
        CancellationToken operationToken)
    {
        var firstDefinition = group.First();
        if (firstDefinition.TextureWidth <= 0 || firstDefinition.TextureHeight <= 0)
        {
            return false;
        }

        if (HasUnsavedChanges && !await ConfirmDiscardUnsavedAsync(operationToken).ConfigureAwait(true))
        {
            return true;
        }

        var placeholder = TextureBitmapService.CreatePlaceholder(firstDefinition.TextureWidth, firstDefinition.TextureHeight);
        string pseudoPath = Path.Combine(
            string.IsNullOrEmpty(FileExplorer.Directory)
                ? (Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory())
                : FileExplorer.Directory,
            group.Key);

        if (!OpenDecodedAtlas(placeholder, pseudoPath, isPlaceholder: true))
        {
            return false;
        }

        CommitParsedDefinitions(allDefinitions);
        ReplaceSlices(group.ToList());
        _savedIniPath = path;
        FileExplorer.CurrentPath = path;
        MarkSaved();

        string extraInfo = totalGroupsCount > 1
            ? Localize("TextureEditor.Notify.ImportExtraTextures", " ({0} other textures in Library)", totalGroupsCount - 1)
            : string.Empty;
        Notifications.ShowInfo(
            Localize("TextureEditor.Notify.PlaceholderOpened.Title", "Placeholder texture"),
            Localize("TextureEditor.Notify.PlaceholderOpened.Message", "Opened placeholder canvas ({0}x{1}) with {2} slices from {3}{4}.", firstDefinition.TextureWidth, firstDefinition.TextureHeight, group.Count(), Path.GetFileName(path), extraInfo),
            NotificationDurations.Medium);
        return true;
    }

    private void RefreshRegistryImages()
    {
        // Replace the instance so bound pickers rebuild once for the whole
        // catalog instead of once per entry.
        RegistryImages = new ObservableCollection<MappedImageDefinition>(registry.All);
        OnPropertyChanged(nameof(RegistryImages));
    }

    private bool OpenDecodedAtlas(DecodedTexture decoded, string path, bool isPlaceholder = false)
    {
        var bitmap = bitmapService.ToBitmap(decoded);
        if (bitmap.Failed || bitmap.Data is null)
        {
            Notifications.ShowError(
                Localize(OpenFailedTitleKey, OpenFailedTitleFallback),
                bitmap.FirstError ?? string.Empty,
                NotificationDurations.Long);
            return false;
        }

        ClearAtlas();
        _atlasDecoded = decoded;
        AtlasPath = path;
        AtlasBitmap = bitmap.Data;
        IsPlaceholder = isPlaceholder;
        RefreshRegistryImages();
        AddSliceCommand.NotifyCanExecuteChanged();
        ExportIniCommand.NotifyCanExecuteChanged();
        ExportSheetCommand.NotifyCanExecuteChanged();
        return true;
    }

    private void LoadSlicesForAtlas()
    {
        if (AtlasBitmap is null || string.IsNullOrEmpty(AtlasPath))
        {
            return;
        }

        var allMatches = registry.GetByTexture(AtlasFileName) ?? Array.Empty<MappedImageDefinition>();
        if (allMatches.Count == 0)
        {
            return;
        }

        bool isAtlasArchive = AtlasPath.Contains('#');
        var matches = isAtlasArchive
            ? FindArchiveAtlasMatches(allMatches, StripArchiveFragment(AtlasPath))
            : FindLooseAtlasMatches(allMatches);

        if (matches.Count > 0)
        {
            ReplaceSlices(matches);
            return;
        }

        if (!string.IsNullOrEmpty(_savedIniPath))
        {
            var savedIniMatches = FindSavedIniAtlasMatches(allMatches);
            if (savedIniMatches.Count > 0)
            {
                ReplaceSlices(savedIniMatches);
                return;
            }
        }

        // If the atlas was loaded from game archives or an external directory outside the workspace,
        // definitions in the open project/workspace matching this texture must be adopted.
        if (!string.IsNullOrEmpty(FileExplorer.Directory) &&
            !string.IsNullOrEmpty(AtlasPath) &&
            !IsUnderDirectory(StripArchiveFragment(AtlasPath), FileExplorer.Directory))
        {
            var projectMatches = allMatches
                .Where(image => image.SourcePath is not null && IsUnderDirectory(image.SourcePath, FileExplorer.Directory))
                .ToList();
            if (projectMatches.Count > 0)
            {
                ReplaceSlices(projectMatches);
                return;
            }
        }

        var fallbackMatches = allMatches
            .Where(image => image.SourcePath is null)
            .ToList();
        if (fallbackMatches.Count > 0)
        {
            ReplaceSlices(fallbackMatches);
            return;
        }

        ReplaceSlices([]);
    }

    private List<MappedImageDefinition> FindArchiveAtlasMatches(IReadOnlyList<MappedImageDefinition> allMatches, string cleanAtlas)
    {
        return allMatches
            .Where(image => image.SourcePath is not null &&
                            ((image.SourcePath.Contains('#') && string.Equals(StripArchiveFragment(image.SourcePath), cleanAtlas, StringComparison.OrdinalIgnoreCase)) ||
                             (!image.SourcePath.Contains('#') && (IsSameDirectory(image.SourcePath, cleanAtlas) ||
                              (!string.IsNullOrEmpty(FileExplorer.Directory) && IsUnderDirectory(image.SourcePath, FileExplorer.Directory) && IsUnderDirectory(cleanAtlas, FileExplorer.Directory))))))
            .ToList();
    }

    private List<MappedImageDefinition> FindLooseAtlasMatches(IReadOnlyList<MappedImageDefinition> allMatches)
    {
        var sameDirMatches = allMatches
            .Where(image => image.SourcePath is not null && !image.SourcePath.Contains('#') && IsSameDirectory(image.SourcePath, AtlasPath))
            .ToList();
        if (sameDirMatches.Count > 0)
        {
            return sameDirMatches;
        }

        if (!string.IsNullOrEmpty(FileExplorer.Directory) &&
            !string.IsNullOrEmpty(AtlasPath) &&
            IsUnderDirectory(AtlasPath, FileExplorer.Directory))
        {
            return allMatches
                .Where(image => image.SourcePath is not null && !image.SourcePath.Contains('#') && IsUnderDirectory(image.SourcePath, FileExplorer.Directory))
                .ToList();
        }

        return [];
    }

    private List<MappedImageDefinition> FindSavedIniAtlasMatches(IReadOnlyList<MappedImageDefinition> allMatches)
    {
        return allMatches
            .Where(image => string.Equals(image.SourcePath, _savedIniPath, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void AdoptRegistryEntry(MappedImageDefinition definition)
    {
        if (AtlasBitmap is null)
        {
            return;
        }

        var existing = Slices.FirstOrDefault(slice => slice.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            SelectedSlice = existing;
            return;
        }

        var slice = new TextureSliceViewModel(definition);
        slice.UpdateTexture(AtlasFileName, AtlasBitmap.PixelSize.Width, AtlasBitmap.PixelSize.Height);
        slice.UpdateZoom(Zoom);
        TrackSlice(slice);
        SelectedSlice = slice;
        MarkDirty();
    }

    private async Task OpenTextureAndLoadEntryAsync(string texturePath, MappedImageDefinition definition)
    {
        if (!await LoadAtlasAsync(texturePath).ConfigureAwait(true))
        {
            // The user cancelled the discard prompt or the atlas failed to open,
            // and LoadAtlasAsync already reported the outcome.
            return;
        }

        await LoadRegistryEntryAsync(definition, explicitOpen: true).ConfigureAwait(true);
        Notifications.ShowInfo(
            Localize("TextureEditor.Notify.RegistryOpened.Title", "Texture opened"),
            Localize("TextureEditor.Notify.RegistryOpened.Message", "Opened {0} to edit '{1}'.", AtlasFileName, definition.Name),
            NotificationDurations.Medium);
    }

    private async Task HandleRegistryEntryWithoutAtlasAsync(MappedImageDefinition definition, bool explicitOpen)
    {
        try
        {
            if (gameInstallationService is not null && gameInstallationService.CachedInstallations is null)
            {
                await EnsureGameInstallationsLoadedAsync().ConfigureAwait(true);
            }

            string? texturePath = FindTextureFile(definition.TextureFileName, definition.SourcePath);

            if (texturePath is not null)
            {
                await OpenTextureAndLoadEntryAsync(texturePath, definition).ConfigureAwait(true);
                return;
            }

            if (explicitOpen)
            {
                await OpenPlaceholderForEntryAsync(definition).ConfigureAwait(true);
                return;
            }

            logger.LogInformation("Registry entry {Name} not loaded: no atlas is open.", definition.Name);
            Notifications.ShowInfo(
                Localize("TextureEditor.Notify.NoAtlas.Title", "No atlas open"),
                Localize("TextureEditor.Notify.NoAtlas.Message", "Open a texture atlas before adding slices."),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to load registry entry {Name}", definition.Name);
            Notifications.ShowError(
                Localize(OpenFailedTitleKey, OpenFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private bool IsDefinitionInSameDirectory(MappedImageDefinition definition)
    {
        return definition.SourcePath is null
            || IsSameDirectory(definition.SourcePath, AtlasPath)
            || (!string.IsNullOrEmpty(FileExplorer.Directory)
                && IsUnderDirectory(definition.SourcePath, FileExplorer.Directory)
                && !string.IsNullOrEmpty(AtlasPath)
                && IsUnderDirectory(AtlasPath, FileExplorer.Directory));
    }

    private bool TryAdoptMatchingAtlasEntry(MappedImageDefinition definition, bool explicitOpen, bool sameDirectory)
    {
        bool textureMatches = MappedImageTextureMatcher.Matches(definition.TextureFileName, AtlasFileName);
        if (textureMatches && (sameDirectory || explicitOpen))
        {
            if (Slices.Count == 0)
            {
                LoadSlicesForAtlas();
            }

            AdoptRegistryEntry(definition);
            return true;
        }

        return false;
    }

    private async Task ResolveExternalAtlasEntryAsync(MappedImageDefinition definition, bool explicitOpen, bool sameDirectory)
    {
        try
        {
            if (gameInstallationService is not null && gameInstallationService.CachedInstallations is null)
            {
                await EnsureGameInstallationsLoadedAsync().ConfigureAwait(true);
            }

            string? resolved = FindTextureFile(definition.TextureFileName, definition.SourcePath);

            if (resolved is not null && !string.Equals(resolved, AtlasPath, StringComparison.OrdinalIgnoreCase))
            {
                await OpenTextureAndLoadEntryAsync(resolved, definition).ConfigureAwait(true);
                return;
            }

            if (resolved is not null && sameDirectory)
            {
                AdoptRegistryEntry(definition);
                return;
            }

            if (explicitOpen)
            {
                await OpenPlaceholderForEntryAsync(definition).ConfigureAwait(true);
                return;
            }

            logger.LogWarning(
                "Registry entry {Name} targets {Expected} but no matching texture file was found.",
                definition.Name,
                definition.TextureFileName);
            Notifications.ShowWarning(
                Localize("TextureEditor.Notify.TextureNotFound.Title", "Texture not found"),
                Localize("TextureEditor.Notify.TextureNotFound.Message", "'{0}' belongs to {1}, which was not found. Open the folder containing {1} and try again.", definition.Name, definition.TextureFileName),
                NotificationDurations.Medium);
        }
        catch (OperationCanceledException)
        {
            // Cancelled
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to resolve external atlas entry {Name}", definition.Name);
            Notifications.ShowError(
                Localize(OpenFailedTitleKey, OpenFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private async Task OpenPlaceholderForEntryAsync(MappedImageDefinition definition)
    {
        if (definition.TextureWidth <= 0 || definition.TextureHeight <= 0)
        {
            logger.LogWarning("Cannot open placeholder for {Name}: invalid dimensions {W}x{H}", definition.Name, definition.TextureWidth, definition.TextureHeight);
            Notifications.ShowWarning(
                Localize("TextureEditor.Notify.TextureNotFound.Title", "Texture not found"),
                Localize("TextureEditor.Notify.TextureNotFound.Message", "'{0}' belongs to {1}, which was not found. Open the folder containing {1} and try again.", definition.Name, definition.TextureFileName),
                NotificationDurations.Medium);
            return;
        }

        try
        {
            await RunOperationAsync(async operationToken =>
            {
                if (HasUnsavedChanges && !await ConfirmDiscardUnsavedAsync(operationToken).ConfigureAwait(true))
                {
                    return;
                }

                var placeholder = TextureBitmapService.CreatePlaceholder(definition.TextureWidth, definition.TextureHeight);
                string? cleanSource = definition.SourcePath is not null ? StripArchiveFragment(definition.SourcePath) : null;
                string pseudoDir = ResolvePlaceholderDirectory(cleanSource);
                string pseudoPath = Path.Combine(pseudoDir, definition.TextureFileName);

                if (!OpenDecodedAtlas(placeholder, pseudoPath, isPlaceholder: true))
                {
                    return;
                }

                if (definition.SourcePath is not null && !definition.SourcePath.Contains('#'))
                {
                    _savedIniPath = definition.SourcePath;
                }

                LoadSlicesForAtlas();
                AdoptRegistryEntry(definition);
                MarkSaved();

                Notifications.ShowInfo(
                    Localize("TextureEditor.Notify.PlaceholderOpened.Title", "Placeholder texture"),
                    Localize(
                        "TextureEditor.Notify.PlaceholderSingleOpened.Message",
                        "Opened {0}x{1} placeholder for '{2}' (texture file not found on disk or game archives).",
                        definition.TextureWidth,
                        definition.TextureHeight,
                        definition.TextureFileName),
                    NotificationDurations.Medium);
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to open placeholder for {Name}", definition.Name);
            Notifications.ShowError(
                Localize(OpenFailedTitleKey, "Failed to open placeholder"),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private string ResolvePlaceholderDirectory(string? cleanSource)
    {
        if (!string.IsNullOrEmpty(FileExplorer.Directory))
        {
            return FileExplorer.Directory;
        }

        if (cleanSource is not null)
        {
            string? dir = Path.GetDirectoryName(cleanSource);
            if (!string.IsNullOrEmpty(dir))
            {
                return dir;
            }
        }

        return AppDataPathHelper.GetDataRoot();
    }

    private string? FindTextureFile(string textureFileName, string? sourcePath)
    {
        var candidates = GetTextureCandidates(textureFileName);
        var probeDirectories = GetProbeDirectories(sourcePath);

        return FindTextureInDirectories(probeDirectories, candidates)
            ?? FindTextureInExplorerTree(candidates)
            ?? FindTextureInArchives(probeDirectories, candidates)
            ?? FindTextureInInstallations(candidates);
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private string? FindTextureInDirectories(IReadOnlyList<string> probeDirectories, IReadOnlyCollection<string> candidates)
    {
        foreach (string directory in probeDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string? found = ProbeDirectoryCandidates(directory, candidates);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private string? ProbeDirectoryCandidates(string directory, IReadOnlyCollection<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            string probed = Path.Combine(directory, candidate);
            if (File.Exists(probed))
            {
                return probed;
            }

            string artTextures = Path.Combine(directory, TextureEditorConstants.ArtFolder, TextureEditorConstants.TexturesFolder, candidate);
            if (File.Exists(artTextures))
            {
                return artTextures;
            }

            string texturesDir = Path.Combine(directory, TextureEditorConstants.TexturesFolder, candidate);
            if (File.Exists(texturesDir))
            {
                return texturesDir;
            }

            string windowDir = Path.Combine(directory, TextureEditorConstants.WindowFolder, candidate);
            if (File.Exists(windowDir))
            {
                return windowDir;
            }
        }

        return null;
    }

    private string? FindTextureInExplorerTree(IReadOnlyCollection<string> candidates)
    {
        if (FileExplorer is null)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            string? inTree = FindTextureInNodes(FileExplorer.Nodes, candidate);
            if (inTree is not null)
            {
                return inTree;
            }
        }

        return null;
    }

    private string? FindTextureInArchives(IReadOnlyList<string> probeDirectories, HashSet<string> candidates)
    {
        foreach (string directory in probeDirectories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            string? fromBig = FindTextureInBigArchives(directory, candidates, recurse: false);
            if (fromBig is not null)
            {
                return fromBig;
            }
        }

        return null;
    }

    private string? FindTextureInInstallations(HashSet<string> candidates)
    {
        var gameDirectories = GetGameInstallationDirectories();
        foreach (string gameDir in gameDirectories)
        {
            if (!Directory.Exists(gameDir))
            {
                continue;
            }

            string? found = ProbeInstallationCandidates(gameDir, candidates);
            if (found is not null)
            {
                return found;
            }

            string? fromGameBig = FindTextureInBigArchives(gameDir, candidates, recurse: false);
            if (fromGameBig is not null)
            {
                return fromGameBig;
            }

            string dataDir = Path.Combine(gameDir, "Data");
            if (Directory.Exists(dataDir))
            {
                string? fromDataBig = FindTextureInBigArchives(dataDir, candidates, recurse: false);
                if (fromDataBig is not null)
                {
                    return fromDataBig;
                }
            }
        }

        return null;
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private string? ProbeInstallationCandidates(string gameDir, HashSet<string> candidates)
    {
        foreach (string candidate in candidates)
        {
            string probed = Path.Combine(gameDir, candidate);
            if (File.Exists(probed))
            {
                return probed;
            }

            string artTextures = Path.Combine(gameDir, TextureEditorConstants.ArtFolder, TextureEditorConstants.TexturesFolder, candidate);
            if (File.Exists(artTextures))
            {
                return artTextures;
            }

            string windowDir = Path.Combine(gameDir, TextureEditorConstants.WindowFolder, candidate);
            if (File.Exists(windowDir))
            {
                return windowDir;
            }

            string windowMenusDir = Path.Combine(gameDir, TextureEditorConstants.WindowFolder, TextureEditorConstants.MenusFolder, candidate);
            if (File.Exists(windowMenusDir))
            {
                return windowMenusDir;
            }
        }

        return null;
    }

    private IReadOnlyList<string> GetGameInstallationDirectories()
    {
        if (gameInstallationService?.CachedInstallations is not { Count: > 0 } installations)
        {
            return [];
        }

        return installations
            .SelectMany(GetInstallationDirectoryCandidates)
            .Where(dir => !string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private IEnumerable<string> GetInstallationDirectoryCandidates(IGameInstallation inst)
    {
        string? generalsCandidate = inst.EffectiveGeneralsArchivePath;
        if (string.IsNullOrEmpty(generalsCandidate) && inst.HasGenerals)
        {
            generalsCandidate = inst.GeneralsPath;
        }

        string?[] candidates =
        [
            inst.HasZeroHour ? inst.ZeroHourPath : null,
            inst.BundledGeneralsPath,
            generalsCandidate,
            inst.InstallationPath,
        ];

        return candidates.OfType<string>();
    }

    private async Task EnsureGameInstallationsLoadedAsync(CancellationToken cancellationToken = default)
    {
        if (gameInstallationService is null || gameInstallationService.CachedInstallations is not null)
        {
            return;
        }

        try
        {
            await gameInstallationService.GetAllInstallationsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogDebug(ex, "Failed to load game installations for texture lookup");
        }
    }

    private List<string> GetProbeDirectories(string? sourcePath)
    {
        var probeDirectories = new List<string>();
        if (!string.IsNullOrEmpty(sourcePath))
        {
            string cleanSource = StripArchiveFragment(sourcePath);
            string? sourceDir = Path.GetDirectoryName(cleanSource);
            if (!string.IsNullOrEmpty(sourceDir))
            {
                AddDirectoryWithParents(probeDirectories, sourceDir);
            }
        }

        if (!string.IsNullOrEmpty(FileExplorer.Directory))
        {
            AddDirectoryWithParents(probeDirectories, FileExplorer.Directory);
        }

        if (ShouldProbeAtlasDirectory(sourcePath, out string? atlasDirectory) && !string.IsNullOrEmpty(atlasDirectory))
        {
            AddDirectoryWithParents(probeDirectories, atlasDirectory);
        }

        return probeDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance method to satisfy StyleCop SA1204 member ordering.")]
    private void AddDirectoryWithParents(List<string> list, string dir)
    {
        if (string.IsNullOrEmpty(dir))
        {
            return;
        }

        list.Add(dir);
        try
        {
            string? tempRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = new DirectoryInfo(dir);
            for (int depth = 0; depth < 5 && current.Parent is not null; depth++)
            {
                current = current.Parent;
                string fullName = current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string root = Path.GetPathRoot(current.FullName)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
                if (string.IsNullOrEmpty(fullName) ||
                    string.Equals(fullName, root, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(tempRoot) && string.Equals(fullName, tempRoot, StringComparison.OrdinalIgnoreCase)))
                {
                    break;
                }

                list.Add(current.FullName);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or SecurityException or NotSupportedException)
        {
            // Ignore invalid paths
        }
    }

    private bool ShouldProbeAtlasDirectory(string? sourcePath, out string? atlasDirectory)
    {
        atlasDirectory = null;
        if (string.IsNullOrEmpty(AtlasPath))
        {
            return false;
        }

        string cleanAtlas = StripArchiveFragment(AtlasPath);
        atlasDirectory = Path.GetDirectoryName(cleanAtlas);

        return !string.IsNullOrEmpty(atlasDirectory) &&
            (string.IsNullOrEmpty(sourcePath) ||
             IsSameDirectory(sourcePath, AtlasPath) ||
             (!string.IsNullOrEmpty(FileExplorer.Directory) &&
              IsUnderDirectory(sourcePath, FileExplorer.Directory) &&
              IsUnderDirectory(AtlasPath, FileExplorer.Directory)));
    }

    private void ReplaceSlices(IEnumerable<MappedImageDefinition> definitions)
    {
        foreach (var slice in Slices)
        {
            slice.PropertyChanged -= OnSlicePropertyChanged;
            DisposeThumbnail(slice.Thumbnail);
        }

        Slices.Clear();
        _undoStack.Clear();
        _redoStack.Clear();
        RefreshEditorCommands();

        foreach (var definition in definitions)
        {
            var slice = new TextureSliceViewModel(definition);
            if (AtlasBitmap is not null)
            {
                slice.UpdateTexture(AtlasFileName, AtlasBitmap.PixelSize.Width, AtlasBitmap.PixelSize.Height);
            }

            slice.UpdateZoom(Zoom);
            TrackSlice(slice);
        }

        SelectedSlice = Slices.FirstOrDefault();
    }

    private void RestoreSlice(TextureSliceViewModel slice, int index)
    {
        if (index >= 0 && index <= Slices.Count)
        {
            Slices.Insert(index, slice);
        }
        else
        {
            Slices.Add(slice);
        }

        slice.PropertyChanged += OnSlicePropertyChanged;
        slice.Thumbnail = CreateThumbnail(slice);
    }

    private void TrackSlice(TextureSliceViewModel slice)
    {
        slice.PropertyChanged += OnSlicePropertyChanged;
        slice.Thumbnail = CreateThumbnail(slice);
        Slices.Add(slice);
    }

    private void UntrackSlice(TextureSliceViewModel slice)
    {
        slice.PropertyChanged -= OnSlicePropertyChanged;
        DisposeThumbnail(slice.Thumbnail);
        slice.Thumbnail = null;
        Slices.Remove(slice);
        if (ReferenceEquals(SelectedSlice, slice))
        {
            SelectedSlice = Slices.FirstOrDefault();
        }
    }

    private void DeleteSlice(TextureSliceViewModel slice) => UntrackSlice(slice);

    private void PasteInternalCopiedSlice()
    {
        var copy = _copiedSlice;
        if (_isCutOperation)
        {
            _copiedSlice = null;
            _isCutOperation = false;
        }

        if (copy is null)
        {
            return;
        }

        var pasted = InsertSliceCopy(copy);
        if (pasted is not null)
        {
            PushUndo(new TextureEditAction(
                Localize("TextureEditor.History.PasteSlice", "Paste slice"),
                () =>
                {
                    if (!Slices.Contains(pasted))
                    {
                        TrackSlice(pasted);
                    }

                    SelectedSlice = pasted;
                    MarkDirty();
                },
                () =>
                {
                    UntrackSlice(pasted);
                    MarkDirty();
                }));
        }

        MarkDirty();
        RefreshEditorCommands();
    }

    private async Task PasteFromClipboardAsync(CancellationToken cancellationToken)
    {
        var topLevel = GetTopLevel();
        if (topLevel?.Clipboard is not { } clipboard)
        {
            return;
        }

        try
        {
            var text = await clipboard.GetTextAsync().ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var parsed = parser.ParseText(text);
            if (!parsed.Success || parsed.Data is null || parsed.Data.Count == 0)
            {
                Notifications.ShowWarning(
                    Localize("TextureEditor.Notify.PasteFailed.Title", "Paste failed"),
                    Localize("TextureEditor.Notify.PasteFailed.Message", "Clipboard does not contain valid MappedImage definitions."),
                    NotificationDurations.Medium);
                return;
            }

            var added = parsed.Data
                .Select(InsertSliceCopy)
                .OfType<TextureSliceViewModel>()
                .ToList();

            if (added.Count > 0)
            {
                PushUndo(new TextureEditAction(
                    Localize("TextureEditor.History.PasteSlice", "Paste slice"),
                    () =>
                    {
                        foreach (var s in added.Where(s => !Slices.Contains(s)))
                        {
                            TrackSlice(s);
                        }

                        SelectedSlice = added[^1];
                        MarkDirty();
                    },
                    () =>
                    {
                        foreach (var s in added)
                        {
                            UntrackSlice(s);
                        }

                        MarkDirty();
                    }));
                MarkDirty();
                RefreshEditorCommands();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Failed to read clipboard text for paste");
        }
    }

    private TextureSliceViewModel? InsertSliceCopy(MappedImageDefinition source)
    {
        if (AtlasBitmap is null)
        {
            return null;
        }

        int width = source.Right - source.Left;
        int height = source.Bottom - source.Top;
        int left = Math.Clamp(source.Left + TextureEditorConstants.PasteOffset, 0, Math.Max(0, AtlasBitmap.PixelSize.Width - width));
        int top = Math.Clamp(source.Top + TextureEditorConstants.PasteOffset, 0, Math.Max(0, AtlasBitmap.PixelSize.Height - height));
        var slice = new TextureSliceViewModel(new MappedImageDefinition(
            UniqueSliceName(source.Name + TextureEditorConstants.DuplicateNameSuffix),
            AtlasFileName,
            AtlasBitmap.PixelSize.Width,
            AtlasBitmap.PixelSize.Height,
            left,
            top,
            left + width,
            top + height));
        slice.UpdateZoom(Zoom);
        TrackSlice(slice);
        SelectedSlice = slice;
        return slice;
    }

    private async Task CopyTextToClipboardAsync(string text)
    {
        try
        {
            var topLevel = GetTopLevel();
            if (topLevel?.Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to copy MappedImage text to clipboard");
        }
    }

    private string UniqueSliceName(string baseName)
    {
        string candidate = baseName;
        int counter = 2;
        while (Slices.Any(slice => slice.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
        {
            candidate = $"{baseName}{counter}";
            counter++;
        }

        return candidate;
    }

    private void ClearAtlas()
    {
        EndSliceDrag();
        foreach (var slice in Slices)
        {
            slice.PropertyChanged -= OnSlicePropertyChanged;
            DisposeThumbnail(slice.Thumbnail);
        }

        Slices.Clear();
        SelectedSlice = null;
        AtlasBitmap?.Dispose();
        AtlasBitmap = null;
        _atlasDecoded = null;
        AtlasPath = string.Empty;
        IsPlaceholder = false;
        _savedIniPath = null;
        _copiedSlice = null;
        _isCutOperation = false;
        _undoStack.Clear();
        _redoStack.Clear();
        RefreshRegistryImages();
    }

    private async Task<bool> TryWriteMappedImagesAsync(string path, CancellationToken cancellationToken)
    {
        if (!ValidateSlicesForExport())
        {
            return false;
        }

        var definitions = Slices.Select(slice => slice.ToDefinition()).ToList();
        var (success, preserved) = await TryGetPreservedDefinitionsAsync(path, definitions, cancellationToken).ConfigureAwait(true);
        if (!success)
        {
            return false;
        }

        if (definitions.Count == 0 && !await ConfirmEmptyOverwriteAsync(path, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        string content = parser.Serialize(preserved.Concat(definitions), $"Generated by GenHub {TextureEditorConstants.ToolName} from {AtlasFileName}");
        await AtomicFile.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(true);
        return true;
    }

    private bool ValidateSlicesForExport()
    {
        var invalid = Slices.FirstOrDefault(slice => !slice.IsWithinTexture);
        if (invalid is not null)
        {
            logger.LogWarning("INI save aborted: slice {Slice} is outside the texture bounds", invalid.Name);
            Notifications.ShowError(
                Localize(ExportInvalidTitleKey, ExportInvalidTitleFallback),
                Localize("TextureEditor.Notify.ExportInvalid.Message", "Slice '{0}' extends outside the texture bounds.", invalid.Name),
                NotificationDurations.Long);
            return false;
        }

        if (Slices.Any(slice => string.IsNullOrWhiteSpace(slice.Name)))
        {
            logger.LogWarning("INI save aborted: a slice has an empty name");
            Notifications.ShowError(
                Localize(ExportInvalidTitleKey, ExportInvalidTitleFallback),
                Localize("TextureEditor.Notify.ExportInvalidBlankName.Message", "A slice has an empty name. Name every slice before exporting."),
                NotificationDurations.Long);
            return false;
        }

        var duplicate = Slices
            .GroupBy(slice => slice.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1)
            ?.First();
        if (duplicate is not null)
        {
            logger.LogWarning("INI save aborted: slice name {Slice} is duplicated", duplicate.Name);
            Notifications.ShowError(
                Localize(ExportInvalidTitleKey, ExportInvalidTitleFallback),
                Localize("TextureEditor.Notify.ExportInvalidDuplicateName.Message", "Slice name '{0}' is duplicated. Slice names must be unique.", duplicate.Name),
                NotificationDurations.Long);
            return false;
        }

        return true;
    }

    private async Task<(bool Success, List<MappedImageDefinition> Preserved)> TryGetPreservedDefinitionsAsync(
        string path,
        IReadOnlyList<MappedImageDefinition> definitions,
        CancellationToken cancellationToken)
    {
        var preserved = new List<MappedImageDefinition>();
        if (!File.Exists(path))
        {
            return (true, preserved);
        }

        var existing = await parser.ParseFileAsync(path, cancellationToken).ConfigureAwait(true);
        if (existing.Failed || existing.Data is null)
        {
            Notifications.ShowError(
                Localize(ExportInvalidTitleKey, ExportInvalidTitleFallback),
                Localize("TextureEditor.Notify.ExportInspectFailed.Message", "Could not inspect '{0}'. The export was cancelled to protect its contents.", Path.GetFileName(path)),
                NotificationDurations.Long);
            return (false, preserved);
        }

        preserved.AddRange(existing.Data.Where(image => !MappedImageTextureMatcher.Matches(image.TextureFileName, AtlasFileName)));

        var colliding = definitions.FirstOrDefault(d => preserved.Any(p => string.Equals(p.Name, d.Name, StringComparison.OrdinalIgnoreCase)));
        if (colliding is not null)
        {
            logger.LogWarning("INI save aborted: slice name {Slice} collides with preserved entry in existing INI", colliding.Name);
            Notifications.ShowError(
                Localize(ExportInvalidTitleKey, ExportInvalidTitleFallback),
                Localize("TextureEditor.Notify.ExportInvalidDuplicateName.Message", "Slice name '{0}' is duplicated. Slice names must be unique.", colliding.Name),
                NotificationDurations.Long);
            return (false, preserved);
        }

        return (true, preserved);
    }

    private async Task<bool> ConfirmEmptyOverwriteAsync(string path, CancellationToken cancellationToken)
    {
        bool hasContent = false;
        try
        {
            hasContent = File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fail closed: without a successful content check the save cannot
            // proceed, otherwise an empty export could silently discard mappings.
            logger.LogWarning(ex, "Unable to inspect existing INI {Path} before an empty save.", path);
            Notifications.ShowWarning(
                Localize(ExportInvalidTitleKey, ExportInvalidTitleFallback),
                Localize("TextureEditor.Notify.ExportInspectFailed.Message", "Could not inspect '{0}'. The export was cancelled to protect its contents.", Path.GetFileName(path)),
                NotificationDurations.Long);
            return false;
        }

        if (!hasContent)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await Dialogs.ShowConfirmationAsync(
            Localize("TextureEditor.Dialog.ClearSlices.Title", "Replace slices with an empty set?"),
            Localize("TextureEditor.Dialog.ClearSlices.Message", "{0} already contains mapped images. Save zero slices anyway?", Path.GetFileName(path)),
            Localize("TextureEditor.Dialog.ClearSlices.Confirm", "Save empty"),
            Localize("TextureEditor.Dialog.ClearSlices.Cancel", "Cancel")).ConfigureAwait(true);
    }

    private async Task<bool> WritePackOutputsAsync(TextureAtlasBuildRequest request, byte[] textureBytes, string iniContent, CancellationToken cancellationToken)
    {
        var (previousTexture, hadTexture) = await ReadExistingFileBytesAsync(request.TargetTexture, cancellationToken).ConfigureAwait(true);
        await AtomicFile.WriteAllBytesAsync(request.TargetTexture, textureBytes, cancellationToken).ConfigureAwait(true);
        try
        {
            await AtomicFile.WriteAllTextAsync(request.TargetIni, iniContent, cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            await RestorePackTextureAsync(request.TargetTexture, previousTexture, hadTexture).ConfigureAwait(true);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Auto-pack INI write failed; restoring the previous atlas texture.");
            await RestorePackTextureAsync(request.TargetTexture, previousTexture, hadTexture).ConfigureAwait(true);
            Notifications.ShowError(
                Localize("TextureEditor.Notify.PackFailed.Title", PackFailedTitleFallback),
                ex.Message,
                NotificationDurations.Long);
            return false;
        }

        return true;
    }

    private async Task<(byte[]? Bytes, bool Existed)> ReadExistingFileBytesAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return (null, false);
        }

        try
        {
            return (await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(true), true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to back up existing atlas texture {Path} before auto-pack.", path);
            return (null, true);
        }
    }

    private async Task RestorePackTextureAsync(string path, byte[]? previousTexture, bool hadTexture)
    {
        // Best effort on a detached path: the failure result is already decided,
        // so the restore must neither throw nor honor the cancelled operation token.
        try
        {
            if (!hadTexture)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                return;
            }

            if (previousTexture is null)
            {
                // The backup failed while the file existed: keep the new output
                // rather than deleting a texture that cannot be restored.
                logger.LogWarning("Atlas texture {Path} was overwritten without a backup; keeping the new output.", path);
                return;
            }

            await AtomicFile.WriteAllBytesAsync(path, previousTexture).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to restore atlas texture {Path} after a failed auto-pack.", path);
        }
    }

    private async Task<bool> ConfirmOverwriteAsync(TextureAtlasBuildRequest request, CancellationToken cancellationToken)
    {
        var existing = new List<string>();
        if (File.Exists(request.TargetTexture))
        {
            existing.Add(Path.GetFileName(request.TargetTexture));
        }

        if (File.Exists(request.TargetIni))
        {
            existing.Add(Path.GetFileName(request.TargetIni));
        }

        if (existing.Count == 0)
        {
            return true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await Dialogs.ShowConfirmationAsync(
            Localize("TextureEditor.Dialog.Overwrite.Title", "Overwrite packed atlas?"),
            Localize("TextureEditor.Dialog.Overwrite.Message", "{0} already exist. Overwrite them?", string.Join(", ", existing)),
            Localize("TextureEditor.Dialog.Overwrite.Confirm", "Overwrite"),
            Localize("TextureEditor.Dialog.Overwrite.Cancel", "Cancel")).ConfigureAwait(true);
    }

    private void OnSlicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not TextureSliceViewModel slice)
        {
            return;
        }

        if (e.PropertyName is nameof(TextureSliceViewModel.Left) or nameof(TextureSliceViewModel.Top) or nameof(TextureSliceViewModel.Right) or nameof(TextureSliceViewModel.Bottom) or nameof(TextureSliceViewModel.Name))
        {
            DisposeThumbnail(slice.Thumbnail);
            slice.Thumbnail = CreateThumbnail(slice);
            MarkDirty();
        }
    }

    private Avalonia.Media.IImage? CreatePickerThumbnail(MappedImageDefinition definition)
    {
        if (AtlasBitmap is null || IsPlaceholder || !MappedImageTextureMatcher.Matches(definition.TextureFileName, AtlasFileName))
        {
            return null;
        }

        var slice = new TextureSliceViewModel(definition);
        slice.UpdateZoom(1.0);
        return CreateThumbnail(slice);
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasBitmap instance state.")]
    private Avalonia.Media.IImage? CreateThumbnail(TextureSliceViewModel slice)
    {
        if (AtlasBitmap is null)
        {
            return null;
        }

        int left = Math.Clamp(slice.Left, 0, AtlasBitmap.PixelSize.Width);
        int top = Math.Clamp(slice.Top, 0, AtlasBitmap.PixelSize.Height);
        int right = Math.Clamp(slice.Right, left, AtlasBitmap.PixelSize.Width);
        int bottom = Math.Clamp(slice.Bottom, top, AtlasBitmap.PixelSize.Height);
        if (right <= left || bottom <= top)
        {
            return null;
        }

        return new CroppedBitmap(AtlasBitmap, new PixelRect(left, top, right - left, bottom - top));
    }
}
