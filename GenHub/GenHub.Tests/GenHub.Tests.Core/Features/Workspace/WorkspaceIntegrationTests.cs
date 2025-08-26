using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameVersions;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Workspace;
using GenHub.Features.Workspace.Strategies;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.Workspace;

/// <summary>
/// Integration tests for workspace operations.
/// </summary>
public class WorkspaceIntegrationTests : IDisposable
{
    private readonly string _tempGameInstall;
    private readonly string _tempWorkspaceRoot;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceIntegrationTests"/> class.
    /// </summary>
    public WorkspaceIntegrationTests()
        {
        _tempGameInstall = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempWorkspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        // Add mock download service
        var mockDownloadService = new Mock<IDownloadService>();
        services.AddSingleton(mockDownloadService.Object);

        // Add configuration services
        var mockConfiguration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        var mockAppConfig = new Mock<IAppConfiguration>();
        var mockUserSettings = new Mock<IUserSettingsService>();

        // Setup mock returns
        mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns(Path.Combine(Path.GetTempPath(), "GenHub"));
        mockAppConfig.Setup(x => x.GetDefaultWorkspacePath()).Returns(_tempWorkspaceRoot);
        mockUserSettings.Setup(x => x.GetSettings()).Returns(new UserSettings());

        services.AddSingleton(mockConfiguration.Object);
        services.AddSingleton(mockAppConfig.Object);
        services.AddSingleton(mockUserSettings.Object);
        services.AddSingleton<IConfigurationProviderService, ConfigurationProviderService>();

        // Add workspace services
        services.AddWorkspaceServices();

        _serviceProvider = services.BuildServiceProvider();
        SetupTestGameInstallation().Wait();
    }

    /// <summary>
    /// Tests end-to-end workspace creation for all strategies.
    /// </summary>
    /// <param name="strategy">The workspace strategy to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public async Task EndToEndWorkspaceCreation_AllStrategies(WorkspaceStrategy strategy)
        {
        var manager = _serviceProvider.GetRequiredService<IWorkspaceManager>();
        var config = CreateTestConfiguration(strategy);

        bool isWindows = OperatingSystem.IsWindows();
        bool isAdmin = isWindows &&
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        if ((strategy == WorkspaceStrategy.SymlinkOnly ||
             strategy == WorkspaceStrategy.HardLink ||
             strategy == WorkspaceStrategy.HybridCopySymlink) &&
            (!isWindows || !isAdmin))
        {
            return;
        }

        var workspace = await manager.PrepareWorkspaceAsync(config);

        Assert.True(Directory.Exists(workspace.WorkspacePath));
        Assert.True(File.Exists(workspace.ExecutablePath));
        Assert.Equal(strategy, workspace.Strategy);
        Assert.True(workspace.FileCount > 0);

        await VerifyWorkspaceStrategy(workspace, strategy);
    }

    /// <summary>
    /// Tests workspace directory creation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_CreatesDirectory()
    {
        var mockDownloadService = new Mock<IDownloadService>();
        var fileOps = new FileOperationsService(
            new Mock<ILogger<FileOperationsService>>().Object,
            mockDownloadService.Object);
        var logger = new Mock<ILogger<HybridCopySymlinkStrategy>>();
        var strategy = new HybridCopySymlinkStrategy(fileOps, logger.Object);

        // Create proper mock for IConfigurationProviderService
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns(_tempWorkspaceRoot);
        mockConfigProvider.Setup(x => x.GetWorkspacePath()).Returns(_tempWorkspaceRoot);

        var mockLogger = new Mock<ILogger<WorkspaceManager>>().Object;
        var manager = new WorkspaceManager([strategy], mockConfigProvider.Object, mockLogger);

        var config = CreateTestConfiguration(WorkspaceStrategy.HybridCopySymlink);

        var info = await manager.PrepareWorkspaceAsync(config);

        var expected = Path.GetFullPath(config.WorkspaceRootPath).TrimEnd(Path.DirectorySeparatorChar);
        var actual = Path.GetFullPath(Path.GetDirectoryName(info.WorkspacePath) ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Performs cleanup by disposing of temporary resources.
    /// </summary>
    public void Dispose()
        {
        try
        {
            if (Directory.Exists(_tempGameInstall))
        {
                Directory.Delete(_tempGameInstall, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
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
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Verifies workspace file link status based on strategy.
    /// </summary>
    /// <param name="workspace">The workspace info.</param>
    /// <param name="strategy">The workspace strategy.</param>
    /// <returns>A completed <see cref="Task"/>.</returns>
    private static Task VerifyWorkspaceStrategy(WorkspaceInfo workspace, WorkspaceStrategy strategy)
        {
        var testFile = Directory.GetFiles(workspace.WorkspacePath, "*.exe").First();
        var fileInfo = new FileInfo(testFile);

        switch (strategy)
        {
            case WorkspaceStrategy.FullCopy:
                Assert.Null(fileInfo.LinkTarget);
                break;
            case WorkspaceStrategy.SymlinkOnly:
                Assert.NotNull(fileInfo.LinkTarget);
                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates a test workspace configuration.
    /// </summary>
    /// <param name="strategy">The workspace strategy.</param>
    /// <returns>A configured <see cref="WorkspaceConfiguration"/>.</returns>
    private WorkspaceConfiguration CreateTestConfiguration(WorkspaceStrategy strategy)
        {
        var manifest = new ContentManifest();
        var testFiles = new[]
        {
            "generals.exe",
            "game.dat",
            "data/textures/texture1.tga",
            "data/audio/sound1.wav",
            "mods/mod1/mod.ini",
        };

        foreach (var file in testFiles)
        {
            var fullPath = Path.Combine(_tempGameInstall, file);
            var fi = new FileInfo(fullPath);
            manifest.Files.Add(new ManifestFile
            {
                RelativePath = file,
                Size = fi.Exists ? fi.Length : 0,
                IsExecutable = file.EndsWith(".exe"),
            });
        }

        return new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            GameVersion = new GameVersion
            {
                Id = "test-version",
                Name = "Test Version",
            },
            WorkspaceRootPath = _tempWorkspaceRoot,
            Strategy = strategy,
            BaseInstallationPath = _tempGameInstall,
            Manifest = manifest,
        };
    }

    /// <summary>
    /// Sets up the test game installation files and directories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous setup.</returns>
    private async Task SetupTestGameInstallation()
        {
        Directory.CreateDirectory(_tempGameInstall);
        var testFiles = new[]
        {
            "generals.exe",
            "game.dat",
            "data/textures/texture1.tga",
            "data/audio/sound1.wav",
            "mods/mod1/mod.ini",
        };

        foreach (var file in testFiles)
        {
            var fullPath = Path.Combine(_tempGameInstall, file);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) !);
            await File.WriteAllTextAsync(fullPath, $"Test content for {file}");
        }
    }
}
