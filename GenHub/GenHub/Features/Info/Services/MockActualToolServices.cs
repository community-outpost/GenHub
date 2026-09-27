using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Interfaces.Tools.GenHotkeys;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Core.Models.Tools.ModBuilder;
using GenHub.Core.Models.Tools.WndEditor;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#pragma warning disable SA1649 // File name should match first type name
#pragma warning disable SA1402 // File may only contain a single type

namespace GenHub.Features.Info.Services;

/// <summary>
/// Mock localization service used when demos are constructed without a real one (e.g. headless tests).
/// Returns resource keys unchanged.
/// </summary>
public sealed class MockLocalizationService : ILocalizationService
{
    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <inheritdoc/>
    public IReadOnlyList<CultureInfo> AvailableCultures { get; } = [CultureInfo.InvariantCulture];

    /// <inheritdoc/>
    public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

    /// <inheritdoc/>
    public string this[string key] => key;

    /// <inheritdoc/>
    public string GetString(string key, params object?[] arguments) => key;

    /// <inheritdoc/>
    public bool TryGetString(string key, [NotNullWhen(true)] out string? result, params object?[] arguments)
    {
        result = null;
        return false;
    }

    /// <inheritdoc/>
    public OperationResult SetCulture(CultureInfo culture) => OperationResult.CreateSuccess();

