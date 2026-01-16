using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.MapManager;
using GenHub.Core.Interfaces.Tools.ReplayManager;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Notifications;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Tools.MapManager;
using GenHub.Core.Models.Tools.ReplayManager;

// Alias to avoid ambiguity if both have ImportResult
using MapImportResult = GenHub.Core.Models.Tools.MapManager.ImportResult;
using ReplayImportResult = GenHub.Core.Models.Tools.ReplayManager.ImportResult;

#pragma warning disable SA1649 // File name should match first type name
#pragma warning disable SA1402 // File may only contain a single type

namespace GenHub.Features.Info.Services;

/// <summary>
/// Mock implementation of <see cref="INotificationService"/> for testing and demos.
/// </summary>
public class MockNotificationService : INotificationService
{
    private readonly Subject<NotificationMessage> _notifications = new();
    private readonly Subject<Guid> _dismissRequests = new();
    private readonly Subject<bool> _dismissAllRequests = new();
    private readonly Subject<NotificationMessage> _notificationHistory = new();

    /// <inheritdoc/>
    public IObservable<NotificationMessage> Notifications => _notifications.AsObservable();

    /// <inheritdoc/>
    public IObservable<Guid> DismissRequests => _dismissRequests.AsObservable();

    /// <inheritdoc/>
    public IObservable<bool> DismissAllRequests => _dismissAllRequests.AsObservable();

    /// <inheritdoc/>
    public IObservable<NotificationMessage> NotificationHistory => _notificationHistory.AsObservable();

    /// <inheritdoc/>
    public void Show(NotificationMessage notification) => _notifications.OnNext(notification);

