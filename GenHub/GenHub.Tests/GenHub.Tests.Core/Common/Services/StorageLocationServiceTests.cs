using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Tests writable workspace path resolution.
/// </summary>
public sealed class StorageLocationServiceTests : IDisposable
{
    private const string ProbeSearchPattern = StorageConstants.WriteProbeFilePrefix + "*";

    private readonly Mock<IUserSettingsService> _userSettingsService = new();
    private readonly Mock<IConfigurationProviderService> _configurationProviderService = new();
    private readonly Mock<IGameInstallationService> _gameInstallationService = new();
    private readonly string _applicationDataPath;
    private readonly string _tempPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageLocationServiceTests"/> class.
    /// </summary>
    public StorageLocationServiceTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        _applicationDataPath = Path.Combine(_tempPath, "AppData");
        Directory.CreateDirectory(_applicationDataPath);

        _configurationProviderService.Setup(service => service.GetApplicationDataPath()).Returns(_applicationDataPath);
    }

    /// <summary>
    /// Uses installation-adjacent storage when its parent is writable.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WhenInstallationParentIsWritable_UsesAdjacentPath()
    {
        var settings = new UserSettings { UseInstallationAdjacentStorage = true };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var installationRoot = Path.Combine(_tempPath, "EA Games");
        var installationPath = Path.Combine(installationRoot, "Command and Conquer Generals Zero Hour");
        Directory.CreateDirectory(installationPath);
        var installation = new GameInstallation(installationPath, GameInstallationType.EaApp);

        var workspacePath = service.GetWorkspacePath(installation);

        Assert.Equal(Path.Combine(installationRoot, DirectoryNames.GenHubWorkspace), workspacePath);
        Assert.True(Directory.Exists(workspacePath));
        Assert.Empty(Directory.GetFiles(workspacePath, ProbeSearchPattern));
    }

    /// <summary>
    /// Falls back to user storage when the installation parent cannot contain a workspace.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WhenInstallationParentIsUnavailable_UsesCentralPath()
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

        Assert.Equal(Path.Combine(_applicationDataPath, DirectoryNames.Workspaces), workspacePath);
    }

    /// <summary>
    /// Honors a writable user-configured workspace path when adjacent storage is disabled.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WhenCustomPathIsConfigured_UsesCustomPath()
    {
        var customWorkspacePath = Path.Combine(_tempPath, "CustomWorkspace");
        var settings = new UserSettings
        {
            UseInstallationAdjacentStorage = false,
            WorkspacePath = customWorkspacePath,
        };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var installation = new GameInstallation(Path.Combine(_tempPath, "Game"), GameInstallationType.Retail);

        var workspacePath = service.GetWorkspacePath(installation);

        Assert.Equal(customWorkspacePath, workspacePath);
        Assert.True(Directory.Exists(customWorkspacePath));
        Assert.Empty(Directory.GetFiles(customWorkspacePath, ProbeSearchPattern));
    }

    /// <summary>
    /// Honors a creatable custom workspace path when its immediate parent does not exist yet.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WhenCustomPathParentDoesNotExist_UsesCustomPath()
    {
        var missingParent = Path.Combine(_tempPath, "Missing", "Parents");
        var customWorkspacePath = Path.Combine(missingParent, "CustomWorkspace");
        var settings = new UserSettings
        {
            UseInstallationAdjacentStorage = false,
            WorkspacePath = customWorkspacePath,
        };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var installation = new GameInstallation(Path.Combine(_tempPath, "Game"), GameInstallationType.Retail);

        var workspacePath = service.GetWorkspacePath(installation);

        Assert.Equal(customWorkspacePath, workspacePath);
        Assert.True(Directory.Exists(customWorkspacePath));
        Assert.Empty(Directory.GetFiles(customWorkspacePath, ProbeSearchPattern));
    }

    /// <summary>
    /// Falls back to user storage when the configured workspace path cannot be created.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WhenCustomPathIsUnavailable_UsesCentralPath()
    {
        var unavailableRoot = Path.Combine(_tempPath, "custom-root");
        File.WriteAllText(unavailableRoot, "not a directory");
        var settings = new UserSettings
        {
            UseInstallationAdjacentStorage = false,
            WorkspacePath = Path.Combine(unavailableRoot, "CustomWorkspace"),
        };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var installation = new GameInstallation(Path.Combine(_tempPath, "Game"), GameInstallationType.Retail);

        var workspacePath = service.GetWorkspacePath(installation);

        Assert.Equal(Path.Combine(_applicationDataPath, DirectoryNames.Workspaces), workspacePath);
    }

    /// <summary>
    /// Probes a storage location once and reuses the result for later resolutions.
    /// </summary>
    [Fact]
    public void GetWorkspacePath_WhenCalledRepeatedly_ProbesOnce()
    {
        var settings = new UserSettings { UseInstallationAdjacentStorage = true };
        _userSettingsService.Setup(service => service.Get()).Returns(settings);
        var service = CreateService();
        var installationRoot = Path.Combine(_tempPath, "EA Games");
        var installationPath = Path.Combine(installationRoot, "Command and Conquer Generals Zero Hour");
        Directory.CreateDirectory(installationPath);
        var installation = new GameInstallation(installationPath, GameInstallationType.EaApp);

        var first = service.GetWorkspacePath(installation);
        Directory.Delete(installationRoot, true);
        var second = service.GetWorkspacePath(installation);

        Assert.Equal(first, second);
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
