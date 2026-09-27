using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Providers;
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
using GenHub.Features.Tools.Services.Hosting;
using GenHub.Features.Tools.ViewModels.Dialogs;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
/// Mock game installation service for the WND editor demo. Reports one sample Generals install
/// so the asset preview pipeline can run without touching disk.
/// </summary>
public sealed class MockWndGameInstallationService : IGameInstallationService
{
    private readonly GameInstallation _sample = new("demo-game-install", GameInstallationType.Steam)
    {
        HasGenerals = true,
        GeneralsPath = "demo-game-install",
    };

    /// <inheritdoc/>
    public IReadOnlyList<GameInstallation>? CachedInstallations => [_sample];

    /// <inheritdoc/>
    public Task<OperationResult<GameInstallation>> GetInstallationAsync(string installationId, CancellationToken cancellationToken = default) => Task.FromResult(
        string.Equals(installationId, _sample.Id, StringComparison.OrdinalIgnoreCase)
            ? OperationResult<GameInstallation>.CreateSuccess(_sample)
            : OperationResult<GameInstallation>.CreateFailure("Installation not found in demo mode."));

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<GameInstallation>>> GetAllInstallationsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GameInstallation> installations = [_sample];
        return Task.FromResult(OperationResult<IReadOnlyList<GameInstallation>>.CreateSuccess(installations));
    }

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
/// Mock WND image asset service for demos. Generates deterministic placeholder art per image name.
/// </summary>
public sealed class MockWndImageAssetService : IWndImageAssetService
{
    private static readonly IReadOnlyList<string> KnownNames =
    [
        "MenuBackdrop",
        "MenuButtonLeft",
        "MenuButtonMiddle",
        "MenuButtonRight",
        "MenuTitle",
        "MenuDivider",
        "CheckboxOn",
        "CheckboxOff",
        "RadioOn",
        "RadioOff",
    ];

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyDictionary<string, byte[]>>> GetImagesAsync(IReadOnlyCollection<string> mappedImageNames, string baseRoot, string? overrideRoot, string? projectDirectory, IReadOnlyCollection<string>? additionalBigFiles = null, bool isZeroHour = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var images = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in mappedImageNames.Where(candidate => !string.IsNullOrWhiteSpace(candidate)))
        {
            images[name] = CreatePlaceholderBmp(name);
        }

        IReadOnlyDictionary<string, byte[]> result = images;
        return Task.FromResult(OperationResult<IReadOnlyDictionary<string, byte[]>>.CreateSuccess(result));
    }

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<string>>> GetKnownImageNamesAsync(string baseRoot, string? overrideRoot, string? projectDirectory, IReadOnlyCollection<string>? additionalBigFiles = null, bool isZeroHour = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<IReadOnlyList<string>>.CreateSuccess(KnownNames));
    }

    /// <inheritdoc/>
    public void InvalidateCache()
    {
    }

    private static byte[] CreatePlaceholderBmp(string name)
    {
        const int size = 96;
        const int bytesPerPixel = 3;
        var rowSize = size * bytesPerPixel;
        var pixels = new byte[rowSize * size];
        var hash = StableHash(name);
        var baseR = (byte)(40 + (hash % 60));
        var baseG = (byte)(50 + ((hash >> 8) % 60));
        var baseB = (byte)(90 + ((hash >> 16) % 80));
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                var border = x < 4 || y < 4 || x >= size - 4 || y >= size - 4;
                var offset = (y * rowSize) + (x * bytesPerPixel);
                pixels[offset] = border ? (byte)Math.Min(255, baseB + 60) : baseB;
                pixels[offset + 1] = border ? (byte)Math.Min(255, baseG + 60) : baseG;
                pixels[offset + 2] = border ? (byte)Math.Min(255, baseR + 60) : baseR;
            }
        }

        var header = new byte[54];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BitConverter.GetBytes(header.Length + pixels.Length).CopyTo(header, 2);
        BitConverter.GetBytes(header.Length).CopyTo(header, 10);
        BitConverter.GetBytes(40).CopyTo(header, 14);
        BitConverter.GetBytes(size).CopyTo(header, 18);
        BitConverter.GetBytes(size).CopyTo(header, 22);
        BitConverter.GetBytes((short)1).CopyTo(header, 26);
        BitConverter.GetBytes((short)24).CopyTo(header, 28);
        var bytes = new byte[header.Length + pixels.Length];
        Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
        Buffer.BlockCopy(pixels, 0, bytes, header.Length, pixels.Length);
        return bytes;
    }

    private static uint StableHash(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        return hash;
    }
}