    /// <inheritdoc/>
    public void ShowInfo(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
        => Show(new NotificationMessage(NotificationType.Info, title, message, autoDismissMs, showInBadge: showInBadge));

    /// <inheritdoc/>
    public void ShowSuccess(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
        => Show(new NotificationMessage(NotificationType.Success, title, message, autoDismissMs, showInBadge: showInBadge));

    /// <inheritdoc/>
    public void ShowWarning(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
        => Show(new NotificationMessage(NotificationType.Warning, title, message, autoDismissMs, showInBadge: showInBadge));

    /// <inheritdoc/>
    public void ShowError(string title, string message, int? autoDismissMs = null, bool showInBadge = false)
        => Show(new NotificationMessage(NotificationType.Error, title, message, autoDismissMs, showInBadge: showInBadge));

    /// <inheritdoc/>
    public void Dismiss(Guid id) => _dismissRequests.OnNext(id);

    /// <inheritdoc/>
    public void DismissAll() => _dismissAllRequests.OnNext(true);

    /// <inheritdoc/>
    public void MarkAsRead(Guid id)
    {
    }

    /// <inheritdoc/>
    public void ClearHistory()
    {
    }
}

/// <summary>
/// Mock implementation of <see cref="IUploadHistoryService"/> for testing and demos.
/// </summary>
public class MockUploadHistoryService : IUploadHistoryService
{
    /// <inheritdoc/>
    public long MaxUploadBytesPerPeriod => 1024 * 1024 * 50; // 50MB mock

    /// <inheritdoc/>
    public Task<IEnumerable<UploadHistoryItem>> GetUploadHistoryAsync()
    {
        return Task.FromResult<IEnumerable<UploadHistoryItem>>([]);
    }

    /// <inheritdoc/>
    public Task<UsageInfo> GetUsageInfoAsync()
    {
        // UsageInfo is a record struct with (UsedBytes, LimitBytes, ResetDate)
        return Task.FromResult(new UsageInfo(1024 * 1024 * 5, 1024 * 1024 * 50, DateTime.Now.AddDays(1)));
    }

    /// <inheritdoc/>
    public Task<bool> CanUploadAsync(long fileSizeBytes)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void RecordUpload(long fileSizeBytes, string url, string fileName)
    {
    }

    /// <inheritdoc/>
    public Task RemoveHistoryItemAsync(string url)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ClearHistoryAsync()
    {
        return Task.CompletedTask;
    }
}

/// <summary>
/// Mock implementation of <see cref="IReplayDirectoryService"/> for testing and demos.
/// </summary>
public class MockReplayDirectoryService : IReplayDirectoryService
{
    /// <inheritdoc/>
    public Task<bool> DeleteReplaysAsync(IEnumerable<ReplayFile> replays, CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc/>
    public string GetReplayDirectory(GameType gameType)
    {
        return "C:\\Mock\\Replays";
    }

    /// <inheritdoc/>
    public void EnsureDirectoryExists(GameType gameType)
    {
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<ReplayFile>> GetReplaysAsync(GameType gameType, CancellationToken cancellationToken = default)
    {
        // Populate mock data for both game types for demo purposes
        var list = new List<ReplayFile>
        {
            new()
            {
                FileName = "Demo Replay 1.rep",
                FullPath = "C:\\Mock\\Demo1.rep",
                SizeInBytes = 1024 * 500,
                LastModified = DateTime.Now.AddDays(-1),
                GameVersion = gameType, // Use requested type so it appears valid
            },
            new()
            {
                FileName = "Pro Match vs AI.rep",
                FullPath = "C:\\Mock\\Demo2.rep",
                SizeInBytes = 1024 * 1200,
                LastModified = DateTime.Now.AddHours(-5),
                GameVersion = gameType, // Use requested type so it appears valid
            },
        };

        return Task.FromResult<IReadOnlyList<ReplayFile>>(list);
    }

    /// <inheritdoc/>
    public void OpenInExplorer(GameType gameType)
    {
    }

    /// <inheritdoc/>
    public void RevealInExplorer(ReplayFile replay)
    {
    }
}

/// <summary>
/// Mock implementation of <see cref="IReplayImportService"/> for testing and demos.
/// </summary>
public class MockReplayImportService : IReplayImportService
{
    /// <inheritdoc/>
    public Task<ReplayImportResult> ImportFromFilesAsync(IEnumerable<string> filePaths, GameType targetVersion, CancellationToken ct = default)
    {
        return Task.FromResult(new ReplayImportResult { Success = true, FilesImported = 0, FilesSkipped = 0 });
    }

    /// <inheritdoc/>
    public Task<ReplayImportResult> ImportFromStreamAsync(Stream stream, string fileName, GameType targetVersion, CancellationToken ct = default)
    {
         return Task.FromResult(new ReplayImportResult { Success = true, FilesImported = 0, FilesSkipped = 0 });
    }

    /// <inheritdoc/>
    public Task<ReplayImportResult> ImportFromUrlAsync(string url, GameType targetVersion, IProgress<double>? progress = null, CancellationToken ct = default)
    {
         return Task.FromResult(new ReplayImportResult { Success = true, FilesImported = 0, FilesSkipped = 0 });
    }

    /// <inheritdoc/>
    public Task<ReplayImportResult> ImportFromZipAsync(string zipPath, GameType targetVersion, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.FromResult(new ReplayImportResult { Success = true, FilesImported = 0, FilesSkipped = 0 });
    }

    /// <inheritdoc/>
    public (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath)
    {
        return (true, null);
    }
}

/// <summary>
/// Mock implementation of <see cref="IReplayExportService"/> for testing and demos.
/// </summary>
public class MockReplayExportService : IReplayExportService
{
    /// <inheritdoc/>
    public Task<string?> ExportToZipAsync(IEnumerable<ReplayFile> replays, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(destinationPath);
    }

    /// <inheritdoc/>
    public Task<string?> UploadToUploadThingAsync(IEnumerable<ReplayFile> replays, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>("https://mock.upload/share/1234");
    }
}

/// <summary>
/// Mock implementation of <see cref="IMapDirectoryService"/> for testing and demos.
/// </summary>
public class MockMapDirectoryService : IMapDirectoryService
{
    /// <inheritdoc/>
    public Task<bool> DeleteMapsAsync(IEnumerable<MapFile> maps, CancellationToken cancellationToken) => Task.FromResult(true);

    /// <inheritdoc/>
    public void EnsureDirectoryExists(GameType gameType)
    {
    }

    /// <inheritdoc/>
    public string GetMapDirectory(GameType gameType)
    {
        return "C:\\Mock\\Maps";
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MapFile>> GetMapsAsync(GameType gameType, CancellationToken ct = default)
    {
        var list = new List<MapFile>
        {
            new()
            {
                FileName = "Tournament Desert",
                DisplayName = "Tournament Desert",
                FullPath = "C:\\Mock\\Maps\\Tournament Desert",
                GameType = GameType.ZeroHour,
                IsDirectory = true,
                SizeBytes = 250000,
                LastModified = DateTime.Now,
                DirectoryName = "Tournament Desert",
                AssetFiles = ["map.ini", "map.str", "map.tga"],
            },
            new()
            {
                FileName = "Twilight Flame",
                DisplayName = "Twilight Flame",
                FullPath = "C:\\Mock\\Maps\\Twilight Flame",
                GameType = GameType.ZeroHour,
                IsDirectory = false,
                SizeBytes = 150000,
                LastModified = DateTime.Now.AddDays(-10),
                DirectoryName = "Twilight Flame",
                AssetFiles = ["map.ini", "map.str", "map.tga"],
            },
            new()
            {
                FileName = "Alpine Assault",
                DisplayName = "Alpine Assault",
                FullPath = "C:\\Mock\\Maps\\Alpine Assault",
                GameType = GameType.Generals,
                IsDirectory = true,
                SizeBytes = 180000,
                LastModified = DateTime.Now.AddDays(-5),
                DirectoryName = "Alpine Assault",
                AssetFiles = ["map.ini", "map.str", "map.tga"],
            },
            new()
            {
                FileName = "Flash Fire",
                DisplayName = "Flash Fire",
                FullPath = "C:\\Mock\\Maps\\Flash Fire",
                GameType = GameType.Generals,
                IsDirectory = false,
                SizeBytes = 120000,
                LastModified = DateTime.Now.AddDays(-20),
                DirectoryName = "Flash Fire",
                AssetFiles = ["map.ini", "map.str", "map.tga"],
            },
        };

        return Task.FromResult<IReadOnlyList<MapFile>>(list);
    }

    /// <inheritdoc/>
    public Task<bool> RenameMapAsync(MapFile map, string newName, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public void OpenInExplorer(GameType gameType)
    {
    }

    /// <inheritdoc/>
    public void RevealInExplorer(MapFile map)
    {
    }
}

/// <summary>
/// Mock implementation of <see cref="IMapImportService"/> for testing and demos.
/// </summary>
public class MockMapImportService : IMapImportService
{
    /// <inheritdoc/>
    public Task<MapImportResult> ImportFromFilesAsync(IEnumerable<string> filePaths, GameType targetVersion, CancellationToken ct = default)
    {
         // MapImportResult does NOT have FilesSkipped (unlike ReplayImportResult)
         return Task.FromResult(new MapImportResult { Success = true, FilesImported = 0 });
    }

    /// <inheritdoc/>
    public Task<MapImportResult> ImportFromStreamAsync(Stream stream, string fileName, GameType targetVersion, CancellationToken ct = default)
    {
         return Task.FromResult(new MapImportResult { Success = true, FilesImported = 0 });
    }

    /// <inheritdoc/>
    public Task<MapImportResult> ImportFromUrlAsync(string url, GameType targetVersion, IProgress<double>? progress = null, CancellationToken ct = default)
    {
         return Task.FromResult(new MapImportResult { Success = true, FilesImported = 0 });
    }

    /// <inheritdoc/>
    public Task<MapImportResult> ImportFromZipAsync(string zipPath, GameType targetVersion, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.FromResult(new MapImportResult { Success = true, FilesImported = 0 });
    }

    /// <inheritdoc/>
    public (bool IsValid, string? ErrorMessage) ValidateZip(string zipPath)
    {
        return (true, null);
    }
}

/// <summary>
/// Mock implementation of <see cref="IMapExportService"/> for testing and demos.
/// </summary>
public class MockMapExportService : IMapExportService
{
    /// <inheritdoc/>
    public Task<string?> ExportToZipAsync(IEnumerable<MapFile> maps, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(destinationPath);
    }

    /// <inheritdoc/>
    public Task<string?> UploadToUploadThingAsync(IEnumerable<MapFile> maps, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>("https://mock.upload/maps/123");
    }
}

/// <summary>
/// Mock implementation of <see cref="IMapPackService"/> for testing and demos.
/// </summary>
public class MockMapPackService : IMapPackService
{
    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> CreateCasMapPackAsync(string name, GameType targetGame, IEnumerable<MapFile> selectedMaps, IProgress<ContentStorageProgress>? progress = null, CancellationToken ct = default)
    {
        return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest()));
    }

    /// <inheritdoc/>
    public Task<MapPack> CreateMapPackAsync(string name, Guid? profileId, IEnumerable<string> mapFilePaths)
    {
        return Task.FromResult(new MapPack { Name = name });
    }

    /// <inheritdoc/>
    public Task<bool> DeleteMapPackAsync(ManifestId mapPackId) => Task.FromResult(true);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MapPack>> GetAllMapPacksAsync() => Task.FromResult<IReadOnlyList<MapPack>>([]);

    /// <inheritdoc/>
    public Task<IReadOnlyList<MapPack>> GetMapPacksForProfileAsync(Guid profileId) => Task.FromResult<IReadOnlyList<MapPack>>([]);

    /// <inheritdoc/>
    public Task<bool> LoadMapPackAsync(ManifestId mapPackId) => Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> UnloadMapPackAsync(ManifestId mapPackId) => Task.FromResult(true);

    /// <inheritdoc/>
    public Task<bool> UpdateMapPackAsync(MapPack mapPack) => Task.FromResult(true);
}

/// <summary>
/// Mock implementation of <see cref="ILocalContentService"/> for testing and demos.
/// </summary>
public class MockLocalContentService : ILocalContentService
{
    /// <inheritdoc/>
    public IReadOnlyList<ContentType> AllowedContentTypes => [ContentType.Mod, ContentType.Map, ContentType.GameClient];

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> AddLocalContentAsync(string name, string directoryPath, ContentType contentType, GameType targetGame)
    {
        return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest { Name = name, ContentType = contentType, TargetGame = targetGame }));
    }

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> CreateLocalContentManifestAsync(string directoryPath, string name, ContentType contentType, GameType targetGame, IProgress<ContentStorageProgress>? progress = null, CancellationToken cancellationToken = default)
    {
         return Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(new ContentManifest { Name = name, ContentType = contentType, TargetGame = targetGame }));
    }

    /// <inheritdoc/>
    public Task<OperationResult> DeleteLocalContentAsync(string manifestId) => Task.FromResult(OperationResult.CreateSuccess());
}

/// <summary>
/// Mock implementation of <see cref="IGameProfileManager"/>.
/// </summary>
public class MockGameProfileManager : IGameProfileManager
{
    /// <inheritdoc/>
    public Task<ProfileOperationResult<IReadOnlyList<GameProfile>>> GetAllProfilesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([]));

    /// <inheritdoc/>
    public Task<ProfileOperationResult<GameProfile>> GetProfileAsync(string profileId, CancellationToken cancellationToken = default)
        => Task.FromResult(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile
        {
            Id = profileId,
            Name = "Demo Profile",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        }));