    /// <summary>
    /// Raises <see cref="PropertyChanged"/> for tests.
    /// </summary>
    /// <param name="propertyName">The changed property name.</param>
    public void RaiseCultureChanged(string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>
/// Mock dialog service for demos. Always declines confirmations and returns no selection.
/// </summary>
public sealed class MockDialogService : IDialogService
{
    /// <inheritdoc/>
    public Task<bool> ShowConfirmationAsync(string title, string message, string confirmText = "Confirm", string cancelText = "Cancel", string? sessionKey = null) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<(DialogAction? Action, bool DoNotAskAgain)> ShowMessageAsync(string title, string content, IEnumerable<DialogAction> actions, bool showDoNotAskAgain = false) => Task.FromResult<(DialogAction? Action, bool DoNotAskAgain)>((null, false));

    /// <inheritdoc/>
    public Task<UpdateDialogResult?> ShowUpdateOptionDialogAsync(string title, string message, bool initialDeleteOldVersions) => Task.FromResult<UpdateDialogResult?>(null);
}

/// <summary>
/// Mock game installation service for demos. Reports no detected installations.
/// </summary>
public sealed class MockGameInstallationService : IGameInstallationService
{
    /// <inheritdoc/>
    public IReadOnlyList<GameInstallation>? CachedInstallations => [];

    /// <inheritdoc/>
    public Task<OperationResult<GameInstallation>> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<GameInstallation>.CreateFailure("No game installations in demo mode."));

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<GameInstallation>>> GetAllInstallationsAsync(CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess([]));

    /// <inheritdoc/>
    public void InvalidateCache()
    {
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> AddInstallationToCacheAsync(GameInstallation installation, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task CreateAndRegisterInstallationManifestsAsync(GameInstallation installation, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<OperationResult<GameInstallation>> RegisterCustomInstallationAsync(string directoryPath, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<GameInstallation>.CreateFailure("Custom installations are disabled in demo mode."));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> RemoveCustomInstallationAsync(string installationIdOrPath, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<bool>.CreateSuccess(false));
}

/// <summary>
/// In-memory hotkey profile storage for demos, seeded with one sample profile.
/// </summary>
public sealed class MockHotkeyProfileStorageService : IHotkeyProfileStorageService
{
    private readonly Dictionary<string, HotkeyProfile> _profiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="MockHotkeyProfileStorageService"/> class.
    /// </summary>
    public MockHotkeyProfileStorageService()
    {
        var sample = new HotkeyProfile
        {
            Id = "demo-hotkey-profile",
            Name = "Demo Hotkeys",
            TargetGame = GameType.ZeroHour,
            BasePreset = "Vanilla",
            KeyMappings = new Dictionary<string, char>(StringComparer.OrdinalIgnoreCase)
            {
                ["CONTROLBAR:Command_ConstructAmericaVehicleDozer"] = 'Q',
                ["CONTROLBAR:Command_ConstructAmericaInfantryRanger"] = 'W',
                ["CONTROLBAR:Command_ConstructAmericaVehicleCrusader"] = 'E',
            },
        };
        _profiles[sample.Id] = sample;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<HotkeyProfile>> GetProfilesAsync(GameType gameType, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HotkeyProfile> result = _profiles.Values.Where(p => p.TargetGame == gameType).ToList();
        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<HotkeyProfile?> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        _profiles.TryGetValue(profileId, out var profile);
        return Task.FromResult(profile);
    }

    /// <inheritdoc/>
    public Task<HotkeyProfile> SaveProfileAsync(HotkeyProfile profile, CancellationToken cancellationToken = default)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        _profiles[profile.Id] = profile;
        return Task.FromResult(profile);
    }

    /// <inheritdoc/>
    public Task<bool> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default) => Task.FromResult(_profiles.Remove(profileId));

    /// <inheritdoc/>
    public Task<HotkeyProfile> LoadPresetAsync(string presetName, GameType gameType, CancellationToken cancellationToken = default)
    {
        var profile = new HotkeyProfile
        {
            Name = $"{presetName} (Demo)",
            TargetGame = gameType,
            BasePreset = presetName,
        };
        return Task.FromResult(profile);
    }
}

/// <summary>
/// Mock hotkey packaging service for demos. Packaging is disabled in the interactive guide.
/// </summary>
public sealed class MockHotkeyPackageService : IHotkeyPackageService
{
    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> CreateHotkeysAddonAsync(HotkeyProfile profile, IProgress<string>? progress = null, string? existingManifestId = null, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<ContentManifest>.CreateFailure("Addon packaging is disabled in the interactive guide."));
}

/// <summary>
/// Mock WND image asset service for demos. Resolves no game art.
/// </summary>
public sealed class MockWndImageAssetService : IWndImageAssetService
{
    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyDictionary<string, byte[]>>> GetImagesAsync(IReadOnlyCollection<string> mappedImageNames, string baseRoot, string? overrideRoot, string? projectDirectory, IReadOnlyCollection<string>? additionalBigFiles = null, bool isZeroHour = false, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(new Dictionary<string, byte[]>()));

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<string>>> GetKnownImageNamesAsync(string baseRoot, string? overrideRoot, string? projectDirectory, IReadOnlyCollection<string>? additionalBigFiles = null, bool isZeroHour = false, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<IReadOnlyList<string>>.CreateSuccess([]));

    /// <inheritdoc/>
    public void InvalidateCache()
    {
    }
}

/// <summary>
/// Mock WND string table service for demos. Resolves no labels.
/// </summary>
public sealed class MockWndStringTableService : IWndStringTableService
{
    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyDictionary<string, string>>> GetStringsAsync(IReadOnlyCollection<string> labels, string baseRoot, string? overrideRoot, string? projectDirectory, IReadOnlyCollection<string>? additionalBigFiles = null, bool isZeroHour = false, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<IReadOnlyDictionary<string, string>>.CreateSuccess(new Dictionary<string, string>()));

    /// <inheritdoc/>
    public void InvalidateCache()
    {
    }
}

/// <summary>
/// Mock aggregate WND asset service for demos.
/// </summary>
public sealed class MockWndEditorAssetService : IWndEditorAssetService
{
    /// <inheritdoc/>
    public IWndImageAssetService Images { get; } = new MockWndImageAssetService();

    /// <inheritdoc/>
    public IWndStringTableService Strings { get; } = new MockWndStringTableService();

    /// <inheritdoc/>
    public void InvalidateCache()
    {
        Images.InvalidateCache();
        Strings.InvalidateCache();
    }
}

/// <summary>
/// Mock WND texture import service for demos. Imports are disabled in the interactive guide.
/// </summary>
public sealed class MockWndTextureImportService : IWndTextureImportService
{
    /// <inheritdoc/>
    public Task<OperationResult<WndTextureImportResult>> ImportTextureAsync(string sourceFilePath, string projectDirectory, string? mappedName = null, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<WndTextureImportResult>.CreateFailure("Texture import is disabled in the interactive guide."));

    /// <inheritdoc/>
    public Task<OperationResult<WndTextureImportResult>> ImportTextureFromBytesAsync(byte[] imageBytes, string projectDirectory, string mappedName, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<WndTextureImportResult>.CreateFailure("Texture import is disabled in the interactive guide."));
}

/// <summary>
/// Mock challenge medal service for demos. Resolves no medallions.
/// </summary>
public sealed class MockChallengeMedalService : IChallengeMedalService
{
    /// <inheritdoc/>
    public Task<OperationResult<ChallengeMedals>> GetMedalsAsync(string baseRoot, string? overrideRoot, string? projectDirectory, IReadOnlyCollection<string>? additionalBigFiles = null, bool isZeroHour = false, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<ChallengeMedals>.CreateSuccess(ChallengeMedals.Empty));

    /// <inheritdoc/>
    public void InvalidateCache()
    {
    }
}

/// <summary>
/// Mock ModBuilder engine for demos. Simulates an instant successful build.
/// </summary>
public sealed class MockBuildEngineService : IBuildEngineService
{
    /// <inheritdoc/>
    public Task<BuildOperationResult> ExecuteBuildAsync(ModBuilderProject project, BuildConfiguration configuration, List<string> selectedBundlePacks, BuildStep buildSteps, IProgress<BuildProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(new BuildProgress { CurrentStep = "Demo build", PercentComplete = 100, Percentage = 100, ProcessedFiles = 12, TotalFiles = 12 });
        return Task.FromResult(BuildOperationResult.CreateSuccess(filesProcessed: 12));
    }

    /// <inheritdoc/>
    public Task<bool> CanAbortAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task AbortAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc/>
    public void InvalidateBuildStructureCache()
    {
    }
}

/// <summary>
/// In-memory ModBuilder project service for demos. Nothing touches disk.
/// </summary>
public sealed class MockProjectConfigService : IProjectConfigService
{
    private readonly Dictionary<string, ModBuilderProject> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _recent = [];

    /// <inheritdoc/>
    public Task<ProjectOperationResult<ModBuilderProject>> CreateProjectAsync(string projectPath, string projectName, string? gameInstallationId = null, ProjectTemplate? template = null, ContentType contentType = ContentType.Mod, CancellationToken cancellationToken = default)
    {
        var project = new ModBuilderProject
        {
            Name = projectName,
            ProjectDir = Path.GetDirectoryName(projectPath) ?? string.Empty,
            ContentType = contentType,
        };
        _projects[projectPath] = project;
        TrackRecent(projectPath);
        return Task.FromResult(ProjectOperationResult<ModBuilderProject>.CreateSuccess(project));
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult<ModBuilderProject>> LoadProjectAsync(string projectPath, bool validateIntegrity = true, CancellationToken cancellationToken = default)
    {
        if (_projects.TryGetValue(projectPath, out var project))
        {
            return Task.FromResult(ProjectOperationResult<ModBuilderProject>.CreateSuccess(project));
        }

        return Task.FromResult(ProjectOperationResult<ModBuilderProject>.CreateFailure($"Project not found in demo storage: {projectPath}"));
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult<ModBuilderProject>> SaveProjectAsync(string projectPath, ModBuilderProject project, CancellationToken cancellationToken = default)
    {
        _projects[projectPath] = project;
        TrackRecent(projectPath);
        return Task.FromResult(ProjectOperationResult<ModBuilderProject>.CreateSuccess(project));
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult<bool>> ValidateProjectAsync(string projectPath, ModBuilderProject project, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<List<string>>> GetRecentProjectsAsync(int maxCount = 10, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<List<string>>.CreateSuccess(_recent.Take(maxCount).ToList()));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<bool>> AddToRecentProjectsAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        TrackRecent(projectPath);
        return Task.FromResult(ProjectOperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<ProjectOperationResult<bool>> RemoveFromRecentProjectsAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<bool>.CreateSuccess(_recent.Remove(projectPath)));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<List<string>>> GetBundleConfigsAsync(string projectPath, ModBuilderProject project, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<List<string>>.CreateSuccess([]));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<bool>> UpdateLastBuildTimeAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<int>> ImportBigFilesAsync(string projectPath, IEnumerable<string> bigFilePaths, bool createBundlePackForBig = true, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<int>.CreateSuccess(0));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<ModBuilderProject>> CreateProjectFromBigFilesAsync(string projectPath, string projectName, IEnumerable<string> bigFilePaths, string? gameInstallationId = null, ContentType contentType = ContentType.Mod, IProgress<double>? progress = null, CancellationToken cancellationToken = default) => CreateProjectAsync(projectPath, projectName, gameInstallationId, null, contentType, cancellationToken);

    private void TrackRecent(string projectPath)
    {
        _recent.Remove(projectPath);
        _recent.Insert(0, projectPath);
    }
}

/// <summary>
/// Mock ModBuilder configuration loader for demos. Returns empty default configurations.
/// </summary>
public sealed class MockConfigurationLoaderService : IConfigurationLoaderService
{
    /// <inheritdoc/>
    public Task<ProjectOperationResult<BuildConfiguration>> LoadConfigurationResultAsync(string configPath, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<BuildConfiguration>.CreateSuccess(new BuildConfiguration()));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<BuildConfiguration>> LoadAndMergeConfigurationsResultAsync(IReadOnlyList<string> configPaths, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<BuildConfiguration>.CreateSuccess(new BuildConfiguration()));

    /// <inheritdoc/>
    public Task<BuildConfiguration> ResolveWildcardsAsync(BuildConfiguration configuration, CancellationToken cancellationToken = default) => Task.FromResult(configuration);

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration(BuildConfiguration configuration) => [];

    /// <inheritdoc/>
    public Task<BuildConfiguration> LoadDefaultConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult(new BuildConfiguration());

    /// <inheritdoc/>
    public BuildConfiguration MergeConfigurations(BuildConfiguration baseConfig, BuildConfiguration overrideConfig) => overrideConfig;

    /// <inheritdoc/>
    public void NormalizePaths(BuildConfiguration configuration)
    {
    }

    /// <inheritdoc/>
    public Task<BuildConfiguration?> LoadProjectConfigurationAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult<BuildConfiguration?>(new BuildConfiguration());
}

/// <summary>
/// Mock ModBuilder structure generator for demos. Creates nothing on disk.
/// </summary>
public sealed class MockProjectStructureGenerator : IProjectStructureGenerator
{
    /// <inheritdoc/>
    public Task GenerateProjectStructureAsync(string projectPath, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// In-memory Publisher Studio service for demos. Nothing touches disk.
/// </summary>
public sealed class MockPublisherStudioService : IPublisherStudioService
{
    private readonly Dictionary<string, PublisherStudioProject> _projects = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<OperationResult<PublisherStudioProject>> CreateProjectAsync(string name, CancellationToken cancellationToken = default)
    {
        var project = new PublisherStudioProject { ProjectName = name };
        _projects[name] = project;
        return Task.FromResult(OperationResult<PublisherStudioProject>.CreateSuccess(project));
    }

    /// <inheritdoc/>
    public Task<OperationResult<PublisherStudioProject>> LoadProjectAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_projects.TryGetValue(path, out var project))
        {
            return Task.FromResult(OperationResult<PublisherStudioProject>.CreateSuccess(project));
        }

        return Task.FromResult(OperationResult<PublisherStudioProject>.CreateFailure($"Project not found in demo storage: {path}"));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> SaveProjectAsync(PublisherStudioProject project, CancellationToken cancellationToken = default)
    {
        project.IsDirty = false;
        project.LastModified = DateTime.UtcNow;
        _projects[project.ProjectName] = project;
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<string>> ExportCatalogAsync(PublisherStudioProject project, NamedCatalog? catalog = null, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<string>.CreateSuccess("{}"));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> ValidateCatalogAsync(PublisherCatalog catalog, bool allowPendingArtifacts = false, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public string GenerateSubscriptionUrl(string catalogUrl) => $"genhub://subscribe?url={Uri.EscapeDataString(catalogUrl)}";

    /// <inheritdoc/>
    public Task<OperationResult<string>> ExportProviderDefinitionAsync(PublisherStudioProject project, Dictionary<string, string> catalogHostingInfo, string definitionUrl, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<string>.CreateSuccess("{}"));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> ValidateArtifactUrlsAsync(PublisherCatalog catalog, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult<bool>.CreateSuccess(true));
}

/// <summary>
/// Mock Publisher Studio dialog service for demos. Every prompt is cancelled.
/// </summary>
public sealed class MockPublisherStudioDialogService : IPublisherStudioDialogService
{
    /// <inheritdoc/>
    public Func<string, (string Name, string Url, long Size)?>? DuplicateAssetLookup { get; set; }

    /// <inheritdoc/>
    public Task<bool> ShowConfirmationAsync(string title, string message, string? confirmText = null, string? cancelText = null, string? sessionKey = null) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<bool> ShowSetupWizardAsync(PublisherStudioProject project) => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<bool> ShowHostingSettingsDialogAsync() => Task.FromResult(false);

    /// <inheritdoc/>
    public Task<CatalogContentItem?> ShowAddContentDialogAsync(string? initialPath = null, PublisherCatalog? catalog = null) => Task.FromResult<CatalogContentItem?>(null);

    /// <inheritdoc/>
    public Task<CatalogContentItem?> ShowAddContentDialogAsync(IEnumerable<string>? initialPaths = null, PublisherCatalog? catalog = null) => Task.FromResult<CatalogContentItem?>(null);

    /// <inheritdoc/>
    public Task<CatalogContentItem?> ShowEditContentDialogAsync(CatalogContentItem existing, PublisherCatalog? catalog = null, Func<CatalogContentItem, Task>? onDelete = null) => Task.FromResult<CatalogContentItem?>(null);

    /// <inheritdoc/>
    public Task<ContentRelease?> ShowAddReleaseDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog, IEnumerable<string>? initialPaths = null) => Task.FromResult<ContentRelease?>(null);

    /// <inheritdoc/>
    public Task<ContentRelease?> ShowEditReleaseDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog, Func<ContentRelease, Task>? onDelete = null) => Task.FromResult<ContentRelease?>(null);

    /// <inheritdoc/>
    public Task<ContentRelease?> ShowAddAddonDialogAsync(CatalogContentItem contentItem, PublisherCatalog catalog, IEnumerable<string>? initialPaths = null) => Task.FromResult<ContentRelease?>(null);

    /// <inheritdoc/>
    public Task<ContentRelease?> ShowEditAddonDialogAsync(ContentRelease existing, CatalogContentItem parent, PublisherCatalog catalog, Func<ContentRelease, Task>? onDelete = null) => Task.FromResult<ContentRelease?>(null);

    /// <inheritdoc/>
    public Task<ReleaseArtifact?> ShowAddArtifactDialogAsync() => Task.FromResult<ReleaseArtifact?>(null);

    /// <inheritdoc/>
    public Task<CatalogDependency?> ShowAddDependencyDialogAsync(PublisherCatalog catalog, CatalogContentItem currentContent) => Task.FromResult<CatalogDependency?>(null);

    /// <inheritdoc/>
    public Task<PublisherReferral?> ShowAddReferralDialogAsync() => Task.FromResult<PublisherReferral?>(null);

    /// <inheritdoc/>
    public Task<string?> ShowProjectOpenPromptAsync(string title) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<string?> ShowProjectSavePromptAsync(string title) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<string?> ShowCatalogFilePickerAsync(string title) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<string?> ShowFilePickerAsync(string title) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ShowFilesPickerAsync(string title) => Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc/>
    public Task<string?> ShowImagePickerAsync(string title) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ShowImageFilesPickerAsync(string title) => Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> ShowVideoFilesPickerAsync(string title) => Task.FromResult<IReadOnlyList<string>>([]);

    /// <inheritdoc/>
    public Task<string?> ShowFolderPickerAsync(string title) => Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public Task<RenameCatalogResult?> ShowRenameCatalogDialogAsync(string currentName, bool canDelete = false, Func<Task<bool>>? onDelete = null, string? currentIconUrl = null, Func<string, Task<string?>>? onUploadImage = null) => Task.FromResult<RenameCatalogResult?>(null);
}