/// <summary>
/// Mock WND string table service for demos. Resolves the labels used by the sample main menu.
/// </summary>
public sealed class MockWndStringTableService : IWndStringTableService
{
    private static readonly IReadOnlyDictionary<string, string> SampleStrings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["GAMETEXT:Menu_Title"] = "Command & Conquer: Generals",
        ["GAMETEXT:GUI_SinglePlayer"] = "Single Player",
        ["GAMETEXT:GUI_Multiplayer"] = "Multiplayer",
        ["GAMETEXT:GUI_Options"] = "Options",
        ["GAMETEXT:GUI_Exit"] = "Exit",
        ["GAMETEXT:GUI_Back"] = "Back",
        ["GAMETEXT:GUI_Fullscreen"] = "Fullscreen",
    };

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyDictionary<string, string>>> GetStringsAsync(IReadOnlyCollection<string> labels, string baseRoot, string? overrideRoot, string? projectDirectory, IReadOnlyCollection<string>? additionalBigFiles = null, bool isZeroHour = false, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var label in labels)
        {
            if (!string.IsNullOrWhiteSpace(label) && SampleStrings.TryGetValue(label, out var text))
            {
                resolved[label] = text;
            }
        }

        IReadOnlyDictionary<string, string> result = resolved;
        return Task.FromResult(OperationResult<IReadOnlyDictionary<string, string>>.CreateSuccess(result));
    }

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
/// Mock ModBuilder configuration loader for demos. Returns an in-memory sample configuration.
/// </summary>
public sealed class MockConfigurationLoaderService : IConfigurationLoaderService
{
    /// <inheritdoc/>
    public Task<ProjectOperationResult<BuildConfiguration>> LoadConfigurationResultAsync(string configPath, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<BuildConfiguration>.CreateSuccess(CreateSampleConfiguration()));

    /// <inheritdoc/>
    public Task<ProjectOperationResult<BuildConfiguration>> LoadAndMergeConfigurationsResultAsync(IReadOnlyList<string> configPaths, CancellationToken cancellationToken = default) => Task.FromResult(ProjectOperationResult<BuildConfiguration>.CreateSuccess(CreateSampleConfiguration()));

    /// <inheritdoc/>
    public Task<BuildConfiguration> ResolveWildcardsAsync(BuildConfiguration configuration, CancellationToken cancellationToken = default) => Task.FromResult(configuration);

    /// <inheritdoc/>
    public IReadOnlyList<string> ValidateConfiguration(BuildConfiguration configuration) => [];

    /// <inheritdoc/>
    public Task<BuildConfiguration> LoadDefaultConfigurationAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateSampleConfiguration());

    /// <inheritdoc/>
    public BuildConfiguration MergeConfigurations(BuildConfiguration baseConfig, BuildConfiguration overrideConfig) => overrideConfig;

    /// <inheritdoc/>
    public void NormalizePaths(BuildConfiguration configuration)
    {
    }

    /// <inheritdoc/>
    public Task<BuildConfiguration?> LoadProjectConfigurationAsync(string projectPath, CancellationToken cancellationToken = default) => Task.FromResult<BuildConfiguration?>(CreateSampleConfiguration());

    private static BuildConfiguration CreateSampleConfiguration() => new()
    {
        Packs =
        [
            new BundlePack { Name = "Core Assets", ItemNames = ["Textures", "Audio", "INI Files"], AllowBuild = true, Description = "Base game assets shared by every variant." },
            new BundlePack { Name = "Maps Pack", ItemNames = ["Skirmish Maps", "Challenge Maps"], AllowBuild = true, Description = "Bundled skirmish and challenge maps." },
            new BundlePack { Name = "Movies Archive", ItemNames = ["Intro Movies"], AllowBuild = false, Big = true, OutputFile = "Movies.big", Description = "Optional high-resolution movie pack." },
        ],
        Items =
        [
            new GenHub.Core.Models.Tools.ModBuilder.BundleItem { Name = "Textures", TargetDir = "Art/Textures" },
            new GenHub.Core.Models.Tools.ModBuilder.BundleItem { Name = "Audio", TargetDir = "Audio" },
            new GenHub.Core.Models.Tools.ModBuilder.BundleItem { Name = "Skirmish Maps", TargetDir = "Maps" },
        ],
        Manifests =
        [
            new BundleManifest { Name = "Demo Mod", Version = "1.0.0", Description = "Sample manifest for the interactive guide.", PackNames = ["Core Assets", "Maps Pack"] },
        ],
        LoadedConfigFiles = ["configs/build.json", "configs/bundles.json"],
        ZipCompressionLevel = CompressionLevel.Optimal,
    };
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