    /// <inheritdoc/>
    public Task<ProfileOperationResult<GameProfile>> CreateProfileAsync(CreateProfileRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()));

    /// <inheritdoc/>
    public Task<ProfileOperationResult<GameProfile>> UpdateProfileAsync(string profileId, UpdateProfileRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile()));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> DeleteProfileAsync(string profileId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<ProfileOperationResult<IReadOnlyList<ContentManifest>>> GetAvailableContentAsync(GameClient gameClient, CancellationToken cancellationToken = default)
        => Task.FromResult(ProfileOperationResult<IReadOnlyList<ContentManifest>>.CreateSuccess([]));
}

/// <summary>
/// Mock implementation of <see cref="IConfigurationProviderService"/>.
/// </summary>
public class MockConfigurationProviderService : IConfigurationProviderService
{
    /// <summary>
    /// Gets the Generals installation path.
    /// </summary>
    /// <returns>The Generals installation path.</returns>
    public static string GetGeneralsInstallationPath() => @"C:\Games\Generals";

    /// <summary>
    /// Gets the Zero Hour installation path.
    /// </summary>
    /// <returns>The Zero Hour installation path.</returns>
    public static string GetZeroHourInstallationPath() => @"C:\Games\Zero Hour";

    /// <summary>
    /// Saves the configuration asynchronously.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static Task SaveConfigurationAsync() => Task.CompletedTask;

