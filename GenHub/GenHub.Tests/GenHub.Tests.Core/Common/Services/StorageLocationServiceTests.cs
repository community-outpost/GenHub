using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests writable storage path resolution.
/// </summary>
public sealed class StorageLocationServiceTests : IDisposable
{
    private readonly Mock<IUserSettingsService> _userSettingsService = new();
    private readonly Mock<IConfigurationProviderService> _configurationProviderService = new();
    private readonly Mock<IGameInstallationService> _gameInstallationService = new();
    private readonly string _applicationDataPath;
    private readonly string _centralCasPath;
    private readonly string _tempPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageLocationServiceTests"/> class.
    /// </summary>
    public StorageLocationServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _applicationDataPath = Path.Combine(_tempPath, "AppData");
        _centralCasPath = Path.Combine(_applicationDataPath, DirectoryNames.CasPool);
        Directory.CreateDirectory(_applicationDataPath);

        _configurationProviderService.Setup(service => service.GetApplicationDataPath()).Returns(_applicationDataPath);
        _configurationProviderService
            .Setup(service => service.GetCasConfiguration())
            .Returns(new CasConfiguration { CasRootPath = _centralCasPath });
    }

    /// <summary>
    /// Uses installation-adjacent storage when its parent is writable.
    /// </summary>
    [Fact]
    public void GetStoragePaths_WhenInstallationParentIsWritable_UsesAdjacentPaths()
    {
        var settings = new UserSettings { UseInstallationAdjacentStorage = true };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var installationRoot = Path.Combine(_tempPath, "EA Games");
        var installationPath = Path.Combine(installationRoot, "Command and Conquer Generals Zero Hour");
        Directory.CreateDirectory(installationPath);
        var installation = new GameInstallation(installationPath, GameInstallationType.EaApp);

        var workspacePath = service.GetWorkspacePath(installation);
        var casPath = service.GetCasPoolPath(installation);

        Assert.Equal(Path.Combine(installationRoot, DirectoryNames.GenHubWorkspace), workspacePath);
        Assert.Equal(Path.Combine(installationRoot, DirectoryNames.GenHubCasPool), casPath);
        Assert.Empty(Directory.GetFiles(installationRoot, ".genhub-write-probe-*"));
    }

    /// <summary>
    /// Falls back to user storage when the installation parent cannot contain a workspace.
    /// </summary>
    [Fact]
    public void GetStoragePaths_WhenInstallationParentIsUnavailable_UsesCentralPaths()
    {
        var settings = new UserSettings { UseInstallationAdjacentStorage = true };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var unavailableRoot = Path.Combine(_tempPath, "protected-root");
        File.WriteAllText(unavailableRoot, "not a directory");
        var installation = new GameInstallation(
            Path.Combine(unavailableRoot, "Command and Conquer Generals Zero Hour"),
            GameInstallationType.EaApp);

        var workspacePath = service.GetWorkspacePath(installation);
        var casPath = service.GetCasPoolPath(installation);

        Assert.Equal(Path.Combine(_applicationDataPath, DirectoryNames.Workspaces), workspacePath);
        Assert.Equal(_centralCasPath, casPath);
    }

    /// <summary>
    /// Honors writable user-configured storage when adjacent storage is disabled.
    /// </summary>
    [Fact]
    public void GetStoragePaths_WhenCustomPathsAreConfigured_UsesCustomPaths()
    {
        var customWorkspacePath = Path.Combine(_tempPath, "CustomWorkspace");
        var customCasPath = Path.Combine(_tempPath, "CustomCas");
        var settings = new UserSettings
        {
            UseInstallationAdjacentStorage = false,
            WorkspacePath = customWorkspacePath,
            CasConfiguration = new CasConfiguration { CasRootPath = customCasPath },
        };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var installation = new GameInstallation(Path.Combine(_tempPath, "Game"), GameInstallationType.Retail);

        var workspacePath = service.GetWorkspacePath(installation);
        var casPath = service.GetCasPoolPath(installation);

        Assert.Equal(customWorkspacePath, workspacePath);
        Assert.Equal(customCasPath, casPath);
        Assert.Empty(Directory.GetFiles(_tempPath, ".genhub-write-probe-*"));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempPath, true);
        }
        catch (IOException)
        {
            // Best-effort cleanup for temporary test files.
        }
        catch (UnauthorizedAccessException)
        {
            // Best-effort cleanup for temporary test files.
        }

        GC.SuppressFinalize(this);
    }

    private StorageLocationService CreateService() => new(
        _userSettingsService.Object,
        _configurationProviderService.Object,
        _gameInstallationService.Object,
        new Mock<ILogger<StorageLocationService>>().Object);
}
