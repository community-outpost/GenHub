using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Storage;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ManifestContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Tests installation CAS pool selection, migration, and legacy lookup behavior.
/// </summary>
public sealed class InstallationCasPoolServiceTests : IDisposable
{
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly Mock<IUserSettingsService> _userSettingsService = new();
    private readonly Mock<IStorageWritabilityProbe> _writabilityProbe = new();
    private readonly Mock<ICasPoolManager> _poolManager = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="InstallationCasPoolServiceTests"/> class.
    /// </summary>
    public InstallationCasPoolServiceTests()
    {
        Directory.CreateDirectory(_tempPath);
    }

    /// <summary>
    /// Clears a historical auto-derived path and retains it for read-only lookup when it is unwritable.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsurePoolPathAsync_WhenHistoricalPathIsUnwritable_PreservesLegacyLookup()
    {
        var installation = CreateInstallation();
        var poolPath = Path.Combine(installation.InstallationPath, DirectoryNames.GenHubCasPool);
        Directory.CreateDirectory(poolPath);
        var settings = new UserSettings
        {
            CasConfiguration = new CasConfiguration { InstallationPoolRootPath = poolPath },
            ExplicitlySetProperties = [nameof(CasConfiguration.InstallationPoolRootPath)],
        };
        ConfigureMutableSettings(settings);
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(poolPath)).Returns(false);
        var service = CreateService();

        var result = await service.EnsurePoolPathAsync([installation]);

