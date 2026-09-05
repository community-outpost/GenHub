using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Results;
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
    private readonly Mock<ICasPoolManager> _mockCasPoolManager;
    private readonly Mock<IStorageWritabilityProbe> _mockWritabilityProbe;
    private readonly Mock<ILaunchRegistry> _mockLaunchRegistry;
    private readonly Mock<IGameProcessManager> _mockGameProcessManager;
    private readonly UserSettings _userSettings;

    /// <summary>
    /// Initializes a new instance of the <see cref="StorageMigrationServiceTests"/> class.
    /// </summary>
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

        _userSettings = new UserSettings
        {
            CasConfiguration = new CasConfiguration { CasRootPath = _casDir },
            WorkspacePath = _workspaceDir,
        };
        _mockUserSettingsService = new Mock<IUserSettingsService>();
        _mockUserSettingsService.Setup(x => x.Get()).Returns(_userSettings);
        _mockUserSettingsService
            .Setup(x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()))
            .ReturnsAsync(true);

        _mockCasPoolManager = new Mock<ICasPoolManager>();

        _mockWritabilityProbe = new Mock<IStorageWritabilityProbe>();
        _mockWritabilityProbe.Setup(x => x.CanCreateStorageAt(It.IsAny<string>())).Returns(true);

        _mockLaunchRegistry = new Mock<ILaunchRegistry>();
        _mockLaunchRegistry
            .Setup(x => x.GetAllActiveLaunchesAsync())
            .ReturnsAsync([]);

        _mockGameProcessManager = new Mock<IGameProcessManager>();
        _mockGameProcessManager
            .Setup(x => x.GetActiveProcessesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess([]));
    }

    /// <summary>
    /// Cleans up temporary test files and directories.
    /// </summary>
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

    /// <summary>
    /// Tests that ValidatePreflightAsync returns invalid when target path is null or whitespace.
    /// </summary>
    /// <param name="invalidPath">The invalid path to test.</param>
    /// <returns>A task representing the test execution.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ValidatePreflightAsync_ReturnsInvalid_WhenTargetPathIsNullOrWhitespaceAsync(string? invalidPath)
    {
        var service = CreateService();

        var result = await service.ValidatePreflightAsync(invalidPath!, false);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.NotNull(result.Data.ErrorMessage);
    }

    /// <summary>
    /// Tests that ValidatePreflightAsync fails when target directory is inside application directory.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenTargetPathIsInsideApplicationDirectoryAsync()
    {
        var service = CreateService();
        var currentAppDir = AppContext.BaseDirectory;
        var subDirInsideApp = Path.Combine(currentAppDir, "subfolder");

        var result = await service.ValidatePreflightAsync(subDirInsideApp, false);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.True(result.Data.IsTargetInsideApplicationDirectory);
        Assert.NotNull(result.Data.ErrorMessage);
    }

    /// <summary>
    /// Tests that ValidatePreflightAsync fails when target directory is not writable.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenTargetDirectoryIsNotWritableAsync()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        _mockWritabilityProbe.Setup(x => x.CanCreateStorageAt(targetPath)).Returns(false);

        var result = await service.ValidatePreflightAsync(targetPath, false);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.False(result.Data.HasWritePermission);
        Assert.Contains("writable", result.Data.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that ValidatePreflightAsync fails when active launches exist.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenActiveLaunchesExistAsync()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        _mockLaunchRegistry
            .Setup(x => x.GetAllActiveLaunchesAsync())
            .ReturnsAsync([
                new GameLaunchInfo
                {
                    LaunchId = "launch-1",
                    ProfileId = "profile-1",
                    WorkspaceId = "ws-1",
                    ProcessInfo = new GameProcessInfo { ProcessId = 1234, ExecutablePath = "game.dat" },
                },
            ]);

        var result = await service.ValidatePreflightAsync(targetPath, false);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.True(result.Data.HasActiveProcesses);
        Assert.Contains("active game", result.Data.ErrorMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that ValidatePreflightAsync fails when game processes are active.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task ValidatePreflightAsync_Fails_WhenGameProcessesAreActiveAsync()
    {
        var service = CreateService();
        var targetPath = Path.Combine(_tempRoot, "NewInstallDir");

        _mockGameProcessManager
            .Setup(x => x.GetActiveProcessesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<GameProcessInfo>>.CreateSuccess([
                new GameProcessInfo { ProcessId = 5678, ExecutablePath = "game.dat" },
            ]));

        var result = await service.ValidatePreflightAsync(targetPath, false);

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.False(result.Data.IsValid);
        Assert.True(result.Data.HasActiveProcesses);
    }

    /// <summary>
    /// Tests that ValidatePreflightAsync succeeds when target is valid.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task ValidatePreflightAsync_Succeeds_WhenTargetIsValidAsync()
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

    /// <summary>
    /// Tests that MigrateAsync throws ArgumentNullException when request is null.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task MigrateAsync_ThrowsArgumentNullException_WhenRequestIsNullAsync()
    {
        var service = CreateService();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.MigrateAsync(null!));
    }

    /// <summary>
    /// Tests that MigrateAsync fails when preflight validation fails.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task MigrateAsync_Fails_WhenPreflightValidationFailsAsync()
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

    /// <summary>
    /// Tests that MigrateAsync relocates CAS and workspaces when requested.
    /// </summary>
    /// <returns>A task representing the test execution.</returns>
    [Fact]
    public async Task MigrateAsync_RelocatesCasAndWorkspaces_WhenRequestedAsync()
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

        var expectedNewCas = Path.Combine(targetPath, DirectoryNames.Data, DirectoryNames.CasPool);
        var expectedNewWs = Path.Combine(targetPath, DirectoryNames.Data, DirectoryNames.Workspaces);

        Assert.True(Directory.Exists(expectedNewCas));
        Assert.True(File.Exists(Path.Combine(expectedNewCas, "sample_cas.bin")));

        Assert.True(Directory.Exists(expectedNewWs));
        Assert.True(File.Exists(Path.Combine(expectedNewWs, "sample_ws.bin")));

        _mockUserSettingsService.Verify(
            x => x.TryUpdateAndSaveAsync(It.IsAny<Func<UserSettings, bool>>()),
            Times.Once);
        _mockCasPoolManager.Verify(x => x.ReinitializeInstallationPool(), Times.Once);
    }

    private StorageMigrationService CreateService()
    {
        return new StorageMigrationService(
            _mockConfigProvider.Object,
            _mockUserSettingsService.Object,
            _mockCasPoolManager.Object,
            _mockLaunchRegistry.Object,
            _mockGameProcessManager.Object,
            _mockWritabilityProbe.Object,
            NullLogger<StorageMigrationService>.Instance);
    }
}