/// <summary>
/// Mock hosting provider for demos. Pre-authenticated; uploads resolve to in-memory demo URLs.
/// Nothing leaves the machine.
/// </summary>
public sealed class MockHostingProvider : IHostingProvider
{
    /// <summary>
    /// Gets the provider ID of the demo hosting provider.
    /// </summary>
    public const string DemoProviderId = "demo-hosting";

    private const string DemoHost = "https://demo.genhub.local/hosting";

    /// <inheritdoc/>
    public string ProviderId => DemoProviderId;

    /// <inheritdoc/>
    public string DisplayName => "Demo Hosting";

    /// <inheritdoc/>
    public string Description => "In-memory hosting for the interactive guide.";

    /// <inheritdoc/>
    public string IconName => "CloudOutline";

    /// <inheritdoc/>
    public bool RequiresAuthentication => false;

    /// <inheritdoc/>
    public bool IsAuthenticated { get; private set; } = true;

    /// <inheritdoc/>
    public bool SupportsCatalogHosting => true;

    /// <inheritdoc/>
    public bool SupportsArtifactHosting => true;

    /// <inheritdoc/>
    public bool SupportsUpdate => true;

    /// <inheritdoc/>
    public Task<OperationResult<bool>> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IsAuthenticated = true;
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task SignOutAsync()
    {
        IsAuthenticated = false;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UploadFileAsync(Stream fileStream, string fileName, string? folderPath = null, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(0);
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);
        progress?.Report(100);
        return OperationResult<HostingUploadResult>.CreateSuccess(BuildResult(fileName, buffer.ToArray()));
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingUploadResult>> UploadCatalogAsync(string catalogJson, string publisherId, string? catalogFileName = null, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(0);
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes(catalogJson);
        progress?.Report(100);
        return Task.FromResult(OperationResult<HostingUploadResult>.CreateSuccess(BuildResult(catalogFileName ?? $"catalog-{publisherId}.json", bytes)));
    }

    /// <inheritdoc/>
    public async Task<OperationResult<HostingUploadResult>> UpdateFileAsync(string fileId, Stream fileStream, string fileName, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        progress?.Report(0);
        using var buffer = new MemoryStream();
        await fileStream.CopyToAsync(buffer, cancellationToken);
        progress?.Report(100);
        var result = BuildResult(fileName, buffer.ToArray());
        result.FileId = fileId;
        return OperationResult<HostingUploadResult>.CreateSuccess(result);
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> DeleteFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<string>> GetOrCreatePublisherFolderAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<string>.CreateSuccess("demo-publisher-folder"));
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingState?>> RecoverHostingStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<HostingState?>.CreateSuccess(null));
    }

    /// <inheritdoc/>
    public string GetSubscriptionLink(string catalogUrl) => $"genhub://subscribe?url={Uri.EscapeDataString(catalogUrl)}";

    /// <inheritdoc/>
    public bool IsValidHostingUrl(string? url) => !string.IsNullOrWhiteSpace(url) && url.StartsWith(DemoHost, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public string GetDirectDownloadUrl(string shareUrl) => shareUrl;

    private static HostingUploadResult BuildResult(string fileName, byte[] bytes)
    {
        var url = $"{DemoHost}/{Uri.EscapeDataString(fileName)}";
        return new HostingUploadResult
        {
            PublicUrl = url,
            DirectDownloadUrl = url,
            FileId = fileName,
            FileSize = bytes.Length,
            Sha256Hash = Convert.ToHexString(SHA256.HashData(bytes)),
        };
    }
}

/// <summary>
/// Mock hosting provider factory for demos. Exposes the single demo hosting provider.
/// </summary>
public sealed class MockHostingProviderFactory : IHostingProviderFactory
{
    private readonly MockHostingProvider _provider = new();

