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
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Features.Tools.TextureEditor.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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
    IDialogService dialogService)
    : EditorToolViewModelBase(notificationService, localizationService, dialogService)
{
    private const string SavedMessageFallback = "Saved {0}.";
    private const string PackFailedTitleFallback = "Auto-pack failed";

    private DecodedTexture? _atlasDecoded;
    private FileExplorerViewModel? _fileExplorer;
    private MappedImageDefinition? _copiedSlice;
    private bool _isCutOperation;
    private string? _savedIniPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAtlas))]
    [NotifyPropertyChangedFor(nameof(AtlasDimensions))]
    [NotifyPropertyChangedFor(nameof(AtlasPixelWidth))]
    [NotifyPropertyChangedFor(nameof(AtlasPixelHeight))]
    private Bitmap? _atlasBitmap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AtlasFileName))]
    [NotifyPropertyChangedFor(nameof(DocumentTitle))]
    private string _atlasPath = string.Empty;

    [ObservableProperty]
    private TextureSliceViewModel? _selectedSlice;

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
    public string AtlasFileName => Path.GetFileName(AtlasPath);

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
    /// Gets a value indicating whether the selected slice can be copied.
    /// </summary>
    public override bool CanCopy => SelectedSlice is not null;

    /// <summary>
    /// Gets a value indicating whether the selected slice can be cut.
    /// </summary>
    public override bool CanCut => SelectedSlice is not null;

    /// <summary>
    /// Gets a value indicating whether a copied slice can be pasted.
    /// </summary>
    public override bool CanPaste => HasAtlas && _copiedSlice is not null;

    /// <summary>
    /// Gets a value indicating whether the selected slice can be duplicated.
    /// </summary>
    public override bool CanDuplicate => SelectedSlice is not null;

    /// <summary>
    /// Gets a value indicating whether the selected slice can be deleted.
    /// </summary>
    public override bool CanDelete => SelectedSlice is not null;

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated AtlasPath instance state.")]
    private string DefaultIniPath => Path.Combine(
        Path.GetDirectoryName(AtlasPath) ?? string.Empty,
        Path.GetFileNameWithoutExtension(AtlasPath) + TextureEditorConstants.MappedImagesExtension);

    /// <summary>
    /// Loads a registry entry into the slice list for editing.
    /// </summary>
    /// <param name="definition">The mapped image definition.</param>
    public void LoadRegistryEntry(MappedImageDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (AtlasBitmap is null)
        {
            return;
        }

        if (!definition.TextureFileName.Equals(AtlasFileName, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning(
                "Registry entry {Name} targets {Expected} but the open atlas is {Actual}; not loading.",
                definition.Name,
                definition.TextureFileName,
                AtlasFileName);
            Notifications.ShowWarning(
                Localize("TextureEditor.Notify.RegistryMismatch.Title", "Different texture"),
                Localize("TextureEditor.Notify.RegistryMismatch.Message", "'{0}' belongs to {1}, not to the open atlas.", definition.Name, definition.TextureFileName),
                NotificationDurations.Medium);
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
    protected override void OnCopy()
    {
        if (IsTextInputFocused() || SelectedSlice is null)
        {
            return;
        }

        _copiedSlice = SelectedSlice.ToDefinition();
        _isCutOperation = false;
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override void OnCut()
    {
        if (IsTextInputFocused() || SelectedSlice is null)
        {
            return;
        }

        _copiedSlice = SelectedSlice.ToDefinition();
        _isCutOperation = true;
        DeleteSlice(SelectedSlice);
        MarkDirty();
        RefreshEditorCommands();
    }

    /// <inheritdoc />
    protected override Task OnPasteAsync(CancellationToken cancellationToken)
    {
        if (IsTextInputFocused() || _copiedSlice is null || AtlasBitmap is null)
        {
            return Task.CompletedTask;
        }

        InsertSliceCopy(_copiedSlice);
        if (_isCutOperation)
        {
            _copiedSlice = null;
            _isCutOperation = false;
        }

        MarkDirty();
        RefreshEditorCommands();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    protected override void OnDuplicate()
    {
        if (IsTextInputFocused() || SelectedSlice is null || AtlasBitmap is null)
        {
            return;
        }

        InsertSliceCopy(SelectedSlice.ToDefinition());
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

        DeleteSlice(SelectedSlice);
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
        if (!string.IsNullOrEmpty(folder))
        {
            FileExplorer.Directory = folder;
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
            Title = Localize("TextureEditor.Dialog.OpenAtlas", "Open texture atlas"),
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Localize("TextureEditor.Dialog.TextureFiles", "Texture files"))
                {
                    Patterns = TextureEditorConstants.TextureExtensions.Select(extension => "*" + extension).ToArray(),
                },
            ],
        }).ConfigureAwait(true);

        if (files.Count == 0)
        {
            return;
        }

        await LoadAtlasAsync(files[0].Path.LocalPath, cancellationToken).ConfigureAwait(true);
    }

    /// <inheritdoc />
    protected override async Task OnSaveAsync(CancellationToken cancellationToken)
    {
        if (AtlasBitmap is null || string.IsNullOrEmpty(AtlasPath))
        {
            return;
        }

        string path = _savedIniPath ?? DefaultIniPath;
        var operation = BeginOperation();
        try
        {
            if (await TryWriteMappedImagesAsync(path, cancellationToken).ConfigureAwait(true))
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
            if (await TryWriteMappedImagesAsync(path, cancellationToken).ConfigureAwait(true))
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
        if (!disposing)
        {
            return;
        }

        foreach (var slice in Slices)
        {
            slice.PropertyChanged -= OnSlicePropertyChanged;
            DisposeThumbnail(slice.Thumbnail);
        }

        Slices.Clear();
        AtlasBitmap?.Dispose();
        AtlasBitmap = null;
        base.Dispose(disposing);
    }

    private static void DisposeThumbnail(Avalonia.Media.IImage? thumbnail)
    {
        // Never dispose CroppedBitmap: disposing a crop also disposes its Source,
        // which would kill the shared AtlasBitmap all thumbnails are cut from.
        if (thumbnail is IDisposable disposable && thumbnail is not CroppedBitmap)
        {
            disposable.Dispose();
        }
    }

    partial void OnAtlasBitmapChanged(Bitmap? value)
    {
        HasDocument = value is not null;
        AddSliceCommand.NotifyCanExecuteChanged();
        ExportIniCommand.NotifyCanExecuteChanged();
        ExportSheetCommand.NotifyCanExecuteChanged();
        RefreshEditorCommands();
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

        string directory = folders[0].Path.LocalPath;
        try
        {
            await RunOperationAsync(async operationToken =>
            {
                var result = await registry.ScanDirectoryAsync(directory, operationToken).ConfigureAwait(true);
                operationToken.ThrowIfCancellationRequested();
                RefreshRegistryImages();

                if (result.Success && result.Data is not null)
                {
                    Notifications.ShowSuccess(
                        Localize("TextureEditor.Notify.ScanComplete.Title", "Scan complete"),
                        Localize("TextureEditor.Notify.ScanComplete.Message", "Indexed {0} mapped images from {1} files.", result.Data.ImagesIndexed, result.Data.FilesScanned),
                        NotificationDurations.Medium);
                    if (Slices.Count == 0)
                    {
                        LoadSlicesForAtlas();
                        MarkSaved();
                    }
                }
                else
                {
                    Notifications.ShowError(
                        Localize("TextureEditor.Notify.ScanFailed.Title", "Scan failed"),
                        result.FirstError ?? Localize("TextureEditor.Notify.ScanFailed.Message", "Failed to scan MappedImages folder."),
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
            logger.LogError(ex, "Registry scan failed");
            Notifications.ShowError(
                Localize("TextureEditor.Notify.ScanFailed.Title", "Scan failed"),
                ex.Message,
                NotificationDurations.Long);
        }
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
            SuggestedFileName = Path.GetFileNameWithoutExtension(AtlasPath) + TextureEditorConstants.MappedImagesExtension,
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
            SuggestedFileName = Path.GetFileNameWithoutExtension(AtlasPath),
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

        SelectedSlice.Right = SelectedSlice.Left + width;
        SelectedSlice.Bottom = SelectedSlice.Top + height;
    }

    private FileExplorerViewModel CreateFileExplorer()
    {
        var explorer = new FileExplorerViewModel(logger);
        explorer.FilePatterns = TextureEditorConstants.ExplorerFilePatterns;
        explorer.ShowFileExtensions = true;
        explorer.ExcludedDirectoryNames = [ModBuilderConstants.DefaultBuildDir, ModBuilderConstants.DefaultReleaseDir];
        explorer.BrowseFolderAsync = BrowseExplorerFolderAsync;
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
            _ = ImportIniFileAsync(node.FullPath);
            return;
        }

        if (TextureEditorConstants.TextureExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            _ = LoadAtlasAsync(node.FullPath, CancellationToken.None);
        }
    }

    private async Task LoadAtlasAsync(string path, CancellationToken cancellationToken)
    {
        if (!await ConfirmDiscardUnsavedAsync(cancellationToken).ConfigureAwait(true))
        {
            return;
        }

        try
        {
            await RunOperationAsync(async operationToken =>
            {
                var decoded = await bitmapService.LoadDecodedAsync(path, operationToken).ConfigureAwait(true);
                operationToken.ThrowIfCancellationRequested();
                if (decoded.Failed || decoded.Data is null)
                {
                    Notifications.ShowError(
                        Localize("TextureEditor.Notify.OpenFailed.Title", "Failed to open atlas"),
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
                Localize("TextureEditor.Notify.OpenFailed.Title", "Failed to open atlas"),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private async Task ImportSiblingIniAsync(string atlasPath, CancellationToken cancellationToken)
    {
        string sibling = Path.Combine(
            Path.GetDirectoryName(atlasPath) ?? string.Empty,
            Path.GetFileNameWithoutExtension(atlasPath) + TextureEditorConstants.MappedImagesExtension);
        if (!File.Exists(sibling))
        {
            return;
        }

        var parsed = await parser.ParseFileAsync(sibling, cancellationToken).ConfigureAwait(true);
        if (parsed.Data is null || parsed.Data.Count == 0)
        {
            logger.LogWarning("Sibling INI {Path} holds no mapped images: {Error}", sibling, parsed.FirstError ?? "unknown");
            Notifications.ShowWarning(
                Localize("TextureEditor.Notify.ImportFailed.Title", "Import failed"),
                Localize("TextureEditor.Notify.ImportFailed.Message", "No mapped images found."),
                NotificationDurations.Medium);
            return;
        }

        registry.ImportDefinitions(parsed.Data);
        RefreshRegistryImages();
    }

    private async Task ImportIniFileAsync(string path)
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
                        Localize("TextureEditor.Notify.ImportFailed.Title", "Import failed"),
                        parsed.FirstError ?? Localize("TextureEditor.Notify.ImportFailed.Message", "No mapped images found."),
                        NotificationDurations.Long);
                    return;
                }

                registry.ImportDefinitions(parsed.Data);
                RefreshRegistryImages();
                if (AtlasBitmap is not null && Slices.Count == 0)
                {
                    LoadSlicesForAtlas();
                    MarkSaved();
                }

                FileExplorer.CurrentPath = path;
                Notifications.ShowSuccess(
                    Localize("TextureEditor.Notify.ImportComplete.Title", "Import complete"),
                    Localize("TextureEditor.Notify.ImportComplete.Message", "Imported {0} mapped images from {1}.", parsed.Data.Count, Path.GetFileName(path)),
                    NotificationDurations.Medium);
            }).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation from the busy overlay is silent by design.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "INI import failed for {Path}", path);
            Notifications.ShowError(
                Localize("TextureEditor.Notify.ImportFailed.Title", "Import failed"),
                ex.Message,
                NotificationDurations.Long);
        }
    }

    private void RefreshRegistryImages()
    {
        // Replace the instance so bound pickers rebuild once for the whole
        // catalog instead of once per entry.
        RegistryImages = new ObservableCollection<MappedImageDefinition>(registry.All);
        OnPropertyChanged(nameof(RegistryImages));
    }

    private bool OpenDecodedAtlas(DecodedTexture decoded, string path)
    {
        var bitmap = bitmapService.ToBitmap(decoded);
        if (bitmap.Failed || bitmap.Data is null)
        {
            Notifications.ShowError(
                Localize("TextureEditor.Notify.OpenFailed.Title", "Failed to open atlas"),
                bitmap.FirstError ?? string.Empty,
                NotificationDurations.Long);
            return false;
        }

        ClearAtlas();
        _atlasDecoded = decoded;
        AtlasPath = path;
        AtlasBitmap = bitmap.Data;
        RefreshRegistryImages();
        AddSliceCommand.NotifyCanExecuteChanged();
        ExportIniCommand.NotifyCanExecuteChanged();
        ExportSheetCommand.NotifyCanExecuteChanged();
        return true;
    }

    private void LoadSlicesForAtlas()
    {
        if (AtlasBitmap is null)
        {
            return;
        }

        var matches = registry.GetByTexture(AtlasFileName);
        if (matches.Count == 0)
        {
            return;
        }

        ReplaceSlices(matches);
    }

    private void ReplaceSlices(IEnumerable<MappedImageDefinition> definitions)
    {
        foreach (var slice in Slices)
        {
            slice.PropertyChanged -= OnSlicePropertyChanged;
            DisposeThumbnail(slice.Thumbnail);
        }

        Slices.Clear();
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

    private void TrackSlice(TextureSliceViewModel slice)
    {
        slice.PropertyChanged += OnSlicePropertyChanged;
        slice.Thumbnail = CreateThumbnail(slice);
        Slices.Add(slice);
    }

    private void DeleteSlice(TextureSliceViewModel slice)
    {
        slice.PropertyChanged -= OnSlicePropertyChanged;
        DisposeThumbnail(slice.Thumbnail);
        Slices.Remove(slice);
        if (ReferenceEquals(SelectedSlice, slice))
        {
            SelectedSlice = Slices.FirstOrDefault();
        }
    }

    private void InsertSliceCopy(MappedImageDefinition source)
    {
        if (AtlasBitmap is null)
        {
            return;
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
        _savedIniPath = null;
        _copiedSlice = null;
        _isCutOperation = false;
        RefreshRegistryImages();
    }

    private async Task<bool> TryWriteMappedImagesAsync(string path, CancellationToken cancellationToken)
    {
        var invalid = Slices.FirstOrDefault(slice => !slice.IsWithinTexture);
        if (invalid is not null)
        {
            logger.LogWarning("INI save aborted: slice {Slice} is outside the texture bounds", invalid.Name);
            Notifications.ShowError(
                Localize("TextureEditor.Notify.ExportInvalid.Title", "Cannot export slices"),
                Localize("TextureEditor.Notify.ExportInvalid.Message", "Slice '{0}' extends outside the texture bounds.", invalid.Name),
                NotificationDurations.Long);
            return false;
        }

        var badName = Slices.FirstOrDefault(slice => string.IsNullOrWhiteSpace(slice.Name))
            ?? Slices.GroupBy(slice => slice.Name, StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => group.First())
                .FirstOrDefault();
        if (badName is not null)
        {
            logger.LogWarning("INI save aborted: slice name '{Slice}' is empty or duplicated", badName.Name);
            Notifications.ShowError(
                Localize("TextureEditor.Notify.ExportInvalid.Title", "Cannot export slices"),
                Localize("TextureEditor.Notify.InvalidName.Message", "Slice name '{0}' is empty or duplicated.", badName.Name),
                NotificationDurations.Long);
            return false;
        }

        var definitions = Slices.Select(slice => slice.ToDefinition()).ToList();
        if (definitions.Count == 0 && !await ConfirmEmptyOverwriteAsync(path, cancellationToken).ConfigureAwait(true))
        {
            return false;
        }

        string content = parser.Serialize(definitions, $"Generated by GenHub {TextureEditorConstants.ToolName} from {AtlasFileName}");
        await AtomicFile.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(true);
        return true;
    }

    private async Task<bool> ConfirmEmptyOverwriteAsync(string path, CancellationToken cancellationToken)
    {
        bool hasContent;
        try
        {
            hasContent = File.Exists(path) && new FileInfo(path).Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Unable to inspect existing INI {Path} before an empty save.", path);
            return true;
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
        var (previousTexture, hadTexture) = await ReadExistingFileBytesAsync(request.TargetTexture).ConfigureAwait(true);
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

    private async Task<(byte[]? Bytes, bool Existed)> ReadExistingFileBytesAsync(string path)
    {
        if (!File.Exists(path))
        {
            return (null, false);
        }

        try
        {
            return (await File.ReadAllBytesAsync(path).ConfigureAwait(true), true);
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
        if (AtlasBitmap is null || !definition.TextureFileName.Equals(AtlasFileName, StringComparison.OrdinalIgnoreCase))
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