    /// <summary>
    /// Uses the default configuration.
    /// </summary>
    public static void UseDefaultConfiguration()
    {
    }

    /// <inheritdoc/>
    public string GetWorkspacePath() => @"C:\GenHub\Workspace";

    /// <inheritdoc/>
    public string GetCachePath() => @"C:\GenHub\Cache";

    /// <inheritdoc/>
    public int GetMaxConcurrentDownloads() => 4;

    /// <inheritdoc/>
    public bool GetAllowBackgroundDownloads() => true;

    /// <inheritdoc/>
    public int GetDownloadTimeoutSeconds() => 300;

    /// <inheritdoc/>
    public string GetDownloadUserAgent() => "GenHub/1.0";

    /// <inheritdoc/>
    public int GetDownloadBufferSize() => 8192;

    /// <inheritdoc/>
    public WorkspaceStrategy GetDefaultWorkspaceStrategy() => WorkspaceStrategy.SymlinkOnly;

    /// <inheritdoc/>
    public bool GetAutoCheckForUpdatesOnStartup() => true;

    /// <inheritdoc/>
    public bool GetEnableDetailedLogging() => false;

    /// <inheritdoc/>
    public string GetTheme() => "System";

    /// <inheritdoc/>
    public double GetWindowWidth() => 1280;