    /// <inheritdoc/>
    public IReadOnlyList<IHostingProvider> GetAllProviders() => [_provider];

    /// <inheritdoc/>
    public IHostingProvider? GetProvider(string providerId) =>
        string.Equals(providerId, MockHostingProvider.DemoProviderId, StringComparison.OrdinalIgnoreCase) ? _provider : null;

    /// <inheritdoc/>
    public IReadOnlyList<IHostingProvider> GetCatalogHostingProviders() => [_provider];

    /// <inheritdoc/>
    public IReadOnlyList<IHostingProvider> GetArtifactHostingProviders() => [_provider];
}

/// <summary>
/// Mock hosting state manager for demos. Persists state in memory only.
/// </summary>
public sealed class MockHostingStateManager : IHostingStateManager
{
    private readonly Dictionary<string, PublisherHostingStates> _states = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public string GetStateFilePath(string projectPath) => projectPath + ".hosting_state.json";

    /// <inheritdoc/>
    public bool StateFileExists(string projectPath) => _states.ContainsKey(projectPath);

    /// <inheritdoc/>
    public Task<OperationResult<PublisherHostingStates>> LoadStatesAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var states = _states.TryGetValue(projectPath, out var stored)
            ? stored
            : new PublisherHostingStates();
        return Task.FromResult(OperationResult<PublisherHostingStates>.CreateSuccess(states));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> SaveStatesAsync(string projectPath, PublisherHostingStates states, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _states[projectPath] = states;
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<HostingState?>> LoadStateAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _states.TryGetValue(projectPath, out var states);
        var state = states?.States.Values.FirstOrDefault();
        return Task.FromResult(OperationResult<HostingState?>.CreateSuccess(state));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> SaveStateAsync(string projectPath, HostingState state, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_states.TryGetValue(projectPath, out var states))
        {
            states = new PublisherHostingStates();
            _states[projectPath] = states;
        }

        states.States[state.ProviderId] = state;
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }
}

/// <summary>
/// Mock hosting credential store for demos. Keeps credentials in memory only.
/// </summary>
public sealed class MockHostingCredentialStore : IHostingCredentialStore
{
    private readonly Dictionary<string, string> _credentials = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task SaveCredentialAsync(string providerId, string credential, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _credentials[providerId] = credential;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<string?> GetCredentialAsync(string providerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _credentials.TryGetValue(providerId, out var credential);
        return Task.FromResult(credential);
    }

    /// <inheritdoc/>
    public Task DeleteCredentialAsync(string providerId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _credentials.Remove(providerId);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Mock publisher subscription store for demos. Keeps subscriptions in memory only.
/// </summary>
public sealed class MockPublisherSubscriptionStore : IPublisherSubscriptionStore
{
    private readonly Dictionary<string, PublisherSubscription> _subscriptions = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<OperationResult<IReadOnlyList<PublisherSubscription>>> GetSubscriptionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<PublisherSubscription> subscriptions = [.. _subscriptions.Values];
        return Task.FromResult(OperationResult<IReadOnlyList<PublisherSubscription>>.CreateSuccess(subscriptions));
    }

    /// <inheritdoc/>
    public Task<OperationResult<PublisherSubscription?>> GetSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _subscriptions.TryGetValue(publisherId, out var subscription);
        return Task.FromResult(OperationResult<PublisherSubscription?>.CreateSuccess(subscription));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> AddSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _subscriptions[subscription.PublisherId] = subscription;
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> UpdateSubscriptionAsync(PublisherSubscription subscription, CancellationToken cancellationToken = default)
        => AddSubscriptionAsync(subscription, cancellationToken);

    /// <inheritdoc/>
    public Task<OperationResult<bool>> RemoveSubscriptionAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _subscriptions.Remove(publisherId);
        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> IsSubscribedAsync(string publisherId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(OperationResult<bool>.CreateSuccess(_subscriptions.ContainsKey(publisherId)));
    }

    /// <inheritdoc/>
    public Task<OperationResult<bool>> UpdateTrustLevelAsync(string publisherId, TrustLevel trustLevel, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_subscriptions.TryGetValue(publisherId, out var subscription))
        {
            subscription.TrustLevel = trustLevel;
        }

        return Task.FromResult(OperationResult<bool>.CreateSuccess(true));
    }
}
