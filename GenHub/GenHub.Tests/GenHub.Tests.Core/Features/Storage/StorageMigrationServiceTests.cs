using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Common.Services;
using GenHub.Core.Configuration;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProcesses;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.GameProcess;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Storage;

/// <summary>
/// Unit tests for <see cref="StorageMigrationService"/>.
/// </summary>
public class StorageMigrationServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _appDataDir;
    private readonly string _casDir;
    private readonly string _workspaceDir;
    private readonly Mock<IConfigurationProviderService> _mockConfigProvider;
    private readonly Mock<IUserSettingsService> _mockUserSettingsService;
    private readonly Mock<IStorageWritabilityProbe> _mockWritabilityProbe;
    private readonly Mock<ILaunchRegistry> _mockLaunchRegistry;
    private readonly Mock<IGameProcessManager> _mockGameProcessManager;
    private readonly UserSettings _userSettings;

    public StorageMigrationServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "GenHubMigrationTests_" + Guid.NewGuid().ToString("N"));
        _appDataDir = Path.Combine(_tempRoot, "AppData");
        _casDir = Path.Combine(_appDataDir, "cas-pool");
        _workspaceDir = Path.Combine(_appDataDir, "workspaces");

        Directory.CreateDirectory(_appDataDir);
        Directory.CreateDirectory(_casDir);
        Directory.CreateDirectory(_workspaceDir);

        // Seed some sample data
        File.WriteAllText(Path.Combine(_casDir, "sample_cas.bin"), "dummy cas content");
        File.WriteAllText(Path.Combine(_workspaceDir, "sample_ws.bin"), "dummy ws content");

        _mockConfigProvider = new Mock<IConfigurationProviderService>();
        _mockConfigProvider.Setup(x => x.GetRootAppDataPath()).Returns(_appDataDir);
        _mockConfigProvider.Setup(x => x.GetCasConfiguration()).Returns(new CasConfiguration { CasRootPath = _casDir });

        _userSettings = new UserSettings { CasRootPath = _casDir, WorkspacePath = _workspaceDir };
        _mockUserSettingsService = new Mock<IUserSettingsService>();
        _mockUserSettingsService.Setup(x => x.Get()).Returns(_userSettings);

        _mockWritabilityProbe = new Mock<IStorageWritabilityProbe>();
        _mockWritabilityProbe.Setup(x => x.CanCreateStorageAt(It.IsAny<string>())).Returns(true);

        _mockLaunchRegistry = new Mock<ILaunchRegistry>();
        _mockLaunchRegistry
            .Setup(x => x.GetAllActiveLaunchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _mockGameProcessManager = new Mock<IGameProcessManager>();
        _mockGameProcessManager
            .Setup(x => x.GetActiveProcessesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GameProcessInfo>());
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }

        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidatePreflightAsync_ThrowsArgumentException_WhenTargetPathIsNullOrWhitespace(string? invalidPath)
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.ValidatePreflightAsync(invalidPath!));
    }

    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenTargetPathIsInsideApplicationDirectory()
    {
        var service = CreateService();
        var currentAppDir = AppContext.BaseDirectory;
        var subDirInsideApp = Path.Combine(currentAppDir, "subfolder");

        var result = await service.ValidatePreflightAsync(subDirInsideApp);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.True(result.Data.IsTargetInsideApplicationDirectory);
        Assert.NotNull(result.Data.ErrorMessage);
    }

    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenTargetDirectoryIsNotWritable()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        _mockWritabilityProbe.Setup(x => x.CanCreateStorageAt(targetPath)).Returns(false);

        var result = await service.ValidatePreflightAsync(targetPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.False(result.Data.HasWritePermission);
        Assert.Contains("permission", result.Data.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenActiveLaunchesExist()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        _mockLaunchRegistry
            .Setup(x => x.GetAllActiveLaunchesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ActiveLaunchEntry { ProfileId = "profile-1", ProcessId = 1234 }]);

        var result = await service.ValidatePreflightAsync(targetPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.True(result.Data.HasActiveProcesses);
        Assert.Contains("active game", result.Data.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenGameProcessesAreActive()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        _mockGameProcessManager
            .Setup(x => x.GetActiveProcessesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new GameProcessInfo { ProcessId = 5678, ExecutablePath = "game.dat" }]);

        var result = await service.ValidatePreflightAsync(targetPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.True(result.Data.HasActiveProcesses);
    }

    [Fact]
    public async Task ValidatePreflightAsync_Succeeds_WhenTargetIsValid()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        var result = await service.ValidatePreflightAsync(targetPath, relocateCasAndWorkspace: true);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.True(result.Data.IsValid);
        Assert.True(result.Data.HasWritePermission);
        Assert.False(result.Data.HasActiveProcesses);
        Assert.False(result.Data.IsTargetInsideApplicationDirectory);
    }

    [Fact]
    public async Task MigrateAsync_ThrowsArgumentNullException_WhenRequestIsNull()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.MigrateAsync(null!));
    }

    [Fact]
    public async Task MigrateAsync_Fails_WhenPreflightValidationFails()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        _mockWritabilityProbe.Setup(x => x.CanCreateStorageAt(targetPath)).Returns(false);

        var request = new StorageMigrationRequest
        {
            TargetPath = targetPath,
            RelocateCasAndWorkspace = false,
            ExitApplicationOnSuccess = false,
            LaunchHelperProcess = false,
        };

        var result = await service.MigrateAsync(request);

        Assert.False(result.Success);
        Assert.NotNull(result.FirstError);
    }

    [Fact]
    public async Task MigrateAsync_RelocatesCasAndWorkspaces_WhenRequested()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");
        Directory.CreateDirectory(targetPath);

        var request = new StorageMigrationRequest
        {
            TargetPath = targetPath,
            RelocateCasAndWorkspace = true,
            ExitApplicationOnSuccess = false,
            LaunchHelperProcess = false,
        };

        var result = await service.MigrateAsync(request);

        Assert.True(result.Success);

        var expectedNewCas = Path.Combine(targetPath, DirectoryNames.CasPool);
        var expectedNewWs = Path.Combine(targetPath, DirectoryNames.Workspaces);

        Assert.True(Directory.Exists(expectedNewCas));
        Assert.True(File.Exists(Path.Combine(expectedNewCas, "sample_cas.bin")));

        Assert.True(Directory.Exists(expectedNewWs));
        Assert.True(File.Exists(Path.Combine(expectedNewWs, "sample_ws.bin")));

        _mockUserSettingsService.Verify(x => x.Update(It.IsAny<Action<UserSettings>>()), Times.Once);
        _mockUserSettingsService.Verify(x => x.SaveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private StorageMigrationService CreateService()
    {
        return new StorageMigrationService(
            _mockConfigProvider.Object,
            _mockUserSettingsService.Object,
            _mockWritabilityProbe.Object,
            _mockLaunchRegistry.Object,
            _mockGameProcessManager.Object,
            NullLogger<StorageMigrationService>.Instance);
    }
}