    /// <inheritdoc/>
    public double GetWindowHeight() => 720;

    /// <inheritdoc/>
    public bool GetIsWindowMaximized() => false;

    /// <inheritdoc/>
    public NavigationTab GetLastSelectedTab() => NavigationTab.Home;

    /// <inheritdoc/>
    public UserSettings GetEffectiveSettings() => new();

    /// <inheritdoc/>
    public List<string> GetContentDirectories() => [@"C:\Games\Content"];

    /// <inheritdoc/>
    public List<string> GetGitHubDiscoveryRepositories() => ["owner/repo"];

    /// <inheritdoc/>
    public string GetApplicationDataPath() => @"C:\GenHub\AppData";

    /// <inheritdoc/>
    public string GetRootAppDataPath() => @"C:\GenHub";

    /// <inheritdoc/>
    public string GetProfilesPath() => @"C:\GenHub\Profiles";

    /// <inheritdoc/>
    public string GetManifestsPath() => @"C:\GenHub\Manifests";

    /// <inheritdoc/>
    public CasConfiguration GetCasConfiguration() => new();

    /// <inheritdoc/>
    public string GetLogsPath() => @"C:\GenHub\Logs";
}

/// <summary>
/// Mock implementation of <see cref="IProfileContentLoader"/>.
/// </summary>
public class MockProfileContentLoader : IProfileContentLoader
{
    /// <inheritdoc/>
    public Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameInstallationsAsync()
    {
        var list = new ObservableCollection<ContentDisplayItem>
        {
            new()
            {
                Id = "mock-zh-install",
                ManifestId = ManifestId.Create("mock.ea.gameinstallation.zerohour"),
                DisplayName = "Zero Hour (EA App)",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.ZeroHour,
                InstallationType = GameInstallationType.EaApp,
                IsEnabled = true,
                Version = "1.04",
            },
            new()
            {
                Id = "mock-gen-install",
                ManifestId = ManifestId.Create("mock.ea.gameinstallation.generals"),
                DisplayName = "Generals (EA App)",
                ContentType = ContentType.GameInstallation,
                GameType = GameType.Generals,
                InstallationType = GameInstallationType.EaApp,
                IsEnabled = false,
                Version = "1.08",
            },
        };
        return Task.FromResult(list);
    }

