using Avalonia.Controls;
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
using GenHub.Core.Interfaces.Tools.IniEditor;
using GenHub.Core.Interfaces.Tools.ModelViewer;
using GenHub.Core.Interfaces.Tools.TextureEditor;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.IniEditor;
using GenHub.Core.Models.Tools.ModelViewer;
using GenHub.Core.Models.Tools.TextureEditor;
using GenHub.Core.Services.Tools.ModelViewer;
using GenHub.Features.Tools.ModBuilder.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Numerics;
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
    IW3dModelResolver modelResolver,
    INotificationService notificationService,
    ILocalizationService localizationService,
    IDialogService dialogService,
    ILogger<IniEditorViewModel> logger)
    : EditorToolViewModelBase(notificationService, localizationService, dialogService)
{
    private const string ImmortalMarker = "Immortal";
    private const string AccentBrushKey = ThemeResourceKeys.AccentBrush;
    private const string TextPrimaryBrushKey = ThemeResourceKeys.TextPrimary;
    private const string TextSecondaryBrushKey = ThemeResourceKeys.TextSecondary;
    private const string SuccessBrushKey = ThemeResourceKeys.SuccessBrush;
    private const string WarningBrushKey = ThemeResourceKeys.WarningBrush;

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
    private readonly List<IniTextureItemViewModel> _allTextureItems = [];
    private FileExplorerViewModel? _fileExplorer;
    private IniDocument? _document;
    private bool _cultureSubscribed;
    private bool _installationsLoaded;
    private int _thumbnailGeneration;
    private IniEditAction? _savedTopAction;
    private bool _isRebuilding;
    private bool _isSyncingSelection;
    private IniBlock? _lastEditableBlock;
    private CancellationTokenSource? _previewCts;
    private CancellationTokenSource? _modelPreviewCts;
    private int _modelPreviewGeneration;
    private DispatcherTimer? _previewPlaybackTimer;
    private string? _lastPreviewModel;
    private string? _lastPreviewErrorToast;
    private string? _previewStatusKey;
    private object[] _previewStatusArgs = [];
    private W3dResolvedModel? _previewResolved;
    private bool _isSyncingPreviewSelection;
    private CancellationTokenSource? _filterCts;
    private CancellationTokenSource? _thumbnailCts;
    private CancellationTokenSource? _rawEditCts;
    private bool _isUpdatingRawPreview;
    private int _documentRevision;
    private IniDocument? _suggestionIndexDocument;
    private int _suggestionIndexRevision = -1;
    private Dictionary<string, IReadOnlyList<string>> _suggestionFieldValues = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, HashSet<string>> _suggestionBlockNames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the root block nodes of the edited document.
    /// </summary>
    public ObservableCollection<IniTreeNodeViewModel> RootNodes { get; } = [];

    /// <summary>
    /// Gets the nodes shown in the block explorer, grouped by type for large documents.
    /// </summary>
    public ObservableCollection<IniTreeNodeViewModel> VisibleRootNodes { get; } = [];

    /// <summary>
    /// Gets the model, texture, and effect assets linked to the selected block.
    /// </summary>
    public ObservableCollection<IniAssetLinkItem> SelectedBlockAssets { get; } = [];

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

    /// <summary>
    /// Gets a value indicating whether the blocks sidebar tab is selected.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated tab index instance state and is bound from XAML.")]
    public bool IsBlocksTabSelected => LeftSidebarTabIndex == 0;

    /// <summary>
    /// Gets a value indicating whether the file explorer sidebar tab is selected.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated tab index instance state and is bound from XAML.")]
    public bool IsFilesTabSelected => LeftSidebarTabIndex == 1;

    /// <summary>
    /// Gets a value indicating whether the reference index sidebar tab is selected.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated tab index instance state and is bound from XAML.")]
    public bool IsReferencesTabSelected => LeftSidebarTabIndex == 2;

    /// <summary>
    /// Gets a value indicating whether the textures sidebar tab is selected.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated tab index instance state and is bound from XAML.")]
    public bool IsTexturesTabSelected => LeftSidebarTabIndex == 3;

    [ObservableProperty]
    private string _textureSearchFilter = string.Empty;

    [ObservableProperty]
    private string? _textureStatusText;

    /// <summary>
    /// Gets the filtered mapped images available for the 2-column texture picker.
    /// </summary>
    public ObservableCollection<IniTextureItemViewModel> FilteredTextureItems { get; } = [];

    /// <summary>
    /// Gets or sets the dynamic vital statistics for the selected block (2-column layout).
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<CanvasVitalItem> _selectedBlockVitals = [];

    /// <summary>
    /// Gets a value indicating whether the selected block has vitals to display.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated vitals instance state and is bound from XAML.")]
    public bool HasSelectedBlockVitals => SelectedBlockVitals?.Count > 0;

    /// <summary>
    /// Gets or sets whether to show all document blocks on the canvas in an overview.
    /// </summary>
    [ObservableProperty]
    private bool _showAllBlocksOnCanvas;

    /// <summary>
    /// Gets the top-level block cards for the multi-object canvas view.
    /// </summary>
    public ObservableCollection<IniCanvasCardViewModel> CanvasBlockCards { get; } = [];

    [ObservableProperty]
    private IniTreeNodeViewModel? _selectedNode;

    /// <summary>
    /// Gets or sets a value indicating whether left-drag pans the canvas instead of interacting with content.
    /// </summary>
    [ObservableProperty]
    private bool _isPanMode;

    /// <summary>
    /// Gets the ancestor chain from the root block to the selected node for breadcrumb navigation.
    /// </summary>
    public ObservableCollection<IniTreeNodeViewModel> SelectedNodeTrail { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the selected node is nested and shows a breadcrumb trail.
    /// </summary>
    [ObservableProperty]
    private bool _hasTrail;

    /// <summary>
    /// Gets the live validation issues for the open document and selected block.
    /// </summary>
    public ObservableCollection<IniValidationRow> ValidationIssues { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether any live validation issues exist.
    /// </summary>
    [ObservableProperty]
    private bool _hasValidationIssues;

    /// <summary>
    /// Gets or sets the total number of live validation issues.
    /// </summary>
    [ObservableProperty]
    private int _validationIssueCount;

    /// <summary>
    /// Gets or sets the block filter text.
    /// </summary>
    [ObservableProperty]
    private string? _blockFilter;

    /// <summary>
    /// Gets or sets a value indicating whether large documents group blocks by type.
    /// </summary>
    [ObservableProperty]
    private bool _isGroupByTypeEnabled = true;

    /// <summary>
    /// Gets or sets the visible block count summary.
    /// </summary>
    [ObservableProperty]
    private string _blockCountText = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the selected block exposes linked assets.
    /// </summary>
    [ObservableProperty]
    private bool _hasSelectedBlockAssets;

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
    /// Gets the list of available field keys for autocomplete.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _availableFieldKeys = [];

    /// <summary>
    /// Gets or sets the raw preview text for two-way block editing.
    /// </summary>
    [ObservableProperty]
    private string _rawPreviewText = string.Empty;

    /// <summary>
    /// Gets or sets the title of the selected block.
    /// </summary>
    [ObservableProperty]
    private string _selectedBlockTitle = string.Empty;

    /// <summary>
    /// Gets or sets the type of the selected block.
    /// </summary>
    [ObservableProperty]
    private string _selectedBlockType = string.Empty;

    /// <summary>
    /// Gets or sets the fallback icon kind for the selected block.
    /// </summary>
    [ObservableProperty]
    private string _selectedBlockIconKind = "CubeOutline";

    /// <summary>
    /// Gets or sets the faction / side of the selected block.
    /// </summary>
    [ObservableProperty]
    private string _selectedBlockSide = string.Empty;

    /// <summary>
    /// Gets or sets the health of the selected block.
    /// </summary>
    [ObservableProperty]
    private string? _selectedBlockHealth;

    /// <summary>
    /// Gets or sets the build cost of the selected block.
    /// </summary>
    [ObservableProperty]
    private string? _selectedBlockCost;

    /// <summary>
    /// Gets or sets the build time of the selected block.
    /// </summary>
    [ObservableProperty]
    private string? _selectedBlockTime;

    /// <summary>
    /// Gets or sets the 3D model asset name of the selected block.
    /// </summary>
    [ObservableProperty]
    private string _selectedBlockModel = string.Empty;

    /// <summary>
    /// Gets or sets the rendered 3D preview scene.
    /// </summary>
    [ObservableProperty]
    private W3dRenderScene? _previewScene;

    /// <summary>
    /// Gets or sets a value indicating whether a 3D preview scene is loaded.
    /// </summary>
    [ObservableProperty]
    private bool _hasPreviewScene;

    /// <summary>
    /// Gets or sets a value indicating whether the 3D preview is resolving.
    /// </summary>
    [ObservableProperty]
    private bool _isPreviewLoading;

    /// <summary>
    /// Gets or sets the 3D preview status text.
    /// </summary>
    [ObservableProperty]
    private string _previewStatusText = string.Empty;

    /// <summary>
    /// Gets or sets the previewed model name.
    /// </summary>
    [ObservableProperty]
    private string _previewModelName = string.Empty;

    /// <summary>
    /// Gets or sets the selected preview mesh index, or -1 for none.
    /// </summary>
    [ObservableProperty]
    private int _previewSelectedMeshIndex = -1;

    /// <summary>
    /// Gets or sets the selected preview mesh item.
    /// </summary>
    [ObservableProperty]
    private W3dPreviewMeshItem? _selectedPreviewMesh;

    /// <summary>
    /// Gets or sets the selected animation clip.
    /// </summary>
    [ObservableProperty]
    private W3dAnimationClip? _selectedPreviewClip;

    /// <summary>
    /// Gets or sets the current preview frame.
    /// </summary>
    [ObservableProperty]
    private double _previewFrame;

    /// <summary>
    /// Gets or sets the preview frame count.
    /// </summary>
    [ObservableProperty]
    private int _previewFrameCount;

    /// <summary>
    /// Gets or sets a value indicating whether preview animation is playing.
    /// </summary>
    [ObservableProperty]
    private bool _isPreviewPlaying;

    /// <summary>
    /// Gets or sets the preview pose transforms, or null for the bind pose.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<Matrix4x4>? _previewPose;

    /// <summary>
    /// Gets or sets the preview bind-pose transforms used to relativize animation.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<Matrix4x4>? _previewBindPose;

    /// <summary>
    /// Gets or sets the mesh names hidden by the selected block draw states.
    /// </summary>
    [ObservableProperty]
    private IReadOnlySet<string>? _previewHiddenMeshNames;

    /// <summary>
    /// Gets or sets a value indicating whether the 3D camera follows the animated model.
    /// </summary>
    [ObservableProperty]
    private bool _previewTrackTarget = true;

    /// <summary>
    /// Gets or sets a value indicating whether the preview skeleton renders.
    /// </summary>
    [ObservableProperty]
    private bool _previewShowSkeleton = true;

    /// <summary>
    /// Gets or sets a value indicating whether the preview renders wireframes.
    /// </summary>
    [ObservableProperty]
    private bool _previewShowWireframe;

    /// <summary>
    /// Gets a value indicating whether the preview has animation clips.
    /// </summary>
    public bool HasPreviewClips => PreviewClips.Count > 0;

    /// <summary>
    /// Gets a value indicating whether draw modules reference the selected preview mesh.
    /// </summary>
    public bool HasPreviewMeshModules => PreviewMeshModules.Count > 0;

    /// <summary>
    /// Gets the preview sub-object rows.
    /// </summary>
    public ObservableCollection<W3dPreviewMeshItem> PreviewMeshes { get; } = [];

    /// <summary>
    /// Gets the preview animation clips.
    /// </summary>
    public ObservableCollection<W3dAnimationClip> PreviewClips { get; } = [];

    /// <summary>
    /// Gets the draw module nodes referencing the selected preview mesh.
    /// </summary>
    public ObservableCollection<IniTreeNodeViewModel> PreviewMeshModules { get; } = [];

    /// <summary>
    /// Gets or sets the portrait image for the selected block.
    /// </summary>
    [ObservableProperty]
    private IImage? _selectedBlockPortrait;

    /// <summary>
    /// Gets the list of KindOf flags for the selected block.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<string> _selectedBlockKindOfList = [];

    /// <summary>
    /// Gets the list of connected modules / sub-blocks for the selected block.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<IniTreeNodeViewModel> _selectedBlockModules = [];

    /// <summary>
    /// Gets a value indicating whether a block is currently selected.
    /// </summary>
    [ObservableProperty]
    private bool _hasSelectedBlockKindOf;

    [ObservableProperty]
    private bool _hasSelectedBlockModules;

    [ObservableProperty]
    private bool _hasSelectedBlock;

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
    public override bool CanCopy => HasDocument && EditableSelectedNode != null;

    /// <inheritdoc />
    public override bool CanCut => HasDocument && EditableSelectedNode != null;

    /// <inheritdoc />
    public override bool CanPaste => HasDocument;

    /// <inheritdoc />
    public override bool CanDuplicate => HasDocument && EditableSelectedNode != null;

    /// <summary>
    /// Gets the health stat label for the canvas summary card.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public string HealthLabel => Localization.GetString("Tools.IniEditor.Canvas.HealthLabel");

    /// <summary>
    /// Gets the cost stat label for the canvas summary card.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public string CostLabel => Localization.GetString("Tools.IniEditor.Canvas.CostLabel");

    /// <summary>
    /// Gets the build time stat label for the canvas summary card.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public string BuildTimeLabel => Localization.GetString("Tools.IniEditor.Canvas.BuildTimeLabel");

    /// <summary>
    /// Gets the kind-of flags header label for the canvas summary card.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public string KindOfLabel => Localization.GetString("Tools.IniEditor.Canvas.KindOfLabel");

    /// <summary>
    /// Gets the modules header label for the canvas summary card.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public string ModulesLabel => Localization.GetString("Tools.IniEditor.Canvas.ModulesLabel");

    /// <inheritdoc />
    public override bool CanDelete => HasDocument && EditableSelectedNode != null;

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
                AsynchronousEnumeration = true,
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
    /// Gets the selected node when it wraps an editable block, or null for group headers.
    /// </summary>
    private IniTreeNodeViewModel? EditableSelectedNode => SelectedNode is { IsGroupHeader: false } ? SelectedNode : null;

    /// <summary>
    /// Attaches the specified texture name to the currently selected block or active field row.
    /// </summary>
    /// <param name="textureName">The mapped image or texture name.</param>
    public void AttachTextureToSelectedBlock(string textureName)
    {
        if (string.IsNullOrWhiteSpace(textureName) || EditableSelectedNode?.Block is null)
        {
            return;
        }

        var block = EditableSelectedNode.Block;
        var expectedPortraitKey = string.Equals(block.BlockType, IniConstants.BlockTypes.CommandButton, StringComparison.OrdinalIgnoreCase)
            ? IniConstants.FieldKeys.ButtonImage
            : IniConstants.FieldKeys.SelectPortrait;
        var existingPortrait = block.Fields.FirstOrDefault(f =>
            string.Equals(f.Key, expectedPortraitKey, StringComparison.OrdinalIgnoreCase));

        if (existingPortrait is not null)
        {
            var oldValue = existingPortrait.Value;
            SetFieldValueByKey(block.Fields, expectedPortraitKey, textureName);

            PushUndo(new IniEditAction(
                Title: Localization.GetString("Tools.IniEditor.History.AttachTexture"),
                Redo: () =>
                {
                    SetFieldValueByKey(block.Fields, expectedPortraitKey, textureName);
                    RebuildAll();
                },
                Undo: () =>
                {
                    SetFieldValueByKey(block.Fields, expectedPortraitKey, oldValue);
                    RebuildAll();
                }));
        }
        else
        {
            var newField = new IniField(expectedPortraitKey, textureName);
            block.Fields.Add(newField);

            PushUndo(new IniEditAction(
                Title: Localization.GetString("Tools.IniEditor.History.AttachTexture"),
                Redo: () =>
                {
                    if (block.Fields.Any(f => string.Equals(f.Key, expectedPortraitKey, StringComparison.OrdinalIgnoreCase)))
                    {
                        SetFieldValueByKey(block.Fields, expectedPortraitKey, textureName);
                    }
                    else
                    {
                        block.Fields.Add(new(expectedPortraitKey, textureName));
                    }

                    RebuildAll();
                },
                Undo: () =>
                {
                    RemoveFieldByKey(block.Fields, expectedPortraitKey);
                    RebuildAll();
                }));
        }

        MarkDirty();
        RebuildFieldRows();
        RebuildCanvasSummary();
        RebuildAssembledRows();
        RebuildVisualObjectCard();
        RefreshRawPreviewText();
        Notifications.ShowSuccess(
            Localization.GetString("Tools.IniEditor.Toast.AttachTextureSuccessTitle"),
            Localization.GetString("Tools.IniEditor.Toast.AttachTextureSuccessMessage", textureName, block.Name));
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
        if (result.Data.HasParseErrors)
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.Open.RecoveredTitle"),
                Localization.GetString(
                    "Tools.IniEditor.Open.RecoveredMessage",
                    result.Data.ParseErrors.Count,
                    result.Data.ParseErrors[0]),
                NotificationDurations.Long);
        }

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

        var first = await FileExplorer.FindFirstFileAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrEmpty(first))
        {
            var opened = await InvokeOnUIThreadAsync(() => OpenFileAsync(first, cancellationToken)).ConfigureAwait(false);
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
        if (IsTextInputFocused() || EditableSelectedNode == null)
        {
            return;
        }

        var text = SerializeBlocks([EditableSelectedNode.Block]);
        _ = CopyTextToClipboardAsync(text);
    }

    /// <inheritdoc />
    protected override void OnCut()
    {
        if (IsTextInputFocused() || _document == null || EditableSelectedNode == null)
        {
            return;
        }

        var text = SerializeBlocks([EditableSelectedNode.Block]);
        _ = CopyTextToClipboardAsync(text);
        DeleteBlock(EditableSelectedNode);
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
        if (IsTextInputFocused() || _document == null || EditableSelectedNode == null)
        {
            return;
        }

        var parsed = iniDocumentService.ParseText(SerializeBlocks([EditableSelectedNode.Block]));
        if (!parsed.Success || parsed.Data == null || parsed.Data.Blocks.Count == 0)
        {
            return;
        }

        var siblings = EditableSelectedNode.Parent == null ? _document.Blocks : EditableSelectedNode.Parent.Block.Children;
        var index = siblings.IndexOf(EditableSelectedNode.Block);
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
        if (IsTextInputFocused() || EditableSelectedNode == null)
        {
            return;
        }

        DeleteBlock(EditableSelectedNode);
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
            CancelDeferred(ref _rawEditCts);
            CancelDeferred(ref _modelPreviewCts);
            StopPreviewPlayback();
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
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
        catch (SecurityException)
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
        catch (ArgumentException)
        {
            return filePath;
        }
        catch (IOException)
        {
            return filePath;
        }
        catch (NotSupportedException)
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

    private static bool IsTextureSuggestionKey(string key, IniFieldSchema? schema)
    {
        return schema?.IsTexture == true ||
            key.Contains("Image", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Portrait", StringComparison.OrdinalIgnoreCase) ||
            key.Contains("Cameo", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveReferenceBlockType(string key, IniFieldSchema? schema)
    {
        if (schema != null)
        {
            return schema.ReferenceBlockType;
        }

        if (string.Equals(key, "CommandButton", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.CommandButton;
        }

        if (string.Equals(key, "CommandSet", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.CommandSet;
        }

        if (key.Contains("Weapon", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.Weapon;
        }

        if (key.Contains("Upgrade", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.Upgrade;
        }

        if (string.Equals(key, IniConstants.FieldKeys.Object, StringComparison.OrdinalIgnoreCase) || string.Equals(key, IniConstants.FieldKeys.TargetObject, StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.Object;
        }

        if (key.Contains("Armor", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.Armor;
        }

        if (key.Contains("DamageFX", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.DamageFX;
        }

        if (key.Contains("Locomotor", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.Locomotor;
        }

        if (key.Contains("SpecialPower", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.SpecialPower;
        }

        if (key.Contains("Science", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.BlockTypes.Science;
        }

        if (key.StartsWith("Voice", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("Sound", StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.SubBlockTypes.AudioEvent;
        }

        return null;
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

    private static async Task RunDeferredRefreshAsync(CancellationTokenSource source, int delayMs, Action<CancellationToken> refresh)
    {
        try
        {
            var token = source.Token;
            await Task.Delay(delayMs, token).ConfigureAwait(false);
            if (token.IsCancellationRequested)
            {
                return;
            }

            PostToUIThread(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    refresh(token);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer edit, or the view model was disposed.
        }
        catch (ObjectDisposedException)
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

    private static void ScheduleDeferred(ref CancellationTokenSource? slot, int delayMs, Action<CancellationToken> refresh)
    {
        slot?.Cancel();
        slot?.Dispose();
        var cts = new CancellationTokenSource();
        slot = cts;
        _ = Task.Run(() => RunDeferredRefreshAsync(cts, delayMs, refresh), CancellationToken.None);
    }

    private static void ScheduleDeferred(ref CancellationTokenSource? slot, int delayMs, Action refresh)
    {
        ScheduleDeferred(ref slot, delayMs, _ => refresh());
    }

    private static string? ResolveHealthValue(IniBlock block)
    {
        var health = FindFieldValue(block, IniConstants.FieldKeys.Health) ??
                     FindFieldValue(block, IniConstants.FieldKeys.MaxHealth) ??
                     FindFieldValue(block, IniConstants.FieldKeys.InitialHealth);
        if (!string.IsNullOrWhiteSpace(health))
        {
            return health;
        }

        foreach (var child in block.Children)
        {
            if (child.BlockType.Contains(IniConstants.FieldKeys.Body, StringComparison.OrdinalIgnoreCase) ||
                child.AssignmentValue?.Contains(IniConstants.FieldKeys.Body, StringComparison.OrdinalIgnoreCase) == true)
            {
                var childHealth = FindFieldValue(child, IniConstants.FieldKeys.MaxHealth) ??
                                  FindFieldValue(child, IniConstants.FieldKeys.InitialHealth);
                if (!string.IsNullOrWhiteSpace(childHealth))
                {
                    return childHealth;
                }

                if (child.BlockType.Contains(ImmortalMarker, StringComparison.OrdinalIgnoreCase) ||
                    child.AssignmentValue?.Contains(ImmortalMarker, StringComparison.OrdinalIgnoreCase) == true)
                {
                    return ImmortalMarker;
                }
            }
        }

        var bodyField = block.Fields.FirstOrDefault(f => string.Equals(f.Key, IniConstants.FieldKeys.Body, StringComparison.OrdinalIgnoreCase));
        if (bodyField != null && bodyField.Value.Contains(ImmortalMarker, StringComparison.OrdinalIgnoreCase))
        {
            return ImmortalMarker;
        }

        return null;
    }

    private static IniTreeNodeViewModel? FindNodeForBlock(IEnumerable<IniTreeNodeViewModel> nodes, IniBlock targetBlock)
    {
        foreach (var node in nodes)
        {
            if (node.Block == targetBlock)
            {
                return node;
            }

            var found = FindNodeForBlock(node.Children, targetBlock);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static void AddVitalIfPresent(List<CanvasVitalItem> vitals, string label, string? value, string brushKey)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            vitals.Add(new(label, value, brushKey));
        }
    }

    private static IniBlock? FindDefaultConditionState(IniBlock block)
    {
        foreach (var child in block.Children)
        {
            if (string.Equals(child.BlockType, "DefaultConditionState", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(child.Name, "DefaultConditionState", StringComparison.OrdinalIgnoreCase))
            {
                return child;
            }

            var nested = FindDefaultConditionState(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private string ResolveBlockModel(IniBlock block)
    {
        return ResolveBlockModelRecursive(block, 0);
    }

    private string ResolveBlockModelRecursive(IniBlock block, int depth)
    {
        var direct = FindFieldValue(block, IniConstants.FieldKeys.Model);
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct.Trim();
        }

        var nested = FindNestedModel(block);
        if (!string.IsNullOrEmpty(nested))
        {
            return nested;
        }

        if (depth > 0)
        {
            return string.Empty;
        }

        // Blocks without their own model, such as command buttons and player
        // templates, preview the model of the object they point at.
        var target = FindFieldValue(block, IniConstants.FieldKeys.Object)
            ?? FindFieldValue(block, IniConstants.FieldKeys.StartingBuilding);
        if (string.IsNullOrWhiteSpace(target))
        {
            return string.Empty;
        }

        var referenced = FindBlocks(IniConstants.BlockTypes.Object, target.Trim()).FirstOrDefault();
        return referenced == null ? string.Empty : ResolveBlockModelRecursive(referenced, depth + 1);
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as an instance helper to satisfy member ordering.")]
    private string FindNestedModel(IniBlock block)
    {
        var queue = new Queue<IniBlock>(block.Children);
        while (queue.Count > 0)
        {
            var child = queue.Dequeue();
            var model = FindFieldValue(child, IniConstants.FieldKeys.Model);
            if (!string.IsNullOrWhiteSpace(model))
            {
                return model.Trim();
            }

            foreach (var grandchild in child.Children)
            {
                queue.Enqueue(grandchild);
            }
        }

        return string.Empty;
    }

    private void RebuildPreviewHiddenMeshes(IniBlock? block)
    {
        if (block == null)
        {
            PreviewHiddenMeshNames = null;
            return;
        }

        var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaultState = FindDefaultConditionState(block);
        if (defaultState != null)
        {
            CollectStateSubObjectVisibility(defaultState, hidden, shown);
        }
        else
        {
            CollectStateSubObjectVisibility(block, hidden, shown);
        }

        hidden.ExceptWith(shown);
        PreviewHiddenMeshNames = hidden.Count > 0 ? hidden : null;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as an instance helper to satisfy member ordering.")]
    private void CollectStateSubObjectVisibility(IniBlock block, HashSet<string> hidden, HashSet<string> shown)
    {
        foreach (var field in block.Fields)
        {
            if (string.Equals(field.Key, IniConstants.FieldKeys.ShowSubObjects, StringComparison.OrdinalIgnoreCase))
            {
                AddSubObjectTokens(shown, field.Value);
            }
            else if (string.Equals(field.Key, IniConstants.FieldKeys.HideSubObjects, StringComparison.OrdinalIgnoreCase))
            {
                AddSubObjectTokens(hidden, field.Value);
            }
        }
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as an instance helper to satisfy member ordering.")]
    private void AddSubObjectTokens(HashSet<string> names, string value)
    {
        foreach (var token in value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            names.Add(token);
        }
    }

    private void AddBlockTypeVitals(IniBlock block, List<CanvasVitalItem> vitals)
    {
        if (string.Equals(block.BlockType, IniConstants.BlockTypes.CommandButton, StringComparison.OrdinalIgnoreCase))
        {
            AddCommandButtonVitals(block, vitals);
        }
        else if (string.Equals(block.BlockType, IniConstants.BlockTypes.Weapon, StringComparison.OrdinalIgnoreCase))
        {
            AddWeaponVitals(block, vitals);
        }
        else if (string.Equals(block.BlockType, IniConstants.BlockTypes.Upgrade, StringComparison.OrdinalIgnoreCase))
        {
            AddUpgradeVitals(block, vitals);
        }
        else
        {
            AddDefaultVitals(block, vitals);
        }
    }

    private void AddCommandButtonVitals(IniBlock block, List<CanvasVitalItem> vitals)
    {
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Command"), FindFieldValue(block, IniConstants.FieldKeys.Command), AccentBrushKey);
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Border"), FindFieldValue(block, IniConstants.FieldKeys.ButtonBorderType), TextSecondaryBrushKey);
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Target"), FindFieldValue(block, IniConstants.FieldKeys.Object) ?? FindFieldValue(block, "Upgrade"), AccentBrushKey);
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Image"), FindFieldValue(block, IniConstants.FieldKeys.ButtonImage), TextPrimaryBrushKey);
    }

    private void AddWeaponVitals(IniBlock block, List<CanvasVitalItem> vitals)
    {
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Damage"), FindFieldValue(block, IniConstants.FieldKeys.PrimaryDamage), AccentBrushKey);
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Range"), FindFieldValue(block, IniConstants.FieldKeys.AttackRange), TextPrimaryBrushKey);
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Type"), FindFieldValue(block, IniConstants.FieldKeys.DamageType), TextSecondaryBrushKey);
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Delay"), FindFieldValue(block, IniConstants.FieldKeys.DelayBetweenShots), TextSecondaryBrushKey);
    }

    private void AddUpgradeVitals(IniBlock block, List<CanvasVitalItem> vitals)
    {
        var cost = FindFieldValue(block, IniConstants.FieldKeys.BuildCost);
        if (!string.IsNullOrWhiteSpace(cost))
        {
            vitals.Add(new(Localization.GetString("Tools.IniEditor.Vitals.Cost"), $"${cost}", AccentBrushKey));
        }

        var time = FindFieldValue(block, IniConstants.FieldKeys.BuildTime);
        if (!string.IsNullOrWhiteSpace(time))
        {
            vitals.Add(new(Localization.GetString("Tools.IniEditor.Vitals.Time"), $"{time}s", TextPrimaryBrushKey));
        }

        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Type"), FindFieldValue(block, IniConstants.FieldKeys.Type), TextSecondaryBrushKey);
    }

    private void AddDefaultVitals(IniBlock block, List<CanvasVitalItem> vitals)
    {
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Health"), ResolveHealthValue(block), SuccessBrushKey);

        var cost = FindFieldValue(block, IniConstants.FieldKeys.BuildCost);
        if (!string.IsNullOrWhiteSpace(cost))
        {
            vitals.Add(new(Localization.GetString("Tools.IniEditor.Vitals.Cost"), $"${cost}", WarningBrushKey));
        }

        var time = FindFieldValue(block, IniConstants.FieldKeys.BuildTime);
        if (!string.IsNullOrWhiteSpace(time))
        {
            vitals.Add(new(Localization.GetString("Tools.IniEditor.Vitals.BuildTime"), $"{time}s", TextPrimaryBrushKey));
        }

        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Vision"), FindFieldValue(block, IniConstants.FieldKeys.VisionRange), TextSecondaryBrushKey);
        AddVitalIfPresent(vitals, Localization.GetString("Tools.IniEditor.Vitals.Shroud"), FindFieldValue(block, IniConstants.FieldKeys.ShroudClearingRange), TextSecondaryBrushKey);
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
        if (EditableSelectedNode == null || string.IsNullOrWhiteSpace(NewFieldKey))
        {
            return;
        }

        var block = EditableSelectedNode.Block;
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
    /// Selects the given child module or sub-block node.
    /// </summary>
    /// <param name="node">The tree node to select.</param>
    [RelayCommand]
    private void SelectModuleNode(IniTreeNodeViewModel? node)
    {
        if (node != null)
        {
            ExpandAncestors(node);
            ExpandGroupForNode(node);
            SelectedNode = node;
        }
    }

    /// <summary>
    /// Selects the parent node of the currently selected block.
    /// </summary>
    [RelayCommand]
    private void SelectParentNode()
    {
        if (SelectedNode?.Parent != null)
        {
            SelectedNode = SelectedNode.Parent;
        }
    }

    /// <summary>
    /// Navigates to the block referenced by a field value.
    /// </summary>
    /// <param name="row">The field row whose reference to follow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RelayCommand]
    private async Task GoToDefinitionAsync(IniFieldRowViewModel? row, CancellationToken cancellationToken = default)
    {
        var referenceName = row?.IsPercentPair == true ? row.PairTarget : row?.Value;
        if (row?.ReferenceBlockType == null || string.IsNullOrWhiteSpace(referenceName))
        {
            return;
        }

        var local = _document?.Blocks.FirstOrDefault(block =>
            string.Equals(block.BlockType, row.ReferenceBlockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(block.Name, referenceName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (local != null)
        {
            SelectBlock(local);
            return;
        }

        var entry = referenceService.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.BlockType, row.ReferenceBlockType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Name, referenceName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (entry?.FilePath == null)
        {
            Notifications.ShowInfo(
                Localization.GetString("Tools.IniEditor.Reference.NotFoundTitle"),
                Localization.GetString("Tools.IniEditor.Reference.NotFoundMessage", referenceName.Trim()),
                NotificationDurations.Short);
            return;
        }

        if (string.Equals(entry.FilePath, FilePath, StringComparison.OrdinalIgnoreCase))
        {
            var localTarget = _document?.Blocks.FirstOrDefault(block =>
                string.Equals(block.BlockType, entry.BlockType, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(block.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
            if (localTarget != null)
            {
                SelectBlock(localTarget);
            }

            return;
        }

        if (await OpenFileAsync(entry.FilePath, cancellationToken).ConfigureAwait(true))
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
        if (EditableSelectedNode == null)
        {
            return;
        }

        var block = EditableSelectedNode.Block;
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
        if (EditableSelectedNode == null)
        {
            return;
        }

        var block = EditableSelectedNode.Block;
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
        var targetDoc = _document;
        if (targetDoc == null || SelectedReference == null)
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

        var cloned = await referenceService.CloneBlockAsync(SelectedReference, cancellationToken);
        if (!cloned.Success || cloned.Data == null)
        {
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Reference.CloneFailureTitle"),
                Localization.GetString("Tools.IniEditor.Reference.CloneFailureMessage", cloned.FirstError ?? SelectedReference.Name),
                NotificationDurations.Long);
            return;
        }

        var block = cloned.Data;
        await InvokeOnUIThreadAsync(() =>
        {
            if (_document != targetDoc)
            {
                return;
            }

            var index = targetDoc.Blocks.Count;
            targetDoc.Blocks.Add(block);
            PushUndo(new IniEditAction(
                Title: Localization.GetString("Tools.IniEditor.History.InsertReference"),
                Redo: () =>
                {
                    if (_document == targetDoc)
                    {
                        targetDoc.Blocks.Insert(Math.Min(index, targetDoc.Blocks.Count), block);
                        RebuildAll();
                    }
                },
                Undo: () =>
                {
                    if (_document == targetDoc)
                    {
                        targetDoc.Blocks.Remove(block);
                        RebuildAll();
                    }
                }));
            MarkDirty();
            RebuildAll();
            SelectBlock(block);
        });
    }

    /// <summary>
    /// Expands all block tree nodes.
    /// </summary>
    [RelayCommand]
    private void ExpandAllBlocks()
    {
        SetAllExpanded(VisibleRootNodes, true);
    }

    /// <summary>
    /// Collapses all block tree nodes.
    /// </summary>
    [RelayCommand]
    private void CollapseAllBlocks()
    {
        SetAllExpanded(VisibleRootNodes, false);
    }

    private bool CanInsertReference => _document != null && SelectedReference != null;

    private async Task AdoptDocumentAsync(IniDocument document, string? filePath, CancellationToken cancellationToken)
    {
        EnsureCultureSubscription();
        await InvokeOnUIThreadAsync(() =>
        {
            _document = document;
            _documentRevision++;
            CancelDeferred(ref _rawEditCts);
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
        await InvokeOnUIThreadAsync(RebuildValidationIssues).ConfigureAwait(false);
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
            RebuildVisualObjectCard();
            UpdateAvailableFieldKeys();
            RefreshRawPreviewText();
            RefreshRawText();
            HasDocument = _document != null;
            InsertReferenceCommand.NotifyCanExecuteChanged();
            RefreshEditorCommands();
            RebuildCanvasBlockCards();
            RebuildTrail();
            RebuildValidationIssues();
            QueueThumbnailRefresh();
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private void RebuildTree()
    {
        _isSyncingSelection = true;
        try
        {
            RebuildTreeCore();
        }
        finally
        {
            _isSyncingSelection = false;
        }

        if (!_isRebuilding)
        {
            RefreshSelectionDependents();
        }
    }

    private void RebuildTreeCore()
    {
        var selectedBlock = EditableSelectedNode?.Block;
        var expanded = new HashSet<IniBlock>();
        CollectExpandedBlocks(RootNodes, expanded);
        RootNodes.Clear();
        if (_document == null)
        {
            RebuildVisibleRootNodes();
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
        if (selectedBlock != null)
        {
            var match = FindNode(RootNodes, selectedBlock);
            if (match == null)
            {
                SelectedNode = null;
            }
            else
            {
                ExpandAncestors(match);
                SelectedNode = match;
            }
        }

        RebuildVisibleRootNodes();
        if (SelectedNode != null && !SelectedNode.IsGroupHeader)
        {
            ExpandGroupForNode(SelectedNode);
        }
    }

    private void RebuildVisibleRootNodes()
    {
        var selectedBlock = EditableSelectedNode?.Block;
        var wasSyncing = _isSyncingSelection;
        _isSyncingSelection = true;
        try
        {
            RebuildVisibleRootNodesCore(selectedBlock);
        }
        finally
        {
            _isSyncingSelection = wasSyncing;
        }
    }

    private void PopulateFlatVisibleNodes(int total)
    {
        foreach (var node in RootNodes)
        {
            VisibleRootNodes.Add(node);
        }

        BlockCountText = Localization.GetString("Tools.IniEditor.Tree.CountFlat", VisibleRootNodes.Count, total);
    }

    private void PopulateGroupedVisibleNodes(HashSet<string> expandedGroups, int total)
    {
        var groups = RootNodes
            .GroupBy(node => node.Block.BlockType, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
        foreach (var group in groups)
        {
            var header = IniTreeNodeViewModel.CreateGroupHeader(group.Key, group.Count());
            header.IsExpanded = expandedGroups.Contains(group.Key);
            foreach (var node in group)
            {
                header.Children.Add(node);
            }

            VisibleRootNodes.Add(header);
        }

        BlockCountText = Localization.GetString("Tools.IniEditor.Tree.CountGrouped", RootNodes.Count, total, groups.Count);
    }

    private void RestoreTreeSelection(string? selectedGroupType, IniBlock? selectedBlock)
    {
        if (selectedGroupType != null)
        {
            SelectedNode = VisibleRootNodes.FirstOrDefault(node =>
                node.IsGroupHeader && string.Equals(node.Block.BlockType, selectedGroupType, StringComparison.OrdinalIgnoreCase));
        }
        else if (selectedBlock != null)
        {
            var match = FindNode(RootNodes, selectedBlock);
            if (match != null)
            {
                ExpandAncestors(match);
                SelectedNode = match;
            }
        }

        if (SelectedNode != null && !IsNodeVisible(SelectedNode))
        {
            SelectedNode = null;
        }

        EnsureEditableSelection();
        if (SelectedNode != null && !SelectedNode.IsGroupHeader)
        {
            ExpandGroupForNode(SelectedNode);
        }
    }

    private void RebuildVisibleRootNodesCore(IniBlock? selectedBlock)
    {
        var expandedGroups = new HashSet<string>(
            VisibleRootNodes.Where(node => node.IsGroupHeader && node.IsExpanded).Select(node => node.Block.BlockType),
            StringComparer.OrdinalIgnoreCase);
        var selectedGroupType = SelectedNode is { IsGroupHeader: true } ? SelectedNode.Block.BlockType : null;
        VisibleRootNodes.Clear();
        var total = _document?.Blocks.Count ?? 0;
        if (!IsGroupByTypeEnabled || RootNodes.Count <= IniConstants.Editor.BlockGroupThreshold)
        {
            PopulateFlatVisibleNodes(total);
        }
        else
        {
            PopulateGroupedVisibleNodes(expandedGroups, total);
        }

        RestoreTreeSelection(selectedGroupType, selectedBlock);
    }

    private void EnsureEditableSelection()
    {
        if (SelectedNode != null && !SelectedNode.IsGroupHeader)
        {
            return;
        }

        var candidate = RootNodes.FirstOrDefault();
        if (candidate == null)
        {
            if (SelectedNode != null && SelectedNode.IsGroupHeader)
            {
                SelectedNode = null;
            }

            return;
        }

        ExpandAncestors(candidate);
        ExpandGroupForNode(candidate);
        SelectedNode = candidate;
    }

    private bool IsNodeVisible(IniTreeNodeViewModel node)
    {
        var root = node;
        while (root.Parent != null)
        {
            root = root.Parent;
        }

        if (VisibleRootNodes.Contains(root))
        {
            return true;
        }

        return VisibleRootNodes.Any(header => header.IsGroupHeader && header.Children.Contains(root));
    }

    private void ExpandGroupForNode(IniTreeNodeViewModel node)
    {
        var root = node;
        while (root.Parent != null)
        {
            root = root.Parent;
        }

        var header = VisibleRootNodes.FirstOrDefault(candidate =>
            candidate.IsGroupHeader && candidate.Children.Contains(root));
        if (header != null)
        {
            header.IsExpanded = true;
        }
    }

    private void RebuildFieldRows()
    {
        FieldRows.Clear();
        var block = EditableSelectedNode?.Block;
        if (block == null)
        {
            return;
        }

        var suggestions = BuildSuggestionScope();
        for (var i = 0; i < block.Fields.Count; i++)
        {
            var key = block.Fields[i].Key;
            var fieldIndex = i;
            var known = schemaService.TryGetField(block.BlockType, key, out var schema);
            var metadata = new IniFieldMetadata(
                schema?.Description,
                known,
                BuildFieldTooltip(key, schema),
                ResolveSuggestions(key, schema, suggestions),
                ResolveReferenceBlockType(key, schema),
                IsTextureSuggestionKey(key, schema),
                ResolvePairTargets(key, schema, suggestions));
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
        var block = EditableSelectedNode?.Block;
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
        var block = EditableSelectedNode?.Block;
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
                foreach (var field in set.Fields.Where(field => string.Equals(field.Key, IniConstants.FieldKeys.Weapon, StringComparison.OrdinalIgnoreCase)))
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
        var tableName = set == null ? null : FindFieldValue(set, IniConstants.FieldKeys.Armor);
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
        RebuildValidationIssues();
    }

    private void MarkDocumentDirty()
    {
        MarkDirty();
        _documentRevision++;
        ScheduleDeferred(ref _previewCts, IniConstants.Editor.PreviewRefreshDebounceMs, RefreshPreviews);
    }

    private void SyncDirtyAfterHistory()
    {
        _documentRevision++;
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
        _documentRevision++;
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
            ExpandGroupForNode(match);
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
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to copy INI text to the clipboard");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to copy INI text to the clipboard");
        }
        catch (InvalidOperationException ex)
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
        var first = await FileExplorer.FindFirstFileAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(first))
        {
            return;
        }

        await InvokeOnUIThreadAsync(() => OpenFileAsync(first, cancellationToken)).ConfigureAwait(false);
        await LoadTexturePickerItemsAsync(cancellationToken).ConfigureAwait(false);
    }

    private void OnExplorerFileActivated(object? sender, EditorFileTreeNodeViewModel node)
    {
        _ = OpenExplorerFileAsync(node.FullPath);
    }

    private async Task OpenExplorerFileAsync(string fullPath)
    {
        try
        {
            await OpenFileAsync(fullPath, CancellationToken.None).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Discard confirmation was cancelled.
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to open INI file {Path} from the explorer", fullPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to open INI file {Path} from the explorer", fullPath);
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

    private sealed class SuggestionScope
    {
        public IReadOnlyList<string>? Textures { get; init; }

        public IReadOnlyList<string> Sides { get; init; } = [];

        public Dictionary<string, IReadOnlyList<string>> DocumentValues { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, IReadOnlyList<string>?> References { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string>? GetDocumentValues(string key)
        {
            return DocumentValues.TryGetValue(key, out var values) && values.Count > 0 ? values : null;
        }
    }

    private void EnsureSuggestionIndexes()
    {
        if (ReferenceEquals(_suggestionIndexDocument, _document) && _suggestionIndexRevision == _documentRevision)
        {
            return;
        }

        _suggestionIndexDocument = _document;
        _suggestionIndexRevision = _documentRevision;
        _suggestionFieldValues = BuildDocumentValueIndex();
        _suggestionBlockNames = BuildBlockNameIndex();
    }

    private Dictionary<string, HashSet<string>> BuildBlockNameIndex()
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        if (_document == null)
        {
            return index;
        }

        foreach (var block in _document.Blocks)
        {
            if (string.IsNullOrWhiteSpace(block.Name))
            {
                continue;
            }

            if (!index.TryGetValue(block.BlockType, out var names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                index[block.BlockType] = names;
            }

            names.Add(block.Name);
        }

        return index;
    }

    private SuggestionScope BuildSuggestionScope()
    {
        EnsureSuggestionIndexes();
        return new SuggestionScope
        {
            Textures = ResolveTextureSuggestions(),
            Sides = ResolveSideSuggestions(_suggestionFieldValues),
            DocumentValues = _suggestionFieldValues,
        };
    }

    private Dictionary<string, IReadOnlyList<string>> BuildDocumentValueIndex()
    {
        var index = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (_document != null)
        {
            foreach (var field in _document.Blocks.SelectMany(static block => block.Fields))
            {
                if (string.IsNullOrWhiteSpace(field.Value))
                {
                    continue;
                }

                if (!index.TryGetValue(field.Key, out var values))
                {
                    values = [];
                    index[field.Key] = values;
                }

                var value = field.Value.Trim();
                if (values.Count < 50 && !values.Contains(value, StringComparer.OrdinalIgnoreCase))
                {
                    values.Add(value);
                }
            }
        }

        return index.ToDictionary(static entry => entry.Key, static entry => (IReadOnlyList<string>)entry.Value, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<string>? ResolveSuggestions(string key, IniFieldSchema? schema, SuggestionScope scope)
    {
        if (string.Equals(key, IniConstants.FieldKeys.KindOf, StringComparison.OrdinalIgnoreCase))
        {
            return IniConstants.KindOfFlags.All;
        }

        if (string.Equals(key, IniConstants.FieldKeys.Side, StringComparison.OrdinalIgnoreCase))
        {
            return scope.Sides;
        }

        var merged = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (schema?.Options != null)
        {
            AddUniqueSuggestions(merged, seen, schema.Options);
        }

        if (IsTextureSuggestionKey(key, schema) && scope.Textures != null)
        {
            AddUniqueSuggestions(merged, seen, scope.Textures);
        }

        var refType = ResolveReferenceBlockType(key, schema);
        if (!string.IsNullOrEmpty(refType))
        {
            var refs = ResolveReferenceSuggestions(refType, scope.References);
            if (refs != null)
            {
                AddUniqueSuggestions(merged, seen, refs);
            }
        }

        var documentValues = scope.GetDocumentValues(key);
        if (documentValues != null)
        {
            AddUniqueSuggestions(merged, seen, documentValues);
        }

        return merged.Count > 0 ? merged : null;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as an instance helper to satisfy member ordering.")]
    private void AddUniqueSuggestions(List<string> merged, HashSet<string> seen, IReadOnlyList<string> candidates)
    {
        foreach (var candidate in candidates)
        {
            if (seen.Add(candidate))
            {
                merged.Add(candidate);
            }
        }
    }

    private IReadOnlyList<string>? ResolvePairTargets(string key, IniFieldSchema? schema, SuggestionScope scope)
    {
        if (!string.Equals(key, IniConstants.FieldKeys.ProductionTimeChange, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return ResolveReferenceSuggestions(schema?.ReferenceBlockType ?? IniConstants.BlockTypes.Object, scope.References);
    }

    private IReadOnlyList<string>? ResolveTextureSuggestions()
    {
        var textures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in TexturePickerItems)
        {
            textures.Add(item.Name);
        }

        foreach (var name in referenceService.GetNames(IniConstants.BlockTypes.MappedImage))
        {
            textures.Add(name);
        }

        if (_suggestionBlockNames.TryGetValue(IniConstants.BlockTypes.MappedImage, out var mappedImages))
        {
            foreach (var name in mappedImages)
            {
                textures.Add(name);
            }
        }

        return textures.Count > 0
            ? textures.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList()
            : null;
    }

    private IReadOnlyList<string>? ResolveReferenceSuggestions(string refType, Dictionary<string, IReadOnlyList<string>?> cache)
    {
        if (cache.TryGetValue(refType, out var cached))
        {
            return cached;
        }

        var refs = new HashSet<string>(referenceService.GetNames(refType), StringComparer.OrdinalIgnoreCase);
        if (_suggestionBlockNames.TryGetValue(refType, out var documentNames))
        {
            foreach (var name in documentNames)
            {
                refs.Add(name);
            }
        }

        IReadOnlyList<string>? result = refs.Count > 0
            ? refs.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList()
            : null;
        cache[refType] = result;
        return result;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as an instance helper to satisfy member ordering.")]
    private IReadOnlyList<string> ResolveSideSuggestions(Dictionary<string, IReadOnlyList<string>> fieldValues)
    {
        var suggestions = new HashSet<string>(IniConstants.Sides.All, StringComparer.OrdinalIgnoreCase);
        if (fieldValues.TryGetValue(IniConstants.FieldKeys.Side, out var values))
        {
            foreach (var value in values)
            {
                suggestions.Add(value);
            }
        }

        return suggestions.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
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
                await LoadThumbnailsAsync(missing, generation, CancellationToken.None).ConfigureAwait(false);
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
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to refresh INI texture thumbnails");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to refresh INI texture thumbnails");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Failed to refresh INI texture thumbnails");
        }
    }

    private List<string> CollectTextureNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (EditableSelectedNode?.Block != null)
        {
            var portrait = FindFieldValue(EditableSelectedNode.Block, IniConstants.FieldKeys.SelectPortrait) ??
                           FindFieldValue(EditableSelectedNode.Block, IniConstants.FieldKeys.ButtonImage);
            if (!string.IsNullOrWhiteSpace(portrait))
            {
                names.Add(portrait.Trim());
            }
        }

        if (_document != null)
        {
            foreach (var block in _document.Blocks.Take(IniConstants.Editor.MaxCardThumbnails))
            {
                var cardPortrait = (FindFieldValue(block, IniConstants.FieldKeys.SelectPortrait) ??
                                    FindFieldValue(block, IniConstants.FieldKeys.ButtonImage))?.Trim();
                if (!string.IsNullOrWhiteSpace(cardPortrait))
                {
                    names.Add(cardPortrait);
                }
            }
        }

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

    private Dictionary<string, Bitmap?> DecodeThumbnails(IReadOnlyDictionary<string, byte[]> data, IReadOnlyList<string> names)
    {
        var decoded = new Dictionary<string, Bitmap?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, png) in data)
        {
            try
            {
                using var stream = new MemoryStream(png);
                decoded[name] = new Bitmap(stream);
            }
            catch (ArgumentException ex)
            {
                logger.LogWarning(ex, "Failed to decode texture thumbnail {Name}", name);
                decoded[name] = null;
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Failed to decode texture thumbnail {Name}", name);
                decoded[name] = null;
            }
            catch (NotSupportedException ex)
            {
                logger.LogWarning(ex, "Failed to decode texture thumbnail {Name}", name);
                decoded[name] = null;
            }
        }

        foreach (var name in names)
        {
            decoded.TryAdd(name, null);
        }

        return decoded;
    }

    private void UpdateDecodedThumbnailsOnUI(Dictionary<string, Bitmap?> decoded, int generation, string installationPath)
    {
        var currentInstallation = SelectedInstallation ?? AvailableInstallations.FirstOrDefault();
        var currentInstallationPath = currentInstallation?.Path;
        if (string.IsNullOrEmpty(currentInstallationPath))
        {
            currentInstallationPath = string.IsNullOrEmpty(FilePath) ? FileExplorer.Directory : Path.GetDirectoryName(FilePath);
        }

        if (generation != _thumbnailGeneration || !string.Equals(currentInstallationPath, installationPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var (name, bitmap) in decoded)
        {
            _textureThumbnails[name] = bitmap;
            var match = _allTextureItems.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (match != null)
            {
                match.Thumbnail = bitmap;
            }
        }

        ApplyCachedThumbnails();
        UpdateCanvasBlockCardPortraits();
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<string> names, int generation, CancellationToken cancellationToken)
    {
        var installation = SelectedInstallation ?? AvailableInstallations.FirstOrDefault();
        var installationPath = installation?.Path;

        // Without a detected installation there is no tier signal, so fall back to the
        // platform default. Zero Hour mode enforces strict per-game isolation while the
        // base tier stays permissive about loose project files.
        var isZeroHour = installation is { IsZeroHour: true };

        var projectDirectory = string.IsNullOrEmpty(FilePath) ? FileExplorer.Directory : Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(installationPath))
        {
            installationPath = projectDirectory;
        }

        if (string.IsNullOrEmpty(installationPath))
        {
            return;
        }

        var result = await imageAssetService.GetImagesAsync(
            names,
            installationPath,
            null,
            projectDirectory,
            [],
            isZeroHour,
            cancellationToken).ConfigureAwait(false);
        if (!result.Success || result.Data == null)
        {
            return;
        }

        var decoded = DecodeThumbnails(result.Data, names);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await InvokeOnUIThreadAsync(() => UpdateDecodedThumbnailsOnUI(decoded, generation, installationPath)).ConfigureAwait(false);
    }

    private void UpdateSelectedBlockPortraitFromCache()
    {
        if (EditableSelectedNode?.Block != null)
        {
            var portrait = FindFieldValue(EditableSelectedNode.Block, IniConstants.FieldKeys.SelectPortrait) ??
                           FindFieldValue(EditableSelectedNode.Block, IniConstants.FieldKeys.ButtonImage);
            SelectedBlockPortrait = (!string.IsNullOrWhiteSpace(portrait) && _textureThumbnails.TryGetValue(portrait.Trim(), out var thumb))
                ? thumb
                : null;
        }
    }

    private void UpdateRowThumbnailsFromCache()
    {
        foreach (var row in FieldRows)
        {
            if (row.IsTexture &&
                !string.IsNullOrWhiteSpace(row.Value) &&
                _textureThumbnails.TryGetValue(row.Value.Trim(), out var thumbnail))
            {
                row.TextureThumbnail = thumbnail;
            }
            else if (row.IsTexture)
            {
                row.TextureThumbnail = null;
            }
        }

        foreach (var row in AssembledRows)
        {
            if (!string.IsNullOrWhiteSpace(row.TextureName) &&
                _textureThumbnails.TryGetValue(row.TextureName.Trim(), out var thumbnail))
            {
                row.TextureThumbnail = thumbnail;
            }
            else if (!string.IsNullOrWhiteSpace(row.TextureName))
            {
                row.TextureThumbnail = null;
            }
        }
    }

    private void ApplyCachedThumbnails()
    {
        UpdateSelectedBlockPortraitFromCache();
        UpdateRowThumbnailsFromCache();

        var assetNode = EditableSelectedNode;
        if (assetNode?.Block != null)
        {
            RebuildSelectedBlockAssets(assetNode.Block, assetNode);
        }

        OnPropertyChanged(nameof(PickerThumbnailProvider));
    }

    private async Task LoadTexturePickerItemsAsync(CancellationToken cancellationToken)
    {
        var files = CollectMappedImageFiles(FileExplorer.Directory);

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

        await PopulateTexturePickerItemsAsync(definitions).ConfigureAwait(false);
    }

    private List<string> CollectMappedImageFiles(string? directory)
    {
        var files = new List<string>();
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return files;
        }

        try
        {
            files.AddRange(Directory
                .EnumerateFiles(directory, ModBuilderConstants.FileNames.IniSearchPattern, SearchOption.AllDirectories)
                .Where(file => file.Contains("MappedImages", StringComparison.OrdinalIgnoreCase))
                .Take(IniConstants.Editor.MaxMappedImageFiles));
        }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Failed to enumerate mapped image files in {Directory}", directory);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Failed to enumerate mapped image files in {Directory}", directory);
        }

        return files;
    }

    private async Task PopulateTexturePickerItemsAsync(List<MappedImageDefinition> definitions)
    {
        await InvokeOnUIThreadAsync(() =>
        {
            TexturePickerItems.Clear();
            _allTextureItems.Clear();
            foreach (var definition in definitions.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                TexturePickerItems.Add(definition);
                _allTextureItems.Add(new IniTextureItemViewModel
                {
                    Name = definition.Name,
                    Tooltip = $"{definition.Name} ({definition.Width}x{definition.Height}) [{(string.IsNullOrEmpty(definition.SourcePath) ? string.Empty : Path.GetFileName(definition.SourcePath))}]",
                });
            }

            ApplyTextureFilter();
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

        if (_document.HasDiscardedContent)
        {
            logger.LogWarning("Refusing to save INI file {Path}: recovery discarded source lines", filePath);
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Save.FailureTitle"),
                Localization.GetString("Tools.IniEditor.Save.DiscardedContentMessage"),
                NotificationDurations.Long);
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
        catch (IOException ex)
        {
            logger.LogError(ex, "Failed to save INI file {Path}", filePath);
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Save.FailureTitle"),
                Localization.GetString("Tools.IniEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError(ex, "Failed to save INI file {Path}", filePath);
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Save.FailureTitle"),
                Localization.GetString("Tools.IniEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
        }
        catch (ArgumentException ex)
        {
            logger.LogError(ex, "Failed to save INI file {Path}", filePath);
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Save.FailureTitle"),
                Localization.GetString("Tools.IniEditor.Save.FailureMessage", ex.Message),
                NotificationDurations.Long);
        }
        catch (NotSupportedException ex)
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
        RebuildVisualObjectCard();
        RebuildCanvasBlockCards();
        ApplyTextureFilter();
        FilterReferenceResults();
        RebuildValidationIssues();
        RefreshEditorCommands();
        RefreshPreviewStatusText();
    }

    partial void OnSelectedNodeChanged(IniTreeNodeViewModel? value)
    {
        if (_isRebuilding || _isSyncingSelection)
        {
            return;
        }

        if (value != null && value.IsGroupHeader)
        {
            value.IsExpanded = true;
            RestoreEditableSelection();
            return;
        }

        if (value != null)
        {
            _lastEditableBlock = value.Block;
        }

        RefreshSelectionDependents();
    }

    private void RefreshSelectionDependents()
    {
        RebuildFieldRows();
        RebuildCanvasSummary();
        RebuildAssembledRows();
        RebuildVisualObjectCard();
        UpdateAvailableFieldKeys();
        RefreshRawPreviewText();
        RebuildTrail();
        RebuildValidationIssues();
        QueueThumbnailRefresh();
    }

    private void RestoreEditableSelection()
    {
        if (_lastEditableBlock != null)
        {
            var match = FindNode(RootNodes, _lastEditableBlock);
            if (match != null)
            {
                _isSyncingSelection = true;
                try
                {
                    ExpandAncestors(match);
                    ExpandGroupForNode(match);
                    SelectedNode = match;
                }
                finally
                {
                    _isSyncingSelection = false;
                }

                RefreshSelectionDependents();
                return;
            }

            _lastEditableBlock = null;
        }

        _isSyncingSelection = true;
        try
        {
            EnsureEditableSelection();
        }
        finally
        {
            _isSyncingSelection = false;
        }

        RefreshSelectionDependents();
    }

    [SuppressMessage("Minor Bug", "S4158:Empty collections should not be accessed", Justification = "False positive: the trail stack always holds the non-null selected node before iteration.")]
    private void RebuildTrail()
    {
        SelectedNodeTrail.Clear();
        var selectedNode = SelectedNode;
        if (selectedNode == null)
        {
            HasTrail = false;
            return;
        }

        var chain = new Stack<IniTreeNodeViewModel>();
        var current = selectedNode;
        while (current != null)
        {
            chain.Push(current);
            current = current.Parent;
        }

        foreach (var node in chain)
        {
            SelectedNodeTrail.Add(node);
        }

        HasTrail = SelectedNodeTrail.Count > 1;
    }

    private void RebuildValidationIssues()
    {
        EnsureSuggestionIndexes();
        ValidationIssues.Clear();
        var total = 0;
        if (_document != null)
        {
            foreach (var parseError in _document.ParseErrors)
            {
                total++;
                if (ValidationIssues.Count < IniConstants.Editor.MaxValidationRows)
                {
                    ValidationIssues.Add(new IniValidationRow(parseError, true));
                }
            }
        }

        total += AddSelectedBlockReferenceRows();
        ValidationIssueCount = total;
        HasValidationIssues = total > 0;
    }

    private int AddSelectedBlockReferenceRows()
    {
        if (SelectedNode == null)
        {
            return 0;
        }

        var cache = new Dictionary<(string ReferenceType, bool IsTexture), HashSet<string>>();

        // TryAddReferenceRow records a validation row as a side effect while testing each row.
        return FieldRows.Count(row => TryAddReferenceRow(row, cache));
    }

    private bool TryAddReferenceRow(IniFieldRowViewModel row, Dictionary<(string ReferenceType, bool IsTexture), HashSet<string>> cache)
    {
        var referenceType = row.ReferenceBlockType;
        if (referenceType == null && row.IsTexture)
        {
            referenceType = IniConstants.BlockTypes.MappedImage;
        }

        if (referenceType == null || string.IsNullOrWhiteSpace(row.Value))
        {
            return false;
        }

        var value = row.Value.Trim();
        if (value.Length == 0 || value.IndexOfAny([' ', '\t']) >= 0)
        {
            return false;
        }

        var cacheKey = (referenceType, row.IsTexture);
        if (!cache.TryGetValue(cacheKey, out var known))
        {
            known = CollectKnownReferenceNames(referenceType, row.IsTexture);
            cache[cacheKey] = known;
        }

        if (known.Count == 0 || known.Contains(value))
        {
            return false;
        }

        if (ValidationIssues.Count < IniConstants.Editor.MaxValidationRows)
        {
            ValidationIssues.Add(new IniValidationRow(
                Localization.GetString("Tools.IniEditor.Validation.UnknownReference", value, referenceType),
                false));
        }

        return true;
    }

    private HashSet<string> CollectKnownReferenceNames(string referenceType, bool includeTextures)
    {
        var known = new HashSet<string>(referenceService.GetNames(referenceType), StringComparer.OrdinalIgnoreCase);
        if (_suggestionBlockNames.TryGetValue(referenceType, out var documentNames))
        {
            foreach (var name in documentNames)
            {
                known.Add(name);
            }
        }

        if (includeTextures)
        {
            foreach (var item in TexturePickerItems)
            {
                known.Add(item.Name);
            }

            foreach (var name in _textureThumbnails.Keys)
            {
                known.Add(name);
            }
        }

        return known;
    }

    private void UpdateCanvasBlockCardPortraits()
    {
        foreach (var card in CanvasBlockCards)
        {
            var portraitName = (FindFieldValue(card.Block, IniConstants.FieldKeys.SelectPortrait) ??
                                FindFieldValue(card.Block, IniConstants.FieldKeys.ButtonImage))?.Trim();
            if (!string.IsNullOrWhiteSpace(portraitName) &&
                _textureThumbnails.TryGetValue(portraitName, out var thumb) &&
                card.Portrait != thumb)
            {
                card.Portrait = thumb;
            }
        }
    }

    private void RebuildCanvasBlockCards()
    {
        CanvasBlockCards.Clear();
        if (_document == null)
        {
            return;
        }

        var hpLabel = Localization.GetString("Tools.IniEditor.Canvas.CardHpLabel");
        var costLabel = Localization.GetString("Tools.IniEditor.Vitals.Cost");
        var cmdLabel = Localization.GetString("Tools.IniEditor.Canvas.CardCmdLabel");

        foreach (var block in _document.Blocks.Take(IniConstants.Editor.MaxCardThumbnails))
        {
            var title = !string.IsNullOrWhiteSpace(block.Name) ? block.Name : block.BlockType;
            var side = FindFieldValue(block, IniConstants.FieldKeys.Side);
            var portraitName = (FindFieldValue(block, IniConstants.FieldKeys.SelectPortrait) ??
                                FindFieldValue(block, IniConstants.FieldKeys.ButtonImage))?.Trim();
            IImage? portrait = null;
            if (!string.IsNullOrWhiteSpace(portraitName) && _textureThumbnails.TryGetValue(portraitName, out var thumb))
            {
                portrait = thumb;
            }

            var vitals = new List<CanvasVitalItem>();
            var health = ResolveHealthValue(block);
            if (!string.IsNullOrWhiteSpace(health))
            {
                vitals.Add(new(hpLabel, health, SuccessBrushKey));
            }

            var cost = FindFieldValue(block, IniConstants.FieldKeys.BuildCost);
            if (!string.IsNullOrWhiteSpace(cost))
            {
                vitals.Add(new(costLabel, $"${cost}", WarningBrushKey));
            }

            var cmd = FindFieldValue(block, IniConstants.FieldKeys.Command);
            if (!string.IsNullOrWhiteSpace(cmd))
            {
                vitals.Add(new(cmdLabel, cmd, AccentBrushKey));
            }

            CanvasBlockCards.Add(new IniCanvasCardViewModel(block, title, block.BlockType, side, portrait, vitals));
        }
    }

    [RelayCommand]
    private void ToggleCanvasOverview()
    {
        ShowAllBlocksOnCanvas = !ShowAllBlocksOnCanvas;
    }

    [RelayCommand]
    private void SelectBlockCard(IniBlock? block)
    {
        if (block == null)
        {
            return;
        }

        ShowAllBlocksOnCanvas = false;
        var targetNode = FindNodeForBlock(RootNodes, block);
        if (targetNode != null)
        {
            ExpandGroupForNode(targetNode);
            SelectedNode = targetNode;
        }
    }

    [RelayCommand]
    private void ApplyTextureToSelectedBlock(string? textureName)
    {
        if (!string.IsNullOrWhiteSpace(textureName))
        {
            AttachTextureToSelectedBlock(textureName);
        }
    }

    [RelayCommand]
    private async Task CopyRawPreviewAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(RawPreviewText))
        {
            return;
        }

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard != null)
        {
            await desktop.MainWindow.Clipboard.SetTextAsync(RawPreviewText);
            return;
        }

        Notifications.ShowWarning(
            Localization.GetString("Tools.IniEditor.Toast.ClipboardUnavailableTitle"),
            Localization.GetString("Tools.IniEditor.Toast.ClipboardUnavailableMessage"),
            NotificationDurations.Medium);
    }

    [RelayCommand]
    private async Task PasteRawPreviewAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard != null)
        {
            var text = await desktop.MainWindow.Clipboard.GetTextAsync();
            if (!string.IsNullOrEmpty(text))
            {
                RawPreviewText = text;
            }

            return;
        }

        Notifications.ShowWarning(
            Localization.GetString("Tools.IniEditor.Toast.ClipboardUnavailableTitle"),
            Localization.GetString("Tools.IniEditor.Toast.ClipboardUnavailableMessage"),
            NotificationDurations.Medium);
    }

    private void RebuildVisualObjectCard()
    {
        var node = EditableSelectedNode;
        var block = node?.Block;
        if (block == null)
        {
            ResetVisualObjectCard();
            return;
        }

        HasSelectedBlock = true;
        SelectedBlockTitle = block.Name;
        SelectedBlockType = block.BlockType;
        SelectedBlockIconKind = IniBlockIconHelper.GetIconKind(block.BlockType);

        SelectedBlockSide = FindFieldValue(block, IniConstants.FieldKeys.Side) ?? string.Empty;
        SelectedBlockHealth = ResolveHealthValue(block);
        SelectedBlockCost = FindFieldValue(block, IniConstants.FieldKeys.BuildCost);
        SelectedBlockTime = FindFieldValue(block, IniConstants.FieldKeys.BuildTime);

        var vitals = new List<CanvasVitalItem>();
        AddBlockTypeVitals(block, vitals);
        SelectedBlockVitals = vitals;
        OnPropertyChanged(nameof(HasSelectedBlockVitals));

        SelectedBlockModel = ResolveBlockModel(block);
        ApplyVisualObjectPortrait(block);
        ApplyVisualObjectLists(block, node);
        RebuildSelectedBlockAssets(block, node);
        RebuildPreviewHiddenMeshes(block);

        HasSelectedBlockKindOf = SelectedBlockKindOfList.Count > 0;
        HasSelectedBlockModules = SelectedBlockModules.Count > 0;
        OnPropertyChanged(nameof(HealthLabel));
        OnPropertyChanged(nameof(CostLabel));
        OnPropertyChanged(nameof(BuildTimeLabel));
        OnPropertyChanged(nameof(KindOfLabel));
        OnPropertyChanged(nameof(ModulesLabel));
        QueueModelPreviewRefresh();
    }

    private void ResetVisualObjectCard()
    {
        HasSelectedBlock = false;
        HasSelectedBlockKindOf = false;
        HasSelectedBlockModules = false;
        SelectedBlockTitle = string.Empty;
        SelectedBlockType = string.Empty;
        SelectedBlockIconKind = "CubeOutline";
        SelectedBlockSide = string.Empty;
        SelectedBlockHealth = null;
        SelectedBlockCost = null;
        SelectedBlockTime = null;
        SelectedBlockModel = string.Empty;
        SelectedBlockPortrait = null;
        SelectedBlockKindOfList.Clear();
        SelectedBlockModules.Clear();
        SelectedBlockAssets.Clear();
        PreviewHiddenMeshNames = null;
        HasSelectedBlockAssets = false;
        SelectedBlockVitals = [];
        OnPropertyChanged(nameof(HasSelectedBlockVitals));
        ClearModelPreview();
    }

    private void QueueModelPreviewRefresh()
    {
        var model = SelectedBlockModel?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(model))
        {
            ClearModelPreview();
            return;
        }

        if (string.Equals(model, _lastPreviewModel, StringComparison.OrdinalIgnoreCase) && HasPreviewScene)
        {
            RebuildPreviewMeshModules();
            return;
        }

        var installation = SelectedInstallation ?? AvailableInstallations.FirstOrDefault();
        var installationPath = installation?.Path;
        var isZeroHour = installation is { IsZeroHour: true };
        var projectDirectory = string.IsNullOrEmpty(FilePath) ? FileExplorer.Directory : Path.GetDirectoryName(FilePath);
        if (string.IsNullOrEmpty(installationPath))
        {
            installationPath = projectDirectory;
        }

        SetPreviewStatus("Tools.IniEditor.Preview3D.Loading", model);
        IsPreviewLoading = true;
        ScheduleDeferred(ref _modelPreviewCts, IniConstants.Editor.ModelPreviewDebounceMs, token => _ = RefreshModelPreviewAsync(model, installationPath, isZeroHour, projectDirectory, token));
    }

    private void ClearModelPreview()
    {
        _modelPreviewGeneration++;
        CancelDeferred(ref _modelPreviewCts);
        StopPreviewPlayback();
        _previewResolved = null;
        _lastPreviewModel = null;
        PreviewScene = null;
        PreviewPose = null;
        PreviewBindPose = null;
        HasPreviewScene = false;
        IsPreviewLoading = false;
        IsPreviewPlaying = false;
        PreviewModelName = string.Empty;
        PreviewMeshes.Clear();
        PreviewClips.Clear();
        PreviewMeshModules.Clear();
        SelectedPreviewClip = null;
        SelectedPreviewMesh = null;
        PreviewSelectedMeshIndex = -1;
        PreviewFrame = 0;
        PreviewFrameCount = 0;
        OnPropertyChanged(nameof(HasPreviewClips));
        OnPropertyChanged(nameof(HasPreviewMeshModules));
        SetPreviewStatus("Tools.IniEditor.Preview3D.Empty");
    }

    private async Task RefreshModelPreviewAsync(
        string model,
        string? installationPath,
        bool isZeroHour,
        string? projectDirectory,
        CancellationToken cancellationToken)
    {
        var generation = ++_modelPreviewGeneration;
        if (string.IsNullOrEmpty(installationPath))
        {
            PostToUIThread(() => ApplyPreviewFailure(model, generation, "Tools.IniEditor.Preview3D.NoInstallation", false));
            return;
        }

        OperationResult<W3dResolvedModel>? resolved = null;
        try
        {
            resolved = await Task.Run(
                () => modelResolver.ResolveAsync(model, installationPath, isZeroHour, projectDirectory, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancelled previews are discarded silently.
        }

        if (generation != _modelPreviewGeneration || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (resolved == null || !resolved.Success || resolved.Data == null)
        {
            bool notFound = resolved != null && resolved.FirstError?.Contains("not found", StringComparison.OrdinalIgnoreCase) == true;
            string key = notFound ? "Tools.IniEditor.Preview3D.NotFound" : "Tools.IniEditor.Preview3D.ParseError";
            PostToUIThread(() => ApplyPreviewFailure(model, generation, key, !notFound));
            return;
        }

        try
        {
            var data = resolved.Data;
            var textures = data.Textures.ToDictionary(texture => texture.Name, texture => texture.Texture, StringComparer.OrdinalIgnoreCase);
            var bones = BoneMap(data.Model);
            var scene = W3dSceneBuilder.Build(data.Model, textures, bones);
            PostToUIThread(() => ApplyPreviewResolved(model, generation, data, scene));
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to build 3D preview scene for {Model}", model);
            PostToUIThread(() => ApplyPreviewFailure(model, generation, "Tools.IniEditor.Preview3D.ParseError", true));
        }
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as an instance helper to satisfy member ordering.")]
    private Dictionary<string, int> BoneMap(W3dModel model)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var level = model.Lods.FirstOrDefault()?.Levels.FirstOrDefault();
        if (level == null)
        {
            return map;
        }

        foreach (var subObject in level.SubObjects.Where(s => !string.IsNullOrWhiteSpace(s.MeshName)))
        {
            if (!map.ContainsKey(subObject.MeshName))
            {
                map[subObject.MeshName] = (int)subObject.BoneIndex;
            }
        }

        return map;
    }

    private void ApplyPreviewResolved(string model, int generation, W3dResolvedModel data, W3dRenderScene scene)
    {
        if (generation != _modelPreviewGeneration)
        {
            return;
        }

        StopPreviewPlayback();
        _previewResolved = data;
        _lastPreviewModel = model;
        PreviewScene = scene;
        PreviewPose = null;
        HasPreviewScene = true;
        IsPreviewLoading = false;
        PreviewModelName = model;
        RebuildPreviewMeshList(data, scene);
        RebuildPreviewClips(data);
        PreviewFrame = 0;
        SamplePreviewPose();
        RebuildPreviewMeshModules();
        if (SelectedPreviewClip == null || !SelectedPreviewClip.IsSamplable)
        {
            IsPreviewPlaying = false;
        }
        else if (IsPreviewPlaying)
        {
            StartPreviewPlayback();
        }

        int pivots = data.Model.Hierarchies.FirstOrDefault()?.Pivots.Count ?? 0;
        if (data.MissingTextures.Count > 0)
        {
            SetPreviewStatus(
                "Tools.IniEditor.Preview3D.ReadyMissing",
                scene.Meshes.Count,
                scene.Textures.Count,
                data.MissingTextures.Count,
                pivots);
        }
        else
        {
            SetPreviewStatus("Tools.IniEditor.Preview3D.Ready", scene.Meshes.Count, scene.Textures.Count, pivots);
        }
    }

    private void ApplyPreviewFailure(string model, int generation, string statusKey, bool toast)
    {
        if (generation != _modelPreviewGeneration)
        {
            return;
        }

        StopPreviewPlayback();
        _previewResolved = null;
        _lastPreviewModel = model;
        PreviewScene = null;
        PreviewPose = null;
        PreviewBindPose = null;
        HasPreviewScene = false;
        IsPreviewLoading = false;
        IsPreviewPlaying = false;
        PreviewMeshes.Clear();
        PreviewClips.Clear();
        PreviewMeshModules.Clear();
        SelectedPreviewClip = null;
        SelectedPreviewMesh = null;
        PreviewSelectedMeshIndex = -1;
        PreviewFrameCount = 0;
        OnPropertyChanged(nameof(HasPreviewClips));
        OnPropertyChanged(nameof(HasPreviewMeshModules));
        SetPreviewStatus(statusKey, model);

        if (toast && !string.Equals(model, _lastPreviewErrorToast, StringComparison.OrdinalIgnoreCase))
        {
            _lastPreviewErrorToast = model;
            Notifications.ShowError(
                Localization.GetString("Tools.IniEditor.Preview3D.ParseErrorTitle"),
                Localization.GetString("Tools.IniEditor.Preview3D.ParseErrorMessage", model),
                NotificationDurations.Long);
        }
    }

    private void RebuildPreviewMeshList(W3dResolvedModel data, W3dRenderScene scene)
    {
        PreviewMeshes.Clear();
        var pivots = data.Model.Hierarchies.FirstOrDefault()?.Pivots;
        for (int i = 0; i < scene.Meshes.Count; i++)
        {
            var mesh = scene.Meshes[i];
            string? boneName = null;
            if (pivots != null && mesh.BoneIndex >= 0 && mesh.BoneIndex < pivots.Count)
            {
                boneName = pivots[mesh.BoneIndex].Name;
            }

            string? textureName = mesh.TextureIndex >= 0 && mesh.TextureIndex < scene.Textures.Count
                ? scene.Textures[mesh.TextureIndex].Name
                : null;
            PreviewMeshes.Add(new W3dPreviewMeshItem(i, mesh.Name, mesh.TriangleCount, mesh.VertexCount, boneName, textureName));
        }

        PreviewSelectedMeshIndex = -1;
    }

    private void RebuildPreviewClips(W3dResolvedModel data)
    {
        PreviewClips.Clear();
        foreach (var clip in data.Model.Animations)
        {
            PreviewClips.Add(clip);
        }

        OnPropertyChanged(nameof(HasPreviewClips));
        SelectedPreviewClip = PreviewClips.FirstOrDefault(clip => clip.IsSamplable) ?? PreviewClips.FirstOrDefault();
        PreviewFrameCount = SelectedPreviewClip == null ? 0 : Math.Max((int)SelectedPreviewClip.FrameCount - 1, 0);
    }

    private void SamplePreviewPose()
    {
        var clip = SelectedPreviewClip;
        var resolved = _previewResolved;
        if (clip == null || resolved == null || !clip.IsSamplable)
        {
            PreviewPose = null;
            PreviewBindPose = null;
            return;
        }

        var hierarchy = resolved.Model.Hierarchies.FirstOrDefault(h =>
                string.Equals(h.Name, clip.HierarchyName, StringComparison.OrdinalIgnoreCase))
            ?? resolved.Model.Hierarchies.FirstOrDefault();
        if (hierarchy == null)
        {
            PreviewPose = null;
            PreviewBindPose = null;
            return;
        }

        PreviewBindPose = W3dAnimationSampler.BindPoseWorlds(hierarchy);
        int frame = (int)Math.Round(PreviewFrame);
        PreviewPose = W3dAnimationSampler.SampleFrame(hierarchy, clip, frame);
    }

    private void StartPreviewPlayback()
    {
        StopPreviewPlayback();
        var clip = SelectedPreviewClip;
        if (clip == null || !clip.IsSamplable || PreviewFrameCount < 1)
        {
            IsPreviewPlaying = false;
            return;
        }

        double fps = clip.FrameRate == 0 ? 30 : clip.FrameRate;
        _previewPlaybackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1 / fps) };
        _previewPlaybackTimer.Tick += OnPreviewPlaybackTick;
        _previewPlaybackTimer.Start();
    }

    private void StopPreviewPlayback()
    {
        if (_previewPlaybackTimer != null)
        {
            _previewPlaybackTimer.Stop();
            _previewPlaybackTimer.Tick -= OnPreviewPlaybackTick;
            _previewPlaybackTimer = null;
        }
    }

    private void OnPreviewPlaybackTick(object? sender, EventArgs e)
    {
        if (PreviewFrameCount < 1)
        {
            return;
        }

        PreviewFrame = PreviewFrame >= PreviewFrameCount ? 0 : PreviewFrame + 1;
    }

    private void RebuildPreviewMeshModules()
    {
        PreviewMeshModules.Clear();
        var mesh = SelectedPreviewMesh;
        var node = EditableSelectedNode;
        if (mesh == null || node == null)
        {
            OnPropertyChanged(nameof(HasPreviewMeshModules));
            return;
        }

        string shortName = mesh.Name.Contains('.') ? mesh.Name[(mesh.Name.LastIndexOf('.') + 1)..] : mesh.Name;
        foreach (var draw in node.Children.Where(child => child.Block.BlockType.Contains("Draw", StringComparison.OrdinalIgnoreCase)))
        {
            CollectMeshModuleMatches(draw, mesh.Name, shortName);
        }

        OnPropertyChanged(nameof(HasPreviewMeshModules));
    }

    private void CollectMeshModuleMatches(IniTreeNodeViewModel node, string fullName, string shortName)
    {
        if (ModuleReferencesMesh(node.Block, fullName, shortName))
        {
            PreviewMeshModules.Add(node);
        }

        foreach (var child in node.Children)
        {
            CollectMeshModuleMatches(child, fullName, shortName);
        }
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Kept as an instance helper to satisfy member ordering.")]
    private bool ModuleReferencesMesh(IniBlock block, string fullName, string shortName)
    {
        foreach (var field in block.Fields)
        {
            if (!string.Equals(field.Key, IniConstants.FieldKeys.ShowSubObjects, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(field.Key, IniConstants.FieldKeys.HideSubObjects, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = field.Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (tokens.Any(token => string.Equals(token, fullName, StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(token, shortName, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private void SetPreviewStatus(string key, params object[] args)
    {
        _previewStatusKey = key;
        _previewStatusArgs = args;
        PreviewStatusText = args.Length == 0 ? Localization.GetString(key) : Localization.GetString(key, args);
    }

    private void RefreshPreviewStatusText()
    {
        if (!string.IsNullOrEmpty(_previewStatusKey))
        {
            PreviewStatusText = _previewStatusArgs.Length == 0
                ? Localization.GetString(_previewStatusKey)
                : Localization.GetString(_previewStatusKey, _previewStatusArgs);
        }
    }

    [RelayCommand]
    private void TogglePreviewPlayback()
    {
        IsPreviewPlaying = !IsPreviewPlaying;
    }

    [RelayCommand]
    private void GoToPreviewModule(IniTreeNodeViewModel? node)
    {
        if (node != null)
        {
            SelectModuleNode(node);
        }
    }

    partial void OnPreviewSelectedMeshIndexChanged(int value)
    {
        if (_isSyncingPreviewSelection)
        {
            return;
        }

        _isSyncingPreviewSelection = true;
        try
        {
            SelectedPreviewMesh = value >= 0 && value < PreviewMeshes.Count ? PreviewMeshes[value] : null;
        }
        finally
        {
            _isSyncingPreviewSelection = false;
        }

        RebuildPreviewMeshModules();
    }

    partial void OnSelectedPreviewMeshChanged(W3dPreviewMeshItem? value)
    {
        if (_isSyncingPreviewSelection)
        {
            return;
        }

        _isSyncingPreviewSelection = true;
        try
        {
            PreviewSelectedMeshIndex = value?.MeshIndex ?? -1;
        }
        finally
        {
            _isSyncingPreviewSelection = false;
        }

        RebuildPreviewMeshModules();
    }

    partial void OnSelectedPreviewClipChanged(W3dAnimationClip? value)
    {
        PreviewFrameCount = value == null ? 0 : Math.Max((int)value.FrameCount - 1, 0);
        PreviewFrame = 0;
        SamplePreviewPose();
        if (IsPreviewPlaying)
        {
            StartPreviewPlayback();
        }
    }

    partial void OnPreviewFrameChanged(double value)
    {
        SamplePreviewPose();
    }

    partial void OnIsPreviewPlayingChanged(bool value)
    {
        if (value)
        {
            StartPreviewPlayback();
        }
        else
        {
            StopPreviewPlayback();
        }
    }

    private void RebuildSelectedBlockAssets(IniBlock block, IniTreeNodeViewModel? node)
    {
        SelectedBlockAssets.Clear();
        var drawNode = node?.Children.FirstOrDefault(child =>
            child.Block.BlockType.Contains("Draw", StringComparison.OrdinalIgnoreCase));
        var model = ResolveBlockModel(block);
        if (!string.IsNullOrWhiteSpace(model))
        {
            SelectedBlockAssets.Add(new IniAssetLinkItem(
                model,
                Localization.GetString("Tools.IniEditor.Assets.ModelDetail"),
                "CubeOutline",
                null,
                drawNode ?? node));
        }

        foreach (var field in block.Fields.Where(field => IsTextureSuggestionKey(field.Key, null)))
        {
            var textureName = field.Value.Trim();
            if (string.IsNullOrEmpty(textureName))
            {
                continue;
            }

            _textureThumbnails.TryGetValue(textureName, out var thumbnail);
            SelectedBlockAssets.Add(new IniAssetLinkItem(
                textureName,
                Localization.GetString("Tools.IniEditor.Assets.TextureDetail", field.Key),
                "ImageOutline",
                thumbnail,
                node));
        }

        if (node != null)
        {
            foreach (var child in node.Children.Where(child => IsAssetModule(child.Block)))
            {
                SelectedBlockAssets.Add(new IniAssetLinkItem(
                    child.Block.AssignmentValue ?? child.ShortName,
                    child.Block.BlockType,
                    child.IconKind,
                    null,
                    child));
            }
        }

        HasSelectedBlockAssets = SelectedBlockAssets.Count > 0;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Block classifier kept as an instance helper to satisfy member ordering.")]
    private bool IsAssetModule(IniBlock block)
    {
        return block.BlockType.Contains("Draw", StringComparison.OrdinalIgnoreCase) ||
            block.BlockType.Contains("FX", StringComparison.OrdinalIgnoreCase) ||
            block.BlockType.Contains("Particle", StringComparison.OrdinalIgnoreCase) ||
            block.BlockType.Contains("Sound", StringComparison.OrdinalIgnoreCase) ||
            block.Fields.Any(field => string.Equals(field.Key, IniConstants.FieldKeys.Model, StringComparison.OrdinalIgnoreCase));
    }

    private void ApplyVisualObjectPortrait(IniBlock block)
    {
        var portraitName = (FindFieldValue(block, IniConstants.FieldKeys.SelectPortrait) ?? FindFieldValue(block, IniConstants.FieldKeys.ButtonImage))?.Trim();
        SelectedBlockPortrait = (!string.IsNullOrWhiteSpace(portraitName) && _textureThumbnails.TryGetValue(portraitName, out var thumb))
            ? thumb
            : null;
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Populates instance-bound visual object collections.")]
    private void ApplyVisualObjectLists(IniBlock block, IniTreeNodeViewModel? node)
    {
        SelectedBlockKindOfList.Clear();
        var kindOfStr = FindFieldValue(block, IniConstants.FieldKeys.KindOf);
        if (!string.IsNullOrWhiteSpace(kindOfStr))
        {
            foreach (var flag in kindOfStr.Split(" ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                SelectedBlockKindOfList.Add(flag);
            }
        }

        SelectedBlockModules.Clear();
        if (node != null)
        {
            foreach (var childNode in node.Children)
            {
                SelectedBlockModules.Add(childNode);
            }
        }
    }

    private void UpdateAvailableFieldKeys()
    {
        AvailableFieldKeys.Clear();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in IniConstants.FieldKeys.All)
        {
            keys.Add(key);
        }

        if (EditableSelectedNode?.Block != null)
        {
            var blockType = EditableSelectedNode.Block.BlockType;
            var blockSchema = schemaService.GetBlockSchema(blockType);
            if (blockSchema != null)
            {
                foreach (var field in blockSchema.Fields)
                {
                    keys.Add(field.Key);
                }
            }
        }

        if (_document != null)
        {
            foreach (var b in _document.Blocks)
            {
                foreach (var f in b.Fields)
                {
                    keys.Add(f.Key);
                }
            }
        }

        foreach (var key in keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            AvailableFieldKeys.Add(key);
        }
    }

    partial void OnRawPreviewTextChanged(string value)
    {
        if (_isUpdatingRawPreview || _isRebuilding || _document == null)
        {
            return;
        }

        var targetBlock = EditableSelectedNode?.Block;
        if (targetBlock == null)
        {
            return;
        }

        var scheduledRevision = _documentRevision;
        ScheduleDeferred(ref _rawEditCts, IniConstants.Editor.PreviewRefreshDebounceMs, () =>
        {
            if (scheduledRevision != _documentRevision)
            {
                return;
            }

            ApplyRawPreviewEdit(targetBlock, value);
        });
    }

    private void RefreshRawPreviewText()
    {
        if (_isUpdatingRawPreview)
        {
            return;
        }

        _isUpdatingRawPreview = true;
        try
        {
            if (EditableSelectedNode?.Block != null)
            {
                var tempDoc = new IniDocument();
                tempDoc.Blocks.Add(EditableSelectedNode.Block);
                RawPreviewText = iniDocumentService.WriteDocument(tempDoc);
            }
            else if (_document != null)
            {
                RawPreviewText = iniDocumentService.WriteDocument(_document);
            }
            else
            {
                RawPreviewText = string.Empty;
            }
        }
        finally
        {
            _isUpdatingRawPreview = false;
        }
    }

    private void ApplyRawPreviewEdit(IniBlock targetBlock, string text)
    {
        if (_document == null || SelectedNode?.Block != targetBlock)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            var emptyOldFields = targetBlock.Fields.ToList();
            var emptyOldChildren = targetBlock.Children.ToList();
            if (emptyOldFields.Count == 0 && emptyOldChildren.Count == 0)
            {
                return;
            }

            void ApplyEmpty()
            {
                targetBlock.Fields.Clear();
                targetBlock.Children.Clear();
                RebuildAll();
            }

            void RevertEmpty()
            {
                targetBlock.Fields.Clear();
                targetBlock.Fields.AddRange(emptyOldFields);
                targetBlock.Children.Clear();
                targetBlock.Children.AddRange(emptyOldChildren);
                RebuildAll();
            }

            // Invalidate the suggestion indexes before the synchronous rebuild inside
            // the apply; PushUndo bumps the revision again afterwards.
            _documentRevision++;
            ApplyEmpty();
            PushUndo(new IniEditAction(
                Title: Localization.GetString("Tools.IniEditor.History.ApplyRawPreview"),
                Redo: ApplyEmpty,
                Undo: RevertEmpty));
            MarkDirty();
            return;
        }

        var parsed = iniDocumentService.ParseText(text);
        if (!parsed.Success || parsed.Data == null || parsed.Data.Blocks.Count == 0 || parsed.Data.HasParseErrors)
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.Preview.RawEditRejectedTitle"),
                Localization.GetString("Tools.IniEditor.Preview.RawEditRejectedMessage"),
                NotificationDurations.Medium);
            return;
        }

        if (parsed.Data.Blocks.Count > 1)
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.Preview.RawEditRejectedTitle"),
                Localization.GetString("Tools.IniEditor.Preview.RawEditMultiBlockMessage"),
                NotificationDurations.Medium);
            return;
        }

        if (parsed.Data.GlobalFields.Count > 0)
        {
            Notifications.ShowWarning(
                Localization.GetString("Tools.IniEditor.Preview.RawEditRejectedTitle"),
                Localization.GetString("Tools.IniEditor.Preview.RawEditFileScopeMessage"),
                NotificationDurations.Medium);
            return;
        }

        var newBlock = parsed.Data.Blocks[0];
        var oldBlockType = targetBlock.BlockType;
        var oldName = targetBlock.Name;
        var oldFields = targetBlock.Fields.ToList();
        var oldChildren = targetBlock.Children.ToList();

        var newBlockType = newBlock.BlockType;
        var newName = newBlock.Name;
        var newFields = newBlock.Fields.ToList();
        var newChildren = newBlock.Children.ToList();

        void ApplyParsedState(string blockType, string name, List<IniField> fields, List<IniBlock> children)
        {
            targetBlock.BlockType = blockType;
            targetBlock.Name = name;
            targetBlock.Fields.Clear();
            targetBlock.Fields.AddRange(fields);
            targetBlock.Children.Clear();
            targetBlock.Children.AddRange(children);
            RebuildAll();
        }

        // Invalidate the suggestion indexes before the synchronous rebuild inside
        // the apply; PushUndo bumps the revision again afterwards.
        _documentRevision++;
        ApplyParsedState(newBlockType, newName, newFields, newChildren);
        PushUndo(new IniEditAction(
            Title: Localization.GetString("Tools.IniEditor.History.ApplyRawPreview"),
            Redo: () => ApplyParsedState(newBlockType, newName, newFields, newChildren),
            Undo: () => ApplyParsedState(oldBlockType, oldName, oldFields, oldChildren)));
        MarkDirty();
    }

    partial void OnBlockFilterChanged(string? value)
    {
        ScheduleDeferred(ref _filterCts, IniConstants.Editor.FilterDebounceMs, RebuildTree);
    }

    partial void OnIsGroupByTypeEnabledChanged(bool value)
    {
        RebuildVisibleRootNodes();
        RefreshSelectionDependents();
    }

    partial void OnReferenceFilterChanged(string? value)
    {
        FilterReferenceResults();
    }

    partial void OnReferenceTypeFilterChanged(string? value)
    {
        FilterReferenceResults();
    }

    partial void OnLeftSidebarTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBlocksTabSelected));
        OnPropertyChanged(nameof(IsFilesTabSelected));
        OnPropertyChanged(nameof(IsReferencesTabSelected));
        OnPropertyChanged(nameof(IsTexturesTabSelected));
    }

    partial void OnTextureSearchFilterChanged(string value)
    {
        ApplyTextureFilter();
    }

    private void ApplyTextureFilter()
    {
        FilteredTextureItems.Clear();
        var filter = TextureSearchFilter?.Trim();
        var items = string.IsNullOrEmpty(filter)
            ? (IEnumerable<IniTextureItemViewModel>)_allTextureItems
            : _allTextureItems.Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));

        foreach (var item in items)
        {
            FilteredTextureItems.Add(item);
        }

        TextureStatusText = Localization.GetString("Tools.IniEditor.Textures.StatusFormat", FilteredTextureItems.Count, _allTextureItems.Count);
    }

    partial void OnSelectedInstallationChanged(GameInstallationOption? value)
    {
        // Invalidate in-flight thumbnail loads and previews synchronously so a stale decode
        // cannot repopulate the cleared cache before the debounced refresh runs.
        _thumbnailGeneration++;
        _modelPreviewGeneration++;
        _lastPreviewErrorToast = null;
        _textureThumbnails.Clear();
        QueueThumbnailRefresh();
        modelResolver.ClearCache();
        _lastPreviewModel = null;
        QueueModelPreviewRefresh();
    }
}
