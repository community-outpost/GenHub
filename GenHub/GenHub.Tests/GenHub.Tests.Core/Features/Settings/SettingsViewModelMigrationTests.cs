using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.UserData;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.AppUpdate.Interfaces;
using GenHub.Features.Settings.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Settings;

/// <summary>
/// Unit tests for migration commands and properties in <see cref="SettingsViewModel"/>.
/// </summary>
public class SettingsViewModelMigrationTests
{
    private readonly Mock<IUserSettingsService> _mockConfigService;
    private readonly Mock<ICasService> _mockCasService;
    private readonly Mock<IGameProfileManager> _mockProfileManager;
    private readonly Mock<IWorkspaceManager> _mockWorkspaceManager;
    private readonly Mock<IContentManifestPool> _mockManifestPool;
    private readonly Mock<IVelopackUpdateManager> _mockUpdateManager;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IConfigurationProviderService> _mockConfigurationProvider;
    private readonly Mock<IGameInstallationService> _mockInstallationService;
    private readonly Mock<IStorageLocationService> _mockStorageLocationService;
    private readonly Mock<IUserDataTracker> _mockUserDataTracker;
    private readonly Mock<IDialogService> _mockDialogService;
    private readonly Mock<IStorageMigrationService> _mockStorageMigrationService;

    public SettingsViewModelMigrationTests()
    {
        _mockConfigService = new Mock<IUserSettingsService>();
        _mockCasService = new Mock<ICasService>();
        _mockProfileManager = new Mock<IGameProfileManager>();
        _mockWorkspaceManager = new Mock<IWorkspaceManager>();
        _mockManifestPool = new Mock<IContentManifestPool>();
        _mockUpdateManager = new Mock<IVelopackUpdateManager>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockConfigurationProvider = new Mock<IConfigurationProviderService>();
        _mockInstallationService = new Mock<IGameInstallationService>();
        _mockStorageLocationService = new Mock<IStorageLocationService>();
        _mockUserDataTracker = new Mock<IUserDataTracker>();
        _mockDialogService = new Mock<IDialogService>();
        _mockStorageMigrationService = new Mock<IStorageMigrationService>();

        _mockConfigService.Setup(x => x.Get()).Returns(new UserSettings());
    }

    [Fact]
    public async Task MigrateInstallationLocationCommand_ShowsWarning_WhenTargetPathIsEmptyAsync()
    {
        var vm = CreateViewModel();
        vm.MigrationTargetPath = string.Empty;

        await vm.MigrateInstallationLocationCommand.ExecuteAsync(null);

        _mockNotificationService.Verify(
            x => x.ShowWarning("Migration Target Required", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);

        _mockStorageMigrationService.Verify(
            x => x.ValidatePreflightAsync(It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MigrateInstallationLocationCommand_ShowsError_WhenPreflightValidationFailsAsync()
    {
        var vm = CreateViewModel();
        vm.MigrationTargetPath = "/valid/path";

        var preflightResult = OperationResult<StorageMigrationPreflightResult>.CreateSuccess(
            StorageMigrationPreflightResult.Failure("Not enough space."));

        _mockStorageMigrationService
            .Setup(x => x.ValidatePreflightAsync("/valid/path", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preflightResult);

        await vm.MigrateInstallationLocationCommand.ExecuteAsync(null);

        _mockNotificationService.Verify(
            x => x.ShowError("Migration Pre-flight Failed", "Not enough space.", It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);

        Assert.False(vm.IsMigrating);
    }

    [Fact]
    public async Task MigrateInstallationLocationCommand_Aborts_WhenUserDeclinesConfirmationAsync()
    {
        var vm = CreateViewModel();
        vm.MigrationTargetPath = "/target/folder";

        var preflightResult = OperationResult<StorageMigrationPreflightResult>.CreateSuccess(
            StorageMigrationPreflightResult.Success(100, 1000));

        _mockStorageMigrationService
            .Setup(x => x.ValidatePreflightAsync("/target/folder", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preflightResult);

        _mockDialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(false);

        await vm.MigrateInstallationLocationCommand.ExecuteAsync(null);

        _mockStorageMigrationService.Verify(
            x => x.MigrateAsync(It.IsAny<StorageMigrationRequest>(), It.IsAny<IProgress<StorageMigrationProgress>>(), It.IsAny<CancellationToken>()),
            Times.Never);

        Assert.False(vm.IsMigrating);
    }

    [Fact]
    public async Task MigrateInstallationLocationCommand_ExecutesMigration_WhenConfirmedAsync()
    {
        var vm = CreateViewModel();
        vm.MigrationTargetPath = "/target/folder";
        vm.RelocateCasAndWorkspacesWithMigration = true;

        var preflightResult = OperationResult<StorageMigrationPreflightResult>.CreateSuccess(
            StorageMigrationPreflightResult.Success(100, 1000));

        _mockStorageMigrationService
            .Setup(x => x.ValidatePreflightAsync("/target/folder", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(preflightResult);

        _mockDialogService
            .Setup(x => x.ShowConfirmationAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>()))
            .ReturnsAsync(true);

        _mockStorageMigrationService
            .Setup(x => x.MigrateAsync(It.IsAny<StorageMigrationRequest>(), It.IsAny<IProgress<StorageMigrationProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));

        await vm.MigrateInstallationLocationCommand.ExecuteAsync(null);

        _mockStorageMigrationService.Verify(
            x => x.MigrateAsync(
                It.Is<StorageMigrationRequest>(r => r.TargetPath == "/target/folder" && r.RelocateCasAndWorkspace),
                It.IsAny<IProgress<StorageMigrationProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private SettingsViewModel CreateViewModel() => new(
        _mockConfigService.Object,
        NullLogger<SettingsViewModel>.Instance,
        _mockCasService.Object,
        _mockProfileManager.Object,
        _mockWorkspaceManager.Object,
        _mockManifestPool.Object,
        _mockUpdateManager.Object,
        _mockNotificationService.Object,
        _mockConfigurationProvider.Object,
        _mockInstallationService.Object,
        _mockStorageLocationService.Object,
        _mockUserDataTracker.Object,
        _mockDialogService.Object,
        _mockStorageMigrationService.Object);
}