        Assert.True(result);
        Assert.Empty(settings.CasConfiguration.InstallationPoolRootPath);
        Assert.Equal(poolPath, settings.CasConfiguration.LegacyInstallationPoolRootPath);
        Assert.False(settings.CasConfiguration.IsInstallationPoolRootPathAutoDerived);
        Assert.DoesNotContain(nameof(CasConfiguration.InstallationPoolRootPath), settings.ExplicitlySetProperties);
        _poolManager.Verify(manager => manager.ReinitializeInstallationPool(), Times.Once);
    }

    /// <summary>
    /// Preserves a deliberate custom path instead of replacing it with an automatically derived path.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsurePoolPathAsync_WhenCustomPathIsConfigured_PreservesIt()
    {
        var installation = CreateInstallation();
        var customPath = Path.Combine(_tempPath, "custom-cas");
        var settings = new UserSettings
        {
            CasConfiguration = new CasConfiguration { InstallationPoolRootPath = customPath },
        };
        ConfigureMutableSettings(settings);
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(customPath)).Returns(true);
        var service = CreateService();

        var result = await service.EnsurePoolPathAsync([installation]);

        Assert.True(result);
        Assert.Equal(customPath, settings.CasConfiguration.InstallationPoolRootPath);
        _userSettingsService.Verify(
            service => service.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()),
            Times.Never);
        _poolManager.Verify(manager => manager.ReinitializeInstallationPool(), Times.Never);
    }

    /// <summary>
    /// Persists provenance when a writable adjacent pool is selected automatically.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsurePoolPathAsync_WhenAdjacentPathIsWritable_RecordsAutoDerivedProvenance()
    {
        var installation = CreateInstallation();
        var poolPath = Path.Combine(installation.InstallationPath, DirectoryNames.GenHubCasPool);
        var settings = new UserSettings();
        ConfigureMutableSettings(settings);
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(poolPath)).Returns(true);
        var service = CreateService();

        var result = await service.EnsurePoolPathAsync([installation]);

        Assert.True(result);
        Assert.Equal(poolPath, settings.CasConfiguration.InstallationPoolRootPath);
        Assert.True(settings.CasConfiguration.IsInstallationPoolRootPathAutoDerived);
        Assert.Equal(installation.Id, settings.PreferredStorageInstallationId);
        _poolManager.Verify(manager => manager.ReinitializeInstallationPool(), Times.Once);
    }

    /// <summary>
    /// Continues with primary storage when no installation is available.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsurePoolPathAsync_WhenNoInstallations_ContinuesWithPrimaryPool()
    {
        var service = CreateService();

        var result = await service.EnsurePoolPathAsync([]);

        Assert.True(result);
        _userSettingsService.Verify(
            settings => settings.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()),
            Times.Never);
    }

    /// <summary>
    /// Keeps a dotted installation directory intact when deriving the adjacent pool path.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsurePoolPathAsync_WhenInstallationDirectoryContainsDot_UsesFullDirectory()
    {
        var installation = CreateInstallation("ZeroHour v1.04");
        var poolPath = Path.Combine(installation.InstallationPath, DirectoryNames.GenHubCasPool);
        var settings = new UserSettings();
        ConfigureMutableSettings(settings);
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(poolPath)).Returns(true);
        var service = CreateService();

        var result = await service.EnsurePoolPathAsync([installation]);

        Assert.True(result);
        Assert.Equal(poolPath, settings.CasConfiguration.InstallationPoolRootPath);
    }

    /// <summary>
    /// Honors cancellation that arrives while resolving the pool and does not persist settings.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EnsurePoolPathAsync_WhenCancelledBeforeSave_DoesNotPersistSettings()
    {
        var installation = CreateInstallation();
        var poolPath = Path.Combine(installation.InstallationPath, DirectoryNames.GenHubCasPool);
        var settings = new UserSettings();
        ConfigureMutableSettings(settings);
        using var cancellationSource = new CancellationTokenSource();
        _writabilityProbe
            .Setup(probe => probe.CanCreateStorageAt(poolPath))
            .Callback(cancellationSource.Cancel)
            .Returns(true);
        var service = CreateService();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.EnsurePoolPathAsync([installation], cancellationSource.Token));

        _userSettingsService.Verify(
            userSettingsService => userSettingsService.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()),
            Times.Never);
        _poolManager.Verify(manager => manager.ReinitializeInstallationPool(), Times.Never);
    }

    /// <summary>
    /// Removes a cached installation pool from every enumeration path after it becomes unavailable.
    /// </summary>
    [Fact]
    public void CasPoolManager_WhenInstallationPoolBecomesUnavailable_DiscardsCachedStorage()
    {
        var primaryPath = Path.Combine(_tempPath, "primary");
        var installationPath = Path.Combine(_tempPath, "installation");
        var legacyPath = Path.Combine(_tempPath, "legacy");
        Directory.CreateDirectory(primaryPath);
        Directory.CreateDirectory(installationPath);
        Directory.CreateDirectory(legacyPath);
        var settings = new UserSettings
        {
            CasConfiguration = new CasConfiguration
            {
                InstallationPoolRootPath = installationPath,
                LegacyInstallationPoolRootPath = legacyPath,
            },
        };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        _writabilityProbe.Setup(probe => probe.CanCreateStorageAt(installationPath)).Returns(true);
        var resolver = new CasPoolResolver(
            Options.Create(new CasConfiguration { CasRootPath = primaryPath }),
            _userSettingsService.Object,
            _writabilityProbe.Object,
            NullLogger<CasPoolResolver>.Instance);
        var manager = new CasPoolManager(
            resolver,
            Options.Create(new CasConfiguration { CasRootPath = primaryPath }),
            new Mock<IFileHashProvider>().Object,
            NullLoggerFactory.Instance,
            _writabilityProbe.Object,
            NullLogger<CasPoolManager>.Instance);

        Assert.Equal(3, manager.GetAllStorages().Count);

        settings.CasConfiguration.InstallationPoolRootPath = string.Empty;
        manager.ReinitializeInstallationPool();

        Assert.Equal(2, manager.GetAllStorages().Count);
        Assert.Same(manager.GetStorage(CasPoolType.Primary), manager.GetStorage(CasPoolType.Installation));
    }

    /// <summary>
    /// Reads an existing legacy object without attempting to create writable CAS directories.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CasStorage_ObjectExistsAsync_DoesNotCreateWriteDirectories()
    {
        var rootPath = Path.Combine(_tempPath, "read-only-cas");
        var hash = new string('a', 64);
        var objectDirectory = Path.Combine(rootPath, "objects", "aa");
        Directory.CreateDirectory(objectDirectory);
        await File.WriteAllTextAsync(Path.Combine(objectDirectory, hash), "content");
        var storage = new CasStorage(
            Options.Create(new CasConfiguration { CasRootPath = rootPath }),
            NullLogger<CasStorage>.Instance,
            new Mock<IFileHashProvider>().Object);

        Assert.True(await storage.ObjectExistsAsync(hash));
        Assert.False(Directory.Exists(Path.Combine(rootPath, "temp")));
        Assert.False(Directory.Exists(Path.Combine(rootPath, "locks")));
    }

    /// <summary>
    /// Resolves content from the retained legacy pool after installation writes fall back to primary storage.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task CasService_GetContentPathAsync_FindsContentInLegacyPool()
    {
        var primaryPath = Path.Combine(_tempPath, "primary-lookup");
        var legacyPath = Path.Combine(_tempPath, "legacy-lookup");
        var hash = new string('b', 64);
        var objectDirectory = Path.Combine(legacyPath, "objects", "bb");
        Directory.CreateDirectory(primaryPath);
        Directory.CreateDirectory(objectDirectory);
        var expectedPath = Path.Combine(objectDirectory, hash);
        await File.WriteAllTextAsync(expectedPath, "legacy content");
        var settings = new UserSettings
        {
            CasConfiguration = new CasConfiguration
            {
                LegacyInstallationPoolRootPath = legacyPath,
            },
        };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var configuration = new CasConfiguration { CasRootPath = primaryPath };
        var resolver = new CasPoolResolver(
            Options.Create(configuration),
            _userSettingsService.Object,
            _writabilityProbe.Object,
            NullLogger<CasPoolResolver>.Instance);
        var fileHashProvider = new Mock<IFileHashProvider>();
        var manager = new CasPoolManager(
            resolver,
            Options.Create(configuration),
            fileHashProvider.Object,
            NullLoggerFactory.Instance,
            _writabilityProbe.Object,
            NullLogger<CasPoolManager>.Instance);
        var service = new CasService(
            manager.GetStorage(CasPoolType.Primary),
            new Mock<ICasReferenceTracker>().Object,
            NullLogger<CasService>.Instance,
            Options.Create(configuration),
            fileHashProvider.Object,
            new Mock<IStreamHashProvider>().Object,
            manager);

        var result = await service.GetContentPathAsync(hash, ManifestContentType.GameClient);

        Assert.True(result.Success);
        Assert.Equal(expectedPath, result.Data);
    }

    /// <summary>
    /// Does not expose a legacy CAS pool inside the application directory.
    /// </summary>
    [Fact]
    public void CasPoolManager_WhenLegacyPoolIsInsideApplicationDirectory_BlocksIt()
    {
        var primaryPath = Path.Combine(_tempPath, "primary-security");
        Directory.CreateDirectory(primaryPath);
        var settings = new UserSettings
        {
            CasConfiguration = new CasConfiguration
            {
                LegacyInstallationPoolRootPath = AppContext.BaseDirectory,
            },
        };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var configuration = new CasConfiguration { CasRootPath = primaryPath };
        var resolver = new CasPoolResolver(
            Options.Create(configuration),
            _userSettingsService.Object,
            _writabilityProbe.Object,
            NullLogger<CasPoolResolver>.Instance);

        var manager = new CasPoolManager(
            resolver,
            Options.Create(configuration),
            new Mock<IFileHashProvider>().Object,
            NullLoggerFactory.Instance,
            _writabilityProbe.Object,
            NullLogger<CasPoolManager>.Instance);

        Assert.Single(manager.GetAllStorages());
        Assert.StartsWith(
            primaryPath,
            manager.GetStorage(CasPoolType.Primary).GetObjectPath(new string('a', 64)),
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Avoids refreshing installation pools during ordinary cached storage lookups.
    /// </summary>
    [Fact]
    public void CasPoolManager_WhenPrimaryStorageIsCached_DoesNotRefreshInstallationPools()
    {
        var primaryPath = Path.Combine(_tempPath, "primary-cached");
        Directory.CreateDirectory(primaryPath);
        var resolver = new Mock<ICasPoolResolver>();
        resolver
            .Setup(service => service.GetPoolRootPath(CasPoolType.Primary))
            .Returns(primaryPath);
        resolver.Setup(service => service.IsInstallationPoolAvailable()).Returns(false);
        resolver.Setup(service => service.GetLegacyInstallationPoolRootPath()).Returns(string.Empty);
        var configuration = new CasConfiguration { CasRootPath = primaryPath };
        var manager = new CasPoolManager(
            resolver.Object,
            Options.Create(configuration),
            new Mock<IFileHashProvider>().Object,
            NullLoggerFactory.Instance,
            _writabilityProbe.Object,
            NullLogger<CasPoolManager>.Instance);
        resolver.Invocations.Clear();

        manager.GetStorage(CasPoolType.Primary);
        manager.GetStorage(CasPoolType.Primary);
        manager.GetAllStorages();

        resolver.Verify(service => service.IsInstallationPoolAvailable(), Times.Never);
        resolver.Verify(service => service.GetLegacyInstallationPoolRootPath(), Times.Never);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Directory.Delete(_tempPath, true);
        GC.SuppressFinalize(this);
    }

    private GameInstallation CreateInstallation(string directoryName = "Game")
    {
        var installationPath = Path.Combine(_tempPath, directoryName);
        Directory.CreateDirectory(installationPath);
        return new GameInstallation(installationPath, GameInstallationType.Steam);
    }

    private InstallationCasPoolService CreateService()
    {
        return new InstallationCasPoolService(
            _userSettingsService.Object,
            _writabilityProbe.Object,
            _poolManager.Object,
            NullLogger<InstallationCasPoolService>.Instance);
    }

    private void ConfigureMutableSettings(UserSettings settings)
    {
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        _userSettingsService
            .Setup(service => service.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .Returns<Func<UserSettings, bool>>(applyChanges => Task.FromResult(applyChanges(settings)));
    }
}
