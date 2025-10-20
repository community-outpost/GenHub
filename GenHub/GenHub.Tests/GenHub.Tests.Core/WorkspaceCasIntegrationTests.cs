using GenHub.Common.Services;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Storage;
using GenHub.Core.Models.Workspace;
using GenHub.Features.Storage.Services;
using GenHub.Features.Workspace;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;

namespace GenHub.Tests.Core;

/// <summary>
/// Integration tests for workspace and CAS (Content Addressable Storage) functionality.
/// </summary>
public class WorkspaceCasIntegrationTests : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IWorkspaceManager _workspaceManager;
    private readonly ICasService _casService;
    private readonly string _testWorkspacePath;
    private readonly string _testFilePath;
    private readonly string _testHash;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkspaceCasIntegrationTests"/> class and sets up test resources.
    /// </summary>
    public WorkspaceCasIntegrationTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Use a temp CAS root path with unique identifier
        var casRootPath = Path.Combine(Path.GetTempPath(), "GenHubTestCas", Guid.NewGuid().ToString());
        Directory.CreateDirectory(casRootPath);

        services.Configure<CasConfiguration>(config =>
        {
            config.CasRootPath = casRootPath;
            config.VerifyIntegrity = true;
        });

        // Mock ConfigurationProviderService instead of using real one
        var mockConfigProvider = new Mock<IConfigurationProviderService>();
        mockConfigProvider.Setup(x => x.GetContentStoragePath()).Returns(casRootPath);
        services.AddSingleton<IConfigurationProviderService>(mockConfigProvider.Object);

        // Register hash providers
        services.AddSingleton<IFileHashProvider, Sha256HashProvider>();
        services.AddSingleton<IStreamHashProvider, Sha256HashProvider>();

        // Register CAS storage and reference tracker
        services.AddSingleton<ICasStorage, CasStorage>();
        services.AddSingleton<CasReferenceTracker>();

        // Register CasService with all dependencies
        services.AddSingleton<ICasService, CasService>();

        // Register required dependencies for FileOperationsService
        var mockDownloadService = new Mock<IDownloadService>();
        mockDownloadService.Setup(x => x.ComputeFileHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>(async (path, ct) =>
            {
                var hashProvider = new Sha256HashProvider();
                return await hashProvider.ComputeFileHashAsync(path, ct);
            });
        services.AddSingleton<IDownloadService>(mockDownloadService.Object);

        services.AddSingleton<IFileOperationsService, FileOperationsService>();

        // Register workspace services
        services.AddWorkspaceServices();

        _serviceProvider = services.BuildServiceProvider();
        _workspaceManager = _serviceProvider.GetRequiredService<IWorkspaceManager>();
        _casService = _serviceProvider.GetRequiredService<ICasService>();
        _testWorkspacePath = Path.Combine(Path.GetTempPath(), "GenHubTestWorkspace", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testWorkspacePath);

        // Create test file and store in CAS with proper stream handling
        _testFilePath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(_testFilePath, "CAS test content");

            // Store in CAS using stream to ensure proper disposal
            using (var fileStream = File.OpenRead(_testFilePath))
            {
                var storeResult = _casService.StoreContentAsync(fileStream).GetAwaiter().GetResult();
                if (!storeResult.Success || string.IsNullOrEmpty(storeResult.Data))
                {
                    throw new InvalidOperationException($"Failed to store test content in CAS: {storeResult.FirstError}");
                }

                _testHash = storeResult.Data;
            }

            // Verify CAS storage worked
            var existsResult = _casService.ExistsAsync(_testHash).GetAwaiter().GetResult();
            if (!existsResult.Success || !existsResult.Data)
            {
                throw new InvalidOperationException($"CAS object {_testHash} was not properly stored");
            }
        }
        catch
        {
            FileOperationsService.DeleteFileIfExists(_testFilePath);
            throw;
        }
    }

    /// <summary>
    /// Releases resources used by the test, including temporary workspace and test files.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (_serviceProvider is IDisposable disposable)
                disposable.Dispose();

            FileOperationsService.DeleteDirectoryIfExists(_testWorkspacePath);
            FileOperationsService.DeleteFileIfExists(_testFilePath);

            // Clean up CAS directory
            var casConfig = _serviceProvider?.GetService<IOptions<CasConfiguration>>();
            if (casConfig?.Value?.CasRootPath != null && Directory.Exists(casConfig.Value.CasRootPath))
            {
                Directory.Delete(casConfig.Value.CasRootPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Tests that PrepareWorkspaceAsync creates correct symlink links for CAS content in the workspace.
    /// </summary>
    /// <returns>A task that represents the asynchronous test operation.</returns>
    [Fact]
    public async Task PrepareWorkspace_WithCasContent_CreatesCorrectLinks()
    {
        // Skip test if running on non-Windows or without admin privileges
        bool isWindows = OperatingSystem.IsWindows();
        bool isAdmin = isWindows &&
            new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        if (!isWindows || !isAdmin)
        {
            return; // Skip test on non-Windows or non-admin
        }

        // The CAS object should already be verified to exist from constructor
        Assert.NotNull(_testHash);
        Assert.NotEmpty(_testHash);

        // Debug: Verify we can get the CAS path
        var casPathResult = await _casService.GetContentPathAsync(_testHash);
        Assert.True(casPathResult.Success, $"Should be able to get CAS path for {_testHash}: {casPathResult.FirstError}");
        Assert.True(File.Exists(casPathResult.Data), $"CAS file should exist at {casPathResult.Data}");

        var manifest = new ContentManifest
        {
            Id = "1.0.genhub.manifest",
            Files = new List<ManifestFile>
            {
                new ManifestFile
                {
                    RelativePath = "data/mymod.big",
                    Hash = _testHash,
                    SourceType = ContentSourceType.ContentAddressable,
                    Size = 16,
                },
            },
        };

        var config = new WorkspaceConfiguration
        {
            Id = "test-workspace",
            Manifests = new List<ContentManifest> { manifest },
            Strategy = WorkspaceStrategy.SymlinkOnly,
            WorkspaceRootPath = _testWorkspacePath,
            BaseInstallationPath = _testWorkspacePath,
            GameClient = new GameClient
            {
                Id = "test-version",
                Name = "Test Version",
            },
        };

        var result = await _workspaceManager.PrepareWorkspaceAsync(config);
        Assert.True(result.Success, $"Workspace preparation failed: {result.FirstError}");
        var expectedPath = Path.Combine(result.Data!.WorkspacePath, "data", "mymod.big");

        Assert.True(File.Exists(expectedPath), $"Expected file {expectedPath} does not exist");
    }
}
