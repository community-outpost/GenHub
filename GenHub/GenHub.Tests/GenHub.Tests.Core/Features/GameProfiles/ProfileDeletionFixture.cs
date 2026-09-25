using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.UserData;
using GenHub.Core.Models.Workspace;
using GenHub.Features.GameProfiles.Infrastructure;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.Storage.Services;
using GenHub.Features.UserData.Services;
using GenHub.Features.Workspace;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Builds a profile with a prepared workspace, tracked workspace CAS references, and deployed user data,
/// all under a private temporary root, and wires the real services that delete it.
/// </summary>
internal sealed class ProfileDeletionFixture : IDisposable
{
    /// <summary>The manifest that owns the profile's user map.</summary>
    public const string MapManifestId = "1.0.test.mappack.usermaps";

    /// <summary>The content of the user's own map that GenHub overwrote and must restore.</summary>
    public const string OriginalMapContent = "users-original-map";

    private const string DeployedMapRelativePath = "Maps/SharedMap/SharedMap.map";

    private readonly Mock<IFileOperationsService> _fileOperations = new();
    private readonly Mock<IGamePathProvider> _pathProvider = new();
    private readonly Mock<IConfigurationProviderService> _configProvider = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileDeletionFixture"/> class.
    /// </summary>
    public ProfileDeletionFixture()
    {
        RootPath = Directory.CreateTempSubdirectory("GenHub.ProfileDeletion.").FullName;
        AppDataPath = Path.Combine(RootPath, "AppData");
        ProfilesPath = Path.Combine(AppDataPath, "Profiles");
        CasRootPath = Path.Combine(RootPath, "Cas");
        OptionsPath = Path.Combine(RootPath, "Documents", GameSettingsConstants.FolderNames.ZeroHour);
        WorkspacesPath = Path.Combine(RootPath, "Workspaces");
        Directory.CreateDirectory(AppDataPath);
        Directory.CreateDirectory(OptionsPath);

        _configProvider.Setup(c => c.GetApplicationDataPath()).Returns(AppDataPath);
        _pathProvider.Setup(p => p.GetOptionsDirectory(It.IsAny<GameType>())).Returns(OptionsPath);

        _fileOperations
            .Setup(f => f.CopyFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, ContentType?, CancellationToken>((hash, target, _, _) => WriteCasCopy(hash, target))
            .ReturnsAsync(true);
        _fileOperations
            .Setup(f => f.LinkFromCasAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<ContentType?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, bool, ContentType?, CancellationToken>((hash, target, _, _, _) => WriteCasCopy(hash, target))
            .ReturnsAsync(true);
        _fileOperations
            .Setup(f => f.VerifyFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _fileOperations
            .Setup(f => f.CheckFileHashAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FileHashVerification.Match);

        ManifestPool
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        Profile = new GameProfile
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Doomed Profile",
            GameInstallationId = "installation-1",
            EnabledContentIds = [MapManifestId],
        };

        Profile.ActiveWorkspaceId = Profile.Id;
        WorkspacePath = Path.Combine(WorkspacesPath, Profile.Id);
        DeployedMapPath = Path.Combine(OptionsPath, "Maps", "SharedMap", "SharedMap.map");
        UserCreatedMapPath = Path.Combine(OptionsPath, "Maps", "MyOwnMap", "MyOwnMap.map");
        WorkspaceObjectHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("workspace-object"))).ToLowerInvariant();
        MapObjectHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("map-object"))).ToLowerInvariant();
    }

    /// <summary>Gets the private temporary root that contains every path the fixture touches.</summary>
    public string RootPath { get; }

    /// <summary>Gets the application data directory.</summary>
    public string AppDataPath { get; }

    /// <summary>Gets the profiles directory.</summary>
    public string ProfilesPath { get; }

    /// <summary>Gets the CAS root.</summary>
    public string CasRootPath { get; }

    /// <summary>Gets the game options directory that stands in for the Documents user data folder.</summary>
    public string OptionsPath { get; }

    /// <summary>Gets the workspace root.</summary>
    public string WorkspacesPath { get; }

    /// <summary>Gets the profile being deleted.</summary>
    public GameProfile Profile { get; }

    /// <summary>Gets the profile's workspace directory.</summary>
    public string WorkspacePath { get; }

    /// <summary>Gets the path of the map GenHub deployed over the user's original.</summary>
    public string DeployedMapPath { get; }

    /// <summary>Gets the path of a map the user created and GenHub never tracked.</summary>
    public string UserCreatedMapPath { get; }

    /// <summary>Gets the CAS object pinned only by the profile's workspace references.</summary>
    public string WorkspaceObjectHash { get; }

    /// <summary>Gets the CAS object backing the deployed map.</summary>
    public string MapObjectHash { get; }

    /// <summary>Gets the manifest pool mock. It is empty, as it is after Settings deletes every manifest.</summary>
    public Mock<IContentManifestPool> ManifestPool { get; } = new();

    /// <summary>Gets the launch registry mock.</summary>
    public Mock<ILaunchRegistry> LaunchRegistry { get; } = new();

    /// <summary>Gets the file operations mock used for deployed user data.</summary>
    public Mock<IFileOperationsService> FileOperations => _fileOperations;

    /// <summary>Gets the user data manifest file for the profile.</summary>
    public string UserDataManifestPath => Path.Combine(
        AppDataPath,
        DirectoryNames.UserData,
        DirectoryNames.UserDataManifests,
        $"{MapManifestId}_{Profile.Id}{FileTypes.UserDataManifestExtension}");

    /// <summary>Gets the user data index file.</summary>
    public string UserDataIndexPath => Path.Combine(AppDataPath, DirectoryNames.UserData, FileTypes.UserDataIndexFileName);

    /// <summary>Gets the workspace references file for the profile.</summary>
    public string WorkspaceRefsPath => Path.Combine(CasRootPath, "refs", "workspaces", $"{Profile.Id}.refs");

    /// <summary>
    /// Persists the profile, its prepared workspace and CAS references, and its deployed user map.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task ArrangeProfileWithDataAsync()
    {
        var saveResult = await CreateRepository().SaveProfileAsync(Profile);
        Assert.True(saveResult.Success, saveResult.FirstError);

        var casStorage = CreateCasStorage();
        foreach (var (hash, content) in new[] { (WorkspaceObjectHash, "workspace-object"), (MapObjectHash, "map-object") })
        {
            await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            Assert.NotNull(await casStorage.StoreObjectAsync(stream, hash));
        }

        Directory.CreateDirectory(WorkspacePath);
        await File.WriteAllTextAsync(Path.Combine(WorkspacePath, "generals.exe"), "workspace-file");
        var workspace = new WorkspaceInfo
        {
            Id = Profile.Id,
            WorkspacePath = WorkspacePath,
            IsPrepared = true,
            Strategy = WorkspaceStrategy.HardLink,
        };
        await File.WriteAllTextAsync(Path.Combine(AppDataPath, FileTypes.WorkspaceMetadataFileName), JsonSerializer.Serialize(new List<WorkspaceInfo> { workspace }));
        var trackResult = await CreateReferenceTracker().TrackWorkspaceReferencesAsync(Profile.Id, [WorkspaceObjectHash, MapObjectHash]);
        Assert.True(trackResult.Success, trackResult.FirstError);

        Directory.CreateDirectory(Path.GetDirectoryName(DeployedMapPath)!);
        await File.WriteAllTextAsync(DeployedMapPath, OriginalMapContent);
        Directory.CreateDirectory(Path.GetDirectoryName(UserCreatedMapPath)!);
        await File.WriteAllTextAsync(UserCreatedMapPath, "users-own-map");

        var installResult = await CreateUserDataTracker().InstallUserDataAsync(
            MapManifestId,
            Profile.Id,
            GameType.ZeroHour,
            [
                new ManifestFile
                {
                    RelativePath = DeployedMapRelativePath,
                    Hash = MapObjectHash,
                    Size = 10,
                    InstallTarget = ContentInstallTarget.UserDataDirectory,
                },
            ],
            "1.0",
            "Test Map Pack");
        Assert.True(installResult.Success, installResult.FirstError);
        Assert.Equal(DeployedCasContent(MapObjectHash), await File.ReadAllTextAsync(DeployedMapPath));

        AssertArranged();
    }

    /// <summary>Creates a profile manager over fresh instances of the real services.</summary>
    /// <returns>The profile manager.</returns>
    public GameProfileManager CreateProfileManager() => new(
        CreateRepository(),
        Mock.Of<IGameInstallationService>(),
        ManifestPool.Object,
        Mock.Of<IGameSettingsService>(),
        CreateWorkspaceManager(),
        new ProfileContentLinkerService(CreateUserDataTracker(), NullLogger<ProfileContentLinkerService>.Instance),
        NullLogger<GameProfileManager>.Instance,
        LaunchRegistry.Object);

    /// <summary>Creates a profile repository over the fixture's profiles directory.</summary>
    /// <returns>The repository.</returns>
    public GameProfileRepository CreateRepository() => new(ProfilesPath, NullLogger<GameProfileRepository>.Instance);

    /// <summary>Creates a CAS lifecycle manager that sees an empty manifest pool.</summary>
    /// <returns>The lifecycle manager.</returns>
    public CasLifecycleManager CreateLifecycleManager() => new(
        CreateReferenceTracker(),
        ManifestPool.Object,
        CreateCasStorage(),
        CreateCasOptions(),
        NullLogger<CasLifecycleManager>.Instance,
        new CasWriteFence());

    /// <summary>Creates a CAS storage over the fixture's CAS root.</summary>
    /// <returns>The storage.</returns>
    public CasStorage CreateCasStorage() => new(CreateCasOptions(), NullLogger<CasStorage>.Instance);

    /// <summary>Reads the user data index as it is persisted on disk.</summary>
    /// <returns>The index.</returns>
    public async Task<UserDataIndex> ReadUserDataIndexAsync()
    {
        var json = await File.ReadAllTextAsync(UserDataIndexPath);
        return JsonSerializer.Deserialize<UserDataIndex>(json)!;
    }

    /// <summary>
    /// Asserts that everything the profile owned is gone after a reload, the user's files survive,
    /// and a forced garbage collection can free the CAS objects the profile had pinned.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task AssertProfileAndDataRemovedAsync()
    {
        var reloaded = await CreateRepository().LoadProfileAsync(Profile.Id);
        Assert.False(reloaded.Success);

        Assert.False(Directory.Exists(WorkspacePath));
        Assert.False(File.Exists(WorkspaceRefsPath));
        Assert.Empty(await CreateReferenceTracker().GetAllReferencedHashesAsync());
        var workspaces = await CreateWorkspaceManager().GetAllWorkspacesAsync();
        Assert.DoesNotContain(workspaces.Data!, w => w.Id == Profile.Id);

        Assert.False(File.Exists(UserDataManifestPath));
        var index = await ReadUserDataIndexAsync();
        Assert.False(index.ProfileInstallations.ContainsKey(Profile.Id));
        Assert.DoesNotContain(index.InstallationKeys, k => k.Contains(Profile.Id, StringComparison.Ordinal));
        Assert.DoesNotContain(index.FileToInstallationMap.Values, k => k.Contains(Profile.Id, StringComparison.Ordinal));

        Assert.Equal(OriginalMapContent, await File.ReadAllTextAsync(DeployedMapPath));
        Assert.True(File.Exists(UserCreatedMapPath));

        using var lifecycle = CreateLifecycleManager();
        var gc = await lifecycle.RunGarbageCollectionAsync(force: true);
        Assert.True(gc.Success, gc.FirstError);
        Assert.Equal(2, gc.Data!.ObjectsDeleted);
        var casStorage = CreateCasStorage();
        Assert.False(await casStorage.ObjectExistsAsync(WorkspaceObjectHash));
        Assert.False(await casStorage.ObjectExistsAsync(MapObjectHash));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(RootPath, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static string DeployedCasContent(string hash) => "cas-content-" + hash;

    private static void WriteCasCopy(string hash, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, DeployedCasContent(hash));
    }

    private void AssertArranged()
    {
        var tempRoot = Path.GetFullPath(Path.GetTempPath());
        Assert.StartsWith(tempRoot, Path.GetFullPath(RootPath), StringComparison.Ordinal);
        foreach (var path in new[] { AppDataPath, ProfilesPath, CasRootPath, OptionsPath, WorkspacePath, DeployedMapPath, UserDataManifestPath, WorkspaceRefsPath })
        {
            Assert.StartsWith(RootPath, Path.GetFullPath(path), StringComparison.Ordinal);
        }

        Assert.True(Directory.Exists(WorkspacePath));
        Assert.True(File.Exists(WorkspaceRefsPath));
        Assert.True(File.Exists(UserDataManifestPath));
        Assert.True(File.Exists(UserDataIndexPath));
    }

    private IOptions<CasConfiguration> CreateCasOptions() => Options.Create(new CasConfiguration
    {
        CasRootPath = CasRootPath,
        GcGracePeriod = TimeSpan.FromDays(1),
        GcLockTimeout = TimeSpan.FromSeconds(5),
    });

    private CasReferenceTracker CreateReferenceTracker() => new(CreateCasOptions(), NullLogger<CasReferenceTracker>.Instance);

    private UserDataTrackerService CreateUserDataTracker() => new(
        _configProvider.Object,
        _fileOperations.Object,
        NullLogger<UserDataTrackerService>.Instance,
        _pathProvider.Object);

    private WorkspaceManager CreateWorkspaceManager() => new(
        [],
        _configProvider.Object,
        NullLogger<WorkspaceManager>.Instance,
        CreateReferenceTracker(),
        Mock.Of<IWorkspaceValidator>(),
        new WorkspaceReconciler(NullLogger<WorkspaceReconciler>.Instance, _fileOperations.Object));
}