    /// <inheritdoc/>
    public Task<ObservableCollection<ContentDisplayItem>> LoadAvailableGameClientsAsync()
    {
        var list = new ObservableCollection<ContentDisplayItem>
        {
            new()
            {
                Id = "mock-zh-client",
                ManifestId = ManifestId.Create("1.104.ea.gameclient.zerohour"),
                DisplayName = "Zero Hour v1.04",
                ContentType = ContentType.GameClient,
                GameType = GameType.ZeroHour,
                Version = "1.04",
                Publisher = "EA",
                InstallationType = GameInstallationType.Unknown,
            },
        };
        return Task.FromResult(list);
    }

    /// <inheritdoc/>
    public Task<ObservableCollection<ContentDisplayItem>> LoadAvailableContentAsync(
        ContentType contentType,
        ObservableCollection<ContentDisplayItem> availableGameInstallations,
        IEnumerable<string> enabledContentIds)
    {
        var list = new ObservableCollection<ContentDisplayItem>();

        switch (contentType)
        {
            case ContentType.GameClient:
                list.Add(new ContentDisplayItem { Id = "zh-client", DisplayName = "Zero Hour v1.04", ContentType = ContentType.GameClient, GameType = GameType.ZeroHour, Publisher = "EA", Version = "1.04", ManifestId = ManifestId.Create("zh-client"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "gen-client", DisplayName = "Generals v1.08", ContentType = ContentType.GameClient, GameType = GameType.Generals, Publisher = "EA", Version = "1.08", ManifestId = ManifestId.Create("gen-client"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "tfd-client", DisplayName = "The First Decade", ContentType = ContentType.GameClient, GameType = GameType.ZeroHour, Publisher = "EA", Version = "TFD", ManifestId = ManifestId.Create("tfd-client"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "go-client", DisplayName = "Generals Online", ContentType = ContentType.GameClient, GameType = GameType.ZeroHour, Publisher = "Community", Version = "1.0", ManifestId = ManifestId.Create("go-client"), InstallationType = GameInstallationType.Unknown });
                break;

            case ContentType.Mod:
                list.Add(new ContentDisplayItem { Id = "rotr-187", DisplayName = "Rise of the Reds 1.87", ContentType = ContentType.Mod, GameType = GameType.ZeroHour, Publisher = "SWR Productions", Version = "1.87", ManifestId = ManifestId.Create("rotr-187"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "shw-1201", DisplayName = "ShockWave 1.201", ContentType = ContentType.Mod, GameType = GameType.ZeroHour, Publisher = "SWR Productions", Version = "1.201", ManifestId = ManifestId.Create("shw-1201"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "contra-009", DisplayName = "Contra 009 Final", ContentType = ContentType.Mod, GameType = GameType.ZeroHour, Publisher = "Contra Team", Version = "009F", ManifestId = ManifestId.Create("contra-009"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "teod", DisplayName = "The End of Days", ContentType = ContentType.Mod, GameType = GameType.ZeroHour, Publisher = "TEOD Team", Version = "1.0", ManifestId = ManifestId.Create("teod"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "untitled", DisplayName = "Untitled", ContentType = ContentType.Mod, GameType = GameType.ZeroHour, Publisher = "Untitled Team", Version = "3.2", ManifestId = ManifestId.Create("untitled"), InstallationType = GameInstallationType.Unknown });
                break;

            case ContentType.Map:
                list.Add(new ContentDisplayItem { Id = "td2", DisplayName = "Tournament Desert II", ContentType = ContentType.Map, GameType = GameType.ZeroHour, Publisher = "Unknown", ManifestId = ManifestId.Create("td2"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "tf-opt", DisplayName = "Twighlight Flame Optimized", ContentType = ContentType.Map, GameType = GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("tf-opt"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "naval-pack", DisplayName = "Naval Wars Map Pack", ContentType = ContentType.Map, GameType = GameType.ZeroHour, Publisher = "MapMaker123", ManifestId = ManifestId.Create("naval-pack"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "ffa-maps", DisplayName = "FFA Map Collection", ContentType = ContentType.Map, GameType = GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("ffa-maps"), InstallationType = GameInstallationType.Unknown });
                break;

            case ContentType.MapPack:
                list.Add(new ContentDisplayItem { Id = "aod-pack", DisplayName = "Art of Defense (AOD) Pack", ContentType = ContentType.MapPack, GameType = GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("aod-pack"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "mission-maps", DisplayName = "Co-Op Mission Maps", ContentType = ContentType.MapPack, GameType = GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("mission-maps"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "ranked-1v1", DisplayName = "Ranked 1v1 Maps 2025", ContentType = ContentType.MapPack, GameType = GameType.ZeroHour, Publisher = "Online League", ManifestId = ManifestId.Create("ranked-1v1"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "team-games", DisplayName = "Team Games Compendium", ContentType = ContentType.MapPack, GameType = GameType.ZeroHour, Publisher = "Community", ManifestId = ManifestId.Create("team-games"), InstallationType = GameInstallationType.Unknown });
                break;

            case ContentType.Addon:
                list.Add(new ContentDisplayItem { Id = "custom-gui", DisplayName = "Modern GUI Overlay", ContentType = ContentType.Addon, GameType = GameType.ZeroHour, Publisher = "UI Modder", ManifestId = ManifestId.Create("custom-gui"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "hd-sounds", DisplayName = "HD Sound Effects", ContentType = ContentType.Addon, GameType = GameType.ZeroHour, Publisher = "Audio Team", ManifestId = ManifestId.Create("hd-sounds"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "music-pack", DisplayName = "Original Soundtrack Remaster", ContentType = ContentType.Addon, GameType = GameType.ZeroHour, Publisher = "Composer", ManifestId = ManifestId.Create("music-pack"), InstallationType = GameInstallationType.Unknown });
                break;

            case ContentType.Patch:
                list.Add(new ContentDisplayItem { Id = "gentool", DisplayName = "GenTool v8.9", ContentType = ContentType.Patch, GameType = GameType.ZeroHour, Publisher = "xezon", Version = "8.9", ManifestId = ManifestId.Create("gentool"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "cbpro", DisplayName = "ControlBar Pro", ContentType = ContentType.Patch, GameType = GameType.ZeroHour, Publisher = "Community", Version = "1.0", ManifestId = ManifestId.Create("cbpro"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "4gb", DisplayName = "4GB Patch", ContentType = ContentType.Patch, GameType = GameType.ZeroHour, Publisher = "NTCore", Version = "1.0", ManifestId = ManifestId.Create("4gb"), InstallationType = GameInstallationType.Unknown });
                break;

            case ContentType.ModdingTool:
                list.Add(new ContentDisplayItem { Id = "wb", DisplayName = "World Builder", ContentType = ContentType.ModdingTool, GameType = GameType.ZeroHour, Publisher = "EA", Version = "1.0", ManifestId = ManifestId.Create("wb"), InstallationType = GameInstallationType.Unknown });
                list.Add(new ContentDisplayItem { Id = "finalbig", DisplayName = "FinalBig", ContentType = ContentType.ModdingTool, GameType = GameType.ZeroHour, Publisher = "Community", Version = "0.4", ManifestId = ManifestId.Create("finalbig"), InstallationType = GameInstallationType.Unknown });
                break;
        }

        return Task.FromResult(list);
    }

    /// <inheritdoc/>
    public Task<ObservableCollection<ContentDisplayItem>> LoadEnabledContentForProfileAsync(GameProfile profile)
        => Task.FromResult(new ObservableCollection<ContentDisplayItem>());

    /// <inheritdoc/>
    public Task<ObservableCollection<ContentDisplayItem>> GetAutoInstallDependenciesAsync(string manifestId)
        => Task.FromResult(new ObservableCollection<ContentDisplayItem>());

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest?>> GetManifestAsync(string manifestId)
        => Task.FromResult(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest()));
}

/// <summary>
/// Mock implementation of <see cref="IContentManifestPool"/>.
/// </summary>
public class MockContentManifestPool : IContentManifestPool
{
    /// <inheritdoc/>
    public Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> AddManifestAsync(ContentManifest manifest, string sourceDirectory, IProgress<ContentStorageProgress>? progress = null, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest?>> GetManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest()));

    /// <inheritdoc/>
    public Task<OperationResult<IEnumerable<ContentManifest>>> GetAllManifestsAsync(CancellationToken cancellationToken = default)
    {
        var list = new List<ContentManifest>
        {
            new() { ContentType = ContentType.GameClient, TargetGame = GameType.ZeroHour, Name = "Zero Hour", Id = "zh-104" },
            new() { ContentType = ContentType.Mod, TargetGame = GameType.ZeroHour, Name = "Rise of the Reds", Id = "rotr-187" },
            new() { ContentType = ContentType.MapPack, TargetGame = GameType.ZeroHour, Name = "Competitive Maps", Id = "comp-maps" },
            new() { ContentType = ContentType.Map, TargetGame = GameType.ZeroHour, Name = "Tournament Desert II", Id = "td2" },
            new() { ContentType = ContentType.Addon, TargetGame = GameType.ZeroHour, Name = "Modern GUI", Id = "custom-gui" },
            new() { ContentType = ContentType.Patch, TargetGame = GameType.ZeroHour, Name = "Community Patch 1.06", Id = "cp-106" },
            new() { ContentType = ContentType.ModdingTool, TargetGame = GameType.ZeroHour, Name = "GenPatcher", Id = "gp-100" },
        };
        return Task.FromResult(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(list));
    }

    /// <inheritdoc/>
    public Task<OperationResult<IEnumerable<ContentManifest>>> SearchManifestsAsync(ContentSearchQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> RemoveManifestAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> IsManifestAcquiredAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<OperationResult<string?>> GetContentDirectoryAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<string?>.CreateSuccess($@"C:\GenHub\Content\{manifestId}"));
}

/// <summary>
/// Mock implementation of <see cref="IContentStorageService"/>.
/// </summary>
public class MockContentStorageService : IContentStorageService
{
    /// <inheritdoc/>
    public string GetContentStorageRoot() => @"C:\GenHub\Content";

    /// <inheritdoc/>
    public string GetManifestStoragePath(ManifestId manifestId) => $@"C:\GenHub\Content\{manifestId}";

    /// <inheritdoc/>
    public Task<OperationResult<ContentManifest>> StoreContentAsync(
        ContentManifest manifest,
        string sourceDirectory,
        IProgress<ContentStorageProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<ContentManifest>.CreateSuccess(manifest));

    /// <inheritdoc/>
    public Task<OperationResult<string>> RetrieveContentAsync(
        ManifestId manifestId,
        string targetDirectory,
        CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<string>.CreateSuccess(targetDirectory));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> IsContentStoredAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<OperationResult<bool>> RemoveContentAsync(ManifestId manifestId, CancellationToken cancellationToken = default)
        => Task.FromResult(OperationResult<bool>.CreateSuccess(true));

    /// <inheritdoc/>
    public Task<StorageStats> GetStorageStatsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new StorageStats());
}
