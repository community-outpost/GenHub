using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Validation;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
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
    private readonly IWorkspaceValidator _workspaceValidator;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceIntegrationTests"/> class.
    /// </summary>
    public WorkspaceIntegrationTests()
    {
        _tempGameInstall = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _tempWorkspaceRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());

        // Add mock download service for FileOperationsService
        var mockDownloadService = new Mock<IDownloadService>();
        services.AddSingleton<IDownloadService>(mockDownloadService.Object);

        // Register hash providers
        services.AddSingleton<IFileHashProvider, Sha256HashProvider>();
        services.AddSingleton<IStreamHashProvider, Sha256HashProvider>();

        // Register CAS storage and reference tracker
        services.Configure<CasConfiguration>(config =>
        {
            config.CasRootPath = _tempWorkspaceRoot;
        });

        // Mock ConfigurationProviderService instead of using real one
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns(_tempWorkspaceRoot);
        services.AddSingleton<IConfigurationProviderService>(mockConfigProvider.Object);

        services.AddSingleton<ICasStorage, CasStorage>();
        services.AddSingleton<CasReferenceTracker>();
        services.AddSingleton<ICasService, CasService>();

        // Register FileOperationsService for workspace strategies
        services.AddSingleton<IFileOperationsService, FileOperationsService>();

        // Add configuration services
        var mockConfiguration = new Mock<Microsoft.Extensions.Configuration.IConfiguration>();
        var mockAppConfig = new Mock<IAppConfiguration>();
        var mockUserSettings = new Mock<IUserSettingsService>();

        // Setup mock returns
        mockAppConfig.Setup(x => x.GetConfiguredDataPath()).Returns(Path.Combine(Path.GetTempPath(), "GenHub"));
        mockAppConfig.Setup(x => x.GetDefaultWorkspacePath()).Returns(_tempWorkspaceRoot);
        mockUserSettings.Setup(x => x.Get()).Returns(new UserSettings());

        services.AddSingleton(mockConfiguration.Object);
        services.AddSingleton(mockAppConfig.Object);
        services.AddSingleton(mockUserSettings.Object);
        services.AddSingleton<IConfigurationProviderService, ConfigurationProviderService>();

        // Add workspace services
        services.AddWorkspaceServices();

        _serviceProvider = services.BuildServiceProvider();
        _workspaceValidator = _serviceProvider.GetRequiredService<IWorkspaceValidator>();
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

        var result = await manager.PrepareWorkspaceAsync(config);

        // Assert
        Assert.True(result.Success, result.FirstError ?? "Workspace preparation failed with an unknown error.");
        Assert.True(Directory.Exists(result.Data!.WorkspacePath));
        Assert.NotNull(result.Data.ExecutablePath);
        Assert.Equal(strategy, result.Data.Strategy);
        Assert.True(result.Data.FileCount > 0);

        // Cleanup
        var validationResult = await _workspaceValidator.ValidateWorkspaceAsync(result.Data!);
        Assert.True(validationResult.Success);
        Assert.NotNull(validationResult.Data);

        // Test GetAllWorkspacesAsync with new return type
        var allWorkspacesResult = await manager.GetAllWorkspacesAsync();
        Assert.True(allWorkspacesResult.Success);
        Assert.NotNull(allWorkspacesResult.Data);
        Assert.Contains(allWorkspacesResult.Data, w => w.Id == result.Data.Id);
    }

    /// <summary>
    /// Tests workspace directory creation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task PrepareWorkspaceAsync_CreatesDirectory()
    {
        var mockDownloadService = new Mock<IDownloadService>();
        var mockCasService = new Mock<ICasService>();
        var fileOps = new FileOperationsService(
            new Mock<ILogger<FileOperationsService>>().Object,
            mockDownloadService.Object,
            mockCasService.Object);

        var logger = new Mock<ILogger<FullCopyStrategy>>();
        var strategy = new FullCopyStrategy(fileOps, logger.Object);

        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns(_tempWorkspaceRoot);
        mockConfigProvider.Setup(x => x.GetWorkspacePath()).Returns(_tempWorkspaceRoot);

        var mockLogger = new Mock<ILogger<WorkspaceManager>>().Object;
        var mockReconcilerLogger = new Mock<ILogger<WorkspaceReconciler>>().Object;

        // Use a real CasReferenceTracker with dummy dependencies
        var dummyLogger = new Mock<ILogger<CasReferenceTracker>>().Object;
        var dummyOptions = new Mock<Microsoft.Extensions.Options.IOptions<CasConfiguration>>();
        dummyOptions.Setup(x => x.Value).Returns(new CasConfiguration { CasRootPath = _tempWorkspaceRoot });
        var casReferenceTracker = new CasReferenceTracker(dummyOptions.Object, dummyLogger);

        var mockWorkspaceValidator = new Mock<IWorkspaceValidator>();
        mockWorkspaceValidator.Setup(x => x.ValidateWorkspaceAsync(It.IsAny<WorkspaceInfo>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ValidationResult>.CreateSuccess(new ValidationResult("test", new List<ValidationIssue>())));
        mockWorkspaceValidator.Setup(x => x.ValidatePrerequisitesAsync(It.IsAny<IWorkspaceStrategy>(), It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));
        mockWorkspaceValidator.Setup(x => x.ValidateConfigurationAsync(It.IsAny<WorkspaceConfiguration>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ValidationResult("test", new List<ValidationIssue>()));

        // Create WorkspaceReconciler
        var workspaceReconciler = new WorkspaceReconciler(mockReconcilerLogger);

        var manager = new WorkspaceManager([strategy], mockConfigProvider.Object, mockLogger, casReferenceTracker, mockWorkspaceValidator.Object, workspaceReconciler);

        var config = CreateTestConfiguration(WorkspaceStrategy.FullCopy);

        // Act
        var result = await manager.PrepareWorkspaceAsync(config, null, CancellationToken.None);

        // Assert
        Assert.True(result.Success, $"Workspace preparation failed: {(result.HasErrors ? result.FirstError : "An unknown error occurred.")}");
        Assert.NotNull(result.Data);
        Assert.True(Directory.Exists(result.Data.WorkspacePath));
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
            "game.exe",
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
                SourceType = ContentSourceType.LocalFile,
            });
        }

        var gameClient = new GameClient
        {
            Id = "test-version",
            Name = "Test Version",
            ExecutablePath = "generals.exe", // Use relative path
            GameType = GameType.Generals,
        };

        return new WorkspaceConfiguration
        {
            Id = Guid.NewGuid().ToString(),
            GameClient = gameClient,
            WorkspaceRootPath = _tempWorkspaceRoot,
            Strategy = strategy,
            BaseInstallationPath = _tempGameInstall,
            Manifests = new List<ContentManifest> { manifest },
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
            "game.exe",
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
