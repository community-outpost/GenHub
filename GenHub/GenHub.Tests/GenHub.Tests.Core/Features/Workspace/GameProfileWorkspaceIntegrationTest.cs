using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Integration tests verifying game profile workspace integration.
/// </summary>
public class GameProfileWorkspaceIntegrationTest : IDisposable
{
    private readonly string _tempGameInstall;
    private readonly string _tempWorkspaceRoot;
    private readonly string _tempContentStorage;
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly Mock<IGameInstallationService> _mockInstallationService;
    private readonly Mock<IGameProfileManager> _mockProfileManager;
    private bool _disposed = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileWorkspaceIntegrationTest"/> class.
    /// Sets up temporary directories, services, and mock objects for testing.
    /// </summary>
    public GameProfileWorkspaceIntegrationTest()
    {
        _tempGameInstall = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempWorkspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempContentStorage = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        Directory.CreateDirectory(_tempGameInstall);
        Directory.CreateDirectory(_tempWorkspaceRoot);
        Directory.CreateDirectory(_tempContentStorage);

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));

        // Add core services
        var mockDownloadService = new Mock<IDownloadService>();
        services.AddSingleton<IDownloadService>(mockDownloadService.Object);
        services.AddSingleton<IFileHashProvider, Sha256HashProvider>();
        services.AddSingleton<IStreamHashProvider, Sha256HashProvider>();

        // Configure CAS for WorkspaceManager dependency
        services.Configure<CasConfiguration>(config =>
        {
            config.CasRootPath = _tempContentStorage;
        });

        // Mock ConfigurationProviderService - required by WorkspaceManager
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns(_tempContentStorage);
        services.AddSingleton<IConfigurationProviderService>(mockConfigProvider.Object);

        // Register CAS reference tracker (required by WorkspaceManager)
        services.AddSingleton<CasReferenceTracker>();

        // Mock services - register before AddWorkspaceServices to avoid dependency issues
        _mockInstallationService = new Mock<IGameInstallationService>();
        _mockProfileManager = new Mock<IGameProfileManager>();
        var mockCasService = new Mock<ICasService>();
        services.AddSingleton<IGameInstallationService>(_mockInstallationService.Object);
        services.AddSingleton<IGameProfileManager>(_mockProfileManager.Object);
        services.AddSingleton<ICasService>(mockCasService.Object);

        services.AddWorkspaceServices();

        _serviceProvider = services.BuildServiceProvider();
        _workspaceManager = _serviceProvider.GetRequiredService<IWorkspaceManager>();

        SetupTestFiles();
        SetupMockServices();
    }

    /// <summary>
    /// Tests that workspace preparation correctly handles game installation files with FullCopy strategy.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_FullCopyStrategy_CopiesGameInstallationAndClientFiles()
    {
        // Arrange
        var manifests = CreateTestManifests();
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-fullcopy",
            Manifests = manifests,
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success, $"Workspace preparation failed: {string.Join(", ", result.Errors)}");
        Assert.NotNull(result.Data);

        var workspaceInfo = result.Data;
        var workspacePath = workspaceInfo.WorkspacePath;

        // Verify workspace was created
        Assert.True(Directory.Exists(workspacePath), "Workspace directory should exist");

        // Verify game installation files were copied
        Assert.True(File.Exists(Path.Combine(workspacePath, "generals.exe")), "Game executable should be copied");
        Assert.True(File.Exists(Path.Combine(workspacePath, "data", "generals.big")), "Game data files should be copied");
        Assert.True(File.Exists(Path.Combine(workspacePath, "data", "textures", "texture1.tga")), "Game texture files should be copied");

        // Verify executable path is set correctly
        Assert.Equal(Path.Combine(workspacePath, "generals.exe"), workspaceInfo.ExecutablePath);
        Assert.Equal(workspacePath, workspaceInfo.WorkingDirectory);

        // Verify file count and size
        Assert.True(workspaceInfo.FileCount > 0, "File count should be greater than 0");
        Assert.True(workspaceInfo.TotalSizeBytes > 0, "Total size should be greater than 0");
    }

    /// <summary>
    /// Tests that workspace preparation correctly handles game installation files with SymlinkOnly strategy.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_SymlinkStrategy_LinksGameInstallationAndClientFiles()
    {
        // Skip on systems that don't support symlinks
        bool isWindows = OperatingSystem.IsWindows();
        bool isAdmin = isWindows &&
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        if (!isWindows || !isAdmin)
        {
            return;
        }

        // Arrange
        var manifests = CreateTestManifests();
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-symlink",
            Manifests = manifests,
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.SymlinkOnly,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success, $"Workspace preparation failed: {string.Join(", ", result.Errors)}");
        Assert.NotNull(result.Data);

        var workspaceInfo = result.Data;
        var workspacePath = workspaceInfo.WorkspacePath;

        // Verify workspace was created
        Assert.True(Directory.Exists(workspacePath), "Workspace directory should exist");

        // Verify game files are accessible (via symlinks)
        Assert.True(File.Exists(Path.Combine(workspacePath, "generals.exe")), "Game executable should be accessible");
        Assert.True(File.Exists(Path.Combine(workspacePath, "data", "generals.big")), "Game data files should be accessible");

        // Verify executable path is set correctly
        Assert.Equal(Path.Combine(workspacePath, "generals.exe"), workspaceInfo.ExecutablePath);
        Assert.Equal(workspacePath, workspaceInfo.WorkingDirectory);
    }

    /// <summary>
    /// Tests that workspace preparation correctly handles both GameInstallation and GameClient content types.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_MixedContentTypes_HandlesGameInstallationAndGameClientCorrectly()
    {
        bool isWindows = OperatingSystem.IsWindows();
        bool isAdmin = isWindows &&
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        if (!isWindows || !isAdmin)
        {
            return;
        }

        // Arrange - Create manifests with both GameInstallation and GameClient content
        var gameInstallationManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.gameinstallation.testgeneinstall"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
            TargetGame = GameType.Generals,
            Files = new List<ManifestFile>
            {
                // GameInstallation files have complete SourcePath
                new ManifestFile
                {
                    RelativePath = "generals.exe",
                    SourcePath = Path.Combine(_tempGameInstall, "generals.exe"), // Complete path
                    SourceType = GenHub.Core.Models.Enums.ContentSourceType.GameInstallation,
                    Size = new FileInfo(Path.Combine(_tempGameInstall, "generals.exe")).Length,
                    IsExecutable = true,
                },
                new ManifestFile
                {
                    RelativePath = "data/generals.big",
                    SourcePath = Path.Combine(_tempGameInstall, "data", "generals.big"), // Complete path
                    SourceType = GenHub.Core.Models.Enums.ContentSourceType.GameInstallation,
                    Size = new FileInfo(Path.Combine(_tempGameInstall, "data", "generals.big")).Length,
                },
            },
        };

        var gameClientManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.gameclient.testgameclient"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameClient,
            TargetGame = GameType.Generals,
            Files = new List<ManifestFile>
            {
                // GameClient files might use RelativePath with BaseInstallationPath
                new ManifestFile
                {
                    RelativePath = "generals.exe",
                    SourceType = GenHub.Core.Models.Enums.ContentSourceType.GameInstallation,
                    Size = new FileInfo(Path.Combine(_tempGameInstall, "generals.exe")).Length,
                    IsExecutable = true,
                },
            },
        };

        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-mixed",
            Manifests = new List<ContentManifest> { gameInstallationManifest, gameClientManifest },
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.HybridCopySymlink,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success, $"Workspace preparation failed: {string.Join(", ", result.Errors)}");
        Assert.NotNull(result.Data);

        var workspaceInfo = result.Data;
        var workspacePath = workspaceInfo.WorkspacePath;

        // Verify both GameInstallation and GameClient files are present
        Assert.True(File.Exists(Path.Combine(workspacePath, "generals.exe")), "GameInstallation executable should be present");
        Assert.True(File.Exists(Path.Combine(workspacePath, "data", "generals.big")), "GameInstallation data should be present");
        Assert.True(File.Exists(Path.Combine(workspacePath, "generals.exe")), "GameClient executable should be present");

        // Verify executable path is set correctly
        // With the new implementation, GameClient manifest takes priority for executable resolution
        Assert.Equal(Path.Combine(workspacePath, "generals.exe"), workspaceInfo.ExecutablePath);
    }

    /// <summary>
    /// Tests that all workspace strategies handle game installation files correctly.
    /// </summary>
    /// <param name="strategy">The workspace strategy to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public async Task PrepareWorkspace_AllStrategies_HandleGameInstallationFiles(WorkspaceStrategy strategy)
    {
        bool isWindows = OperatingSystem.IsWindows();
        bool isAdmin = isWindows &&
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        // Skip symlink strategies when not admin on Windows
        if ((strategy == WorkspaceStrategy.SymlinkOnly || strategy == WorkspaceStrategy.HybridCopySymlink) &&
            (!isWindows || !isAdmin))
        {
            return;
        }

        // Skip HardLink strategy on Windows in Core tests - the base FileOperationsService
        // doesn't support hard links on Windows, use WindowsFileOperationsService instead
        if (strategy == WorkspaceStrategy.HardLink && isWindows)
        {
            return;
        }

        // Arrange
        var manifests = CreateTestManifests();
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = $"test-workspace-{strategy.ToString().ToLower()}",
            Manifests = manifests,
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = strategy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success, $"Workspace preparation failed for {strategy}: {string.Join(", ", result.Errors)}");
        Assert.NotNull(result.Data);

        var workspaceInfo = result.Data;
        var workspacePath = workspaceInfo.WorkspacePath;

        // Verify workspace was created
        Assert.True(Directory.Exists(workspacePath), $"Workspace directory should exist for {strategy}");

        // Verify game files are accessible
        Assert.True(File.Exists(Path.Combine(workspacePath, "generals.exe")), $"Game executable should be accessible for {strategy}");
        Assert.True(File.Exists(Path.Combine(workspacePath, "data", "generals.big")), $"Game data files should be accessible for {strategy}");

        // Verify executable path is set correctly
        Assert.Equal(Path.Combine(workspacePath, "generals.exe"), workspaceInfo.ExecutablePath);
        Assert.Equal(workspacePath, workspaceInfo.WorkingDirectory);
    }

    /// <summary>
    /// Tests that workspace cleanup removes all created files and directories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task CleanupWorkspace_RemovesAllFiles()
    {
        // Arrange
        var manifests = CreateTestManifests();
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-cleanup",
            Manifests = manifests,
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        var prepareResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);
        Assert.True(prepareResult.Success);
        Assert.NotNull(prepareResult.Data);
        var workspacePath = prepareResult.Data!.WorkspacePath;

        // Act
        var cleanupResult = await _workspaceManager.CleanupWorkspaceAsync(workspaceConfig.Id);

        // Assert
        Assert.True(cleanupResult.Success);
        Assert.False(Directory.Exists(workspacePath), "Workspace should be deleted after cleanup");
    }

    /// <summary>
    /// Tests that workspace preparation handles empty manifests gracefully.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_EmptyManifests_CreatesEmptyWorkspace()
    {
        // Arrange
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-empty",
            Manifests = new List<ContentManifest>(),
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(Directory.Exists(result.Data.WorkspacePath));
    }

    /// <summary>
    /// Tests that workspace preparation with ForceRecreate removes existing workspace.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_ForceRecreate_RemovesExistingWorkspace()
    {
        // Arrange
        var manifests = CreateTestManifests();
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-recreate",
            Manifests = manifests,
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = false,
        };

        // Create initial workspace
        var firstResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);
        Assert.True(firstResult.Success);
        var workspacePath = firstResult.Data!.WorkspacePath;
        var markerFile = Path.Combine(workspacePath, "marker.txt");
        File.WriteAllText(markerFile, "test");

        // Act - Recreate with ForceRecreate
        workspaceConfig.ForceRecreate = true;
        var secondResult = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(secondResult.Success);
        Assert.False(File.Exists(markerFile), "Marker file should be removed after recreation");
    }

    /// <summary>
    /// Tests that workspace preparation handles nested directory structures.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_NestedDirectories_PreservesStructure()
    {
        // Arrange
        var manifests = CreateTestManifests();
        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-nested",
            Manifests = manifests,
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success);
        var workspacePath = result.Data!.WorkspacePath;
        Assert.True(Directory.Exists(Path.Combine(workspacePath, "data")));
        Assert.True(Directory.Exists(Path.Combine(workspacePath, "data", "textures")));
        Assert.True(Directory.Exists(Path.Combine(workspacePath, "mods", "mod1")));
    }

    /// <summary>
    /// Tests that workspace preparation handles large files correctly.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_LargeFiles_CopiesSuccessfully()
    {
        // Arrange - Create a larger test file
        var largeFilePath = Path.Combine(_tempGameInstall, "large.dat");
        var largeFileSize = 1024 * 1024; // 1MB
        File.WriteAllBytes(largeFilePath, new byte[largeFileSize]);

        var manifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.gameinstallation.largefile"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
            Files = new List<ManifestFile>
            {
                new ManifestFile
                {
                    RelativePath = "large.dat",
                    SourcePath = largeFilePath,
                    SourceType = GenHub.Core.Models.Enums.ContentSourceType.GameInstallation,
                    Size = largeFileSize,
                },
            },
        };

        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-large",
            Manifests = new List<ContentManifest> { manifest },
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success);
        var copiedFile = Path.Combine(result.Data!.WorkspacePath, "large.dat");
        Assert.True(File.Exists(copiedFile));
        Assert.Equal(largeFileSize, new FileInfo(copiedFile).Length);
    }

    /// <summary>
    /// Tests that workspace preparation handles multiple manifests with overlapping files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_OverlappingManifests_HandlesCorrectly()
    {
        // Arrange
        var manifest1 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.gameinstallation.testmanifestone"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
            Files = new List<ManifestFile>
            {
                new ManifestFile
                {
                    RelativePath = "generals.exe",
                    SourcePath = Path.Combine(_tempGameInstall, "generals.exe"),
                    SourceType = GenHub.Core.Models.Enums.ContentSourceType.GameInstallation,
                    Size = new FileInfo(Path.Combine(_tempGameInstall, "generals.exe")).Length,
                },
            },
        };

        var manifest2 = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.mod.testmanifesttwo"),
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Files = new List<ManifestFile>
            {
                new ManifestFile
                {
                    RelativePath = "mods/mod1/mod.ini",
                    SourcePath = Path.Combine(_tempGameInstall, "mods", "mod1", "mod.ini"),
                    SourceType = GenHub.Core.Models.Enums.ContentSourceType.GameInstallation,
                    Size = new FileInfo(Path.Combine(_tempGameInstall, "mods", "mod1", "mod.ini")).Length,
                },
            },
        };

        var workspaceConfig = new WorkspaceConfiguration
        {
            Id = "test-workspace-overlap",
            Manifests = new List<ContentManifest> { manifest1, manifest2 },
            GameClient = new GameClient
            {
                Id = "generals-108",
                ExecutablePath = "generals.exe",
                WorkingDirectory = _tempGameInstall,
            },
            Strategy = WorkspaceStrategy.FullCopy,
            BaseInstallationPath = _tempGameInstall,
            WorkspaceRootPath = _tempWorkspaceRoot,
            ForceRecreate = true,
        };

        // Act
        var result = await _workspaceManager.PrepareWorkspaceAsync(workspaceConfig);

        // Assert
        Assert.True(result.Success);
        var workspacePath = result.Data!.WorkspacePath;
        Assert.True(File.Exists(Path.Combine(workspacePath, "generals.exe")));
        Assert.True(File.Exists(Path.Combine(workspacePath, "mods", "mod1", "mod.ini")));
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (Directory.Exists(_tempGameInstall))
            {
                Directory.Delete(_tempGameInstall, true);
            }
        }
        catch
        {
            /* Ignore cleanup errors */
        }

        try
        {
            if (Directory.Exists(_tempWorkspaceRoot))
            {
                Directory.Delete(_tempWorkspaceRoot, true);
            }
        }
        catch
        {
            /* Ignore cleanup errors */
        }

        try
        {
            if (Directory.Exists(_tempContentStorage))
            {
                Directory.Delete(_tempContentStorage, true);
            }
        }
        catch
        {
            /* Ignore cleanup errors */
        }

        if (_serviceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }

        GC.SuppressFinalize(this);
    }

    private void SetupTestFiles()
    {
        // Create test game installation files
        var testFiles = new[]
        {
            "generals.exe",
            "generals.exe",
            "data/generals.big",
            "data/textures/texture1.tga",
            "mods/mod1/mod.ini",
        };

        foreach (var file in testFiles)
        {
            var fullPath = Path.Combine(_tempGameInstall, file);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) !);
            File.WriteAllText(fullPath, $"Test content for {file}");
        }
    }

    private void SetupMockServices()
    {
        // Setup game installation mock
        var gameInstallation = new GameInstallation(
            _tempGameInstall,
            GameInstallationType.Steam,
            null)
        {
            Id = "test-installation-123",
            HasGenerals = true,
            GeneralsPath = _tempGameInstall,
            AvailableGameClients = new List<GameClient>
            {
                new GameClient
                {
                    Id = "generals-108",
                    Name = "Generals 1.08",
                    Version = "1.08",
                    ExecutablePath = Path.Combine(_tempGameInstall, "generals.exe"),
                    GameType = GameType.Generals,
                    InstallationId = "test-installation-123",
                    WorkingDirectory = _tempGameInstall,
                },
            },
        };

        _mockInstallationService
            .Setup(x => x.GetInstallationAsync("test-installation-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameInstallation>.CreateSuccess(gameInstallation));

        // Setup game profile mock
        var gameProfile = new GameProfile
        {
            Id = "test-profile-456",
            Name = "Test Profile",
            GameInstallationId = "test-installation-123",
            GameClient = gameInstallation.AvailableGameClients.First(),
            EnabledContentIds = new List<string> { "1.0.genhub.gameinstallation.testgeneinstall", "1.0.genhub.gameclient.testgameclient" },
            WorkspaceStrategy = WorkspaceStrategy.FullCopy,
        };

        _mockProfileManager
            .Setup(x => x.GetProfileAsync("test-profile-456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(gameProfile));
    }

    private List<ContentManifest> CreateTestManifests()
    {
        var gameInstallationManifest = new ContentManifest
        {
            Id = ManifestId.Create("1.0.genhub.gameinstallation.testgeneinstall"),
            ContentType = GenHub.Core.Models.Enums.ContentType.GameInstallation,
            Files = new List<ManifestFile>(),
        };

        // Add files with complete SourcePath (typical for GameInstallation content)
        var testFiles = new[] { "generals.exe", "generals.exe", "data/generals.big", "data/textures/texture1.tga", "mods/mod1/mod.ini" };
        foreach (var file in testFiles)
        {
            var fullPath = Path.Combine(_tempGameInstall, file);
            if (File.Exists(fullPath))
            {
                gameInstallationManifest.Files.Add(new ManifestFile
                {
                    RelativePath = file,
                    SourcePath = fullPath, // Complete path for GameInstallation content
                    SourceType = GenHub.Core.Models.Enums.ContentSourceType.GameInstallation,
                    Size = new FileInfo(fullPath).Length,
                    IsExecutable = file.EndsWith(".exe"),
                });
            }
        }

        return new List<ContentManifest> { gameInstallationManifest };
    }
}
