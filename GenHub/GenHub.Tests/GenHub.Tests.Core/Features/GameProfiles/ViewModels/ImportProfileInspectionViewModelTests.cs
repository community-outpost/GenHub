using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameProfiles;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Unit tests for <see cref="ImportProfileInspectionViewModel"/>.
/// </summary>
public class ImportProfileInspectionViewModelTests
{
    private readonly Mock<IProfileSharingService> _sharingServiceMock = new();
    private readonly Mock<INotificationService> _notificationServiceMock = new();

    /// <summary>
    /// Verifies initialization with inspection result properties.
    /// </summary>
    [Fact]
    public void Constructor_Should_PopulatePropertiesFromInspectionResult()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata
            {
                Name = "Generals Online Pro",
                GameType = GameType.ZeroHour,
                GameVersion = "1.04",
                ThemeColor = "#2196F3",
            },
            RequiredManifests = [],
        };

        var installation = new GameInstallation("/c/games/zh", GameInstallationType.Steam)
        {
            Id = "inst-1",
            HasZeroHour = true,
        };

        var inspection = new SharedProfileInspectionResult
        {
            ProfileMetadata = package.Profile,
            Manifests = [],
            HasValidGameInstallation = true,
            MatchedGameInstallationId = "inst-1",
            CompatibleInstallations = [installation],
            TotalDownloadBytesRequired = 25 * 1024 * 1024,
            CachedManifestCount = 1,
            MissingManifestCount = 1,
            HasNameConflict = true,
            SuggestedProfileName = "Generals Online Pro (1)",
            SecurityWarnings = ["Warning: dangerous args stripped"],
            Package = package,
        };

        // Act
        var vm = new ImportProfileInspectionViewModel(
            inspection,
            _sharingServiceMock.Object,
            _notificationServiceMock.Object,
            NullLogger<ImportProfileInspectionViewModel>.Instance);

        // Assert
        Assert.Equal("Generals Online Pro (1)", vm.ProfileName);
        Assert.True(vm.HasNameConflict);
        Assert.True(vm.HasValidGameInstallation);
        Assert.Single(vm.CompatibleInstallations);
        Assert.Equal("inst-1", vm.SelectedInstallation?.Id);
        Assert.True(vm.HasSecurityWarnings);
        Assert.True(vm.HasMissingDownloads);
        Assert.Equal(25 * 1024 * 1024, vm.TotalDownloadBytesRequired);
    }

    /// <summary>
    /// Verifies that ConfirmImportCommand invokes the profile sharing service with proper request.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConfirmImportCommand_Should_InvokeImportAndCloseOnSuccessAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata
            {
                Name = "Imported ZH",
                GameType = GameType.ZeroHour,
            },
            RequiredManifests = [],
        };

        var installation = new GameInstallation("/c/games/zh", GameInstallationType.Steam)
        {
            Id = "inst-steam",
            HasZeroHour = true,
        };

        var inspection = new SharedProfileInspectionResult
        {
            ProfileMetadata = package.Profile,
            Manifests = [],
            HasValidGameInstallation = true,
            MatchedGameInstallationId = "inst-steam",
            CompatibleInstallations = [installation],
            TotalDownloadBytesRequired = 0,
            CachedManifestCount = 0,
            MissingManifestCount = 0,
            HasNameConflict = false,
            SuggestedProfileName = "Imported ZH",
            SecurityWarnings = [],
            Package = package,
        };

        var createdProfile = new GameProfile { Id = "new-id", Name = "Imported ZH" };
        _sharingServiceMock.Setup(s => s.ImportSharedProfileAsync(
                It.IsAny<SharedProfileImportRequest>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProfile>.CreateSuccess(createdProfile));

        var vm = new ImportProfileInspectionViewModel(
            inspection,
            _sharingServiceMock.Object,
            _notificationServiceMock.Object,
            NullLogger<ImportProfileInspectionViewModel>.Instance);

        bool closed = false;
        vm.CloseRequested += (s, e) => closed = true;

        // Act
        await vm.ConfirmImportCommand.ExecuteAsync(null);

        // Assert
        Assert.True(closed);
        Assert.False(vm.HasError);
        _sharingServiceMock.Verify(
            s => s.ImportSharedProfileAsync(
                It.Is<SharedProfileImportRequest>(r => r.ProfileName == "Imported ZH" && r.GameInstallationId == "inst-steam"),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies error handling when import fails.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ConfirmImportCommand_Should_SetErrorMessage_WhenImportFailsAsync()
    {
        // Arrange
        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata { Name = "Fail ZH", GameType = GameType.ZeroHour },
            RequiredManifests = [],
        };

        var installation = new GameInstallation("/c/games/zh", GameInstallationType.Steam)
        {
            Id = "inst-steam",
            HasZeroHour = true,
        };

        var inspection = new SharedProfileInspectionResult
        {
            ProfileMetadata = package.Profile,
            Manifests = [],
            HasValidGameInstallation = true,
            MatchedGameInstallationId = "inst-steam",
            CompatibleInstallations = [installation],
            TotalDownloadBytesRequired = 0,
            CachedManifestCount = 0,
            MissingManifestCount = 0,
            HasNameConflict = false,
            SuggestedProfileName = "Fail ZH",
            SecurityWarnings = [],
            Package = package,
        };

        _sharingServiceMock.Setup(s => s.ImportSharedProfileAsync(
                It.IsAny<SharedProfileImportRequest>(),
                It.IsAny<IProgress<ContentAcquisitionProgress>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<GameProfile>.CreateFailure("Network timeout while downloading manifest"));

        var vm = new ImportProfileInspectionViewModel(
            inspection,
            _sharingServiceMock.Object,
            _notificationServiceMock.Object,
            NullLogger<ImportProfileInspectionViewModel>.Instance);

        // Act
        await vm.ConfirmImportCommand.ExecuteAsync(null);

        // Assert
        Assert.True(vm.HasError);
        Assert.Contains("Network timeout", vm.ErrorMessage);
    }

    /// <summary>
    /// Verifies that manifests collection is mapped to SharedManifestItemViewModel with full details and toggle capability.
    /// </summary>
    [Fact]
    public void Manifests_Should_BeMappedToItemViewModels_WithDetails()
    {
        // Arrange
        var dependency = new SharedManifestDependency
        {
            ManifestId = "outpost.mod.shockwave.1.20",
            DisplayName = "ShockWave Mod",
            Version = "1.20",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            Publisher = "Community Outpost",
            DownloadSize = 500 * 1024 * 1024,
            IsCachedLocally = false,
            Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            Files =
            [
                new ManifestFile
                {
                    RelativePath = "ShockWave.big",
                    Size = 500 * 1024 * 1024,
                    DownloadUrl = "https://github.com/community-outpost/shockwave/releases/download/v1.20/ShockWave.big",
                    Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                },
            ],
        };

        var package = new SharedGameProfilePackage
        {
            SchemaVersion = 1,
            Profile = new SharedProfileMetadata { Name = "ShockWave Profile", GameType = GameType.ZeroHour },
            RequiredManifests = [dependency],
        };

        var inspection = new SharedProfileInspectionResult
        {
            ProfileMetadata = package.Profile,
            Manifests = [dependency],
            HasValidGameInstallation = true,
            MatchedGameInstallationId = null,
            CompatibleInstallations = [],
            TotalDownloadBytesRequired = 500 * 1024 * 1024,
            CachedManifestCount = 0,
            MissingManifestCount = 1,
            HasNameConflict = false,
            SuggestedProfileName = "ShockWave Profile",
            SecurityWarnings = [],
            Package = package,
        };

        // Act
        var vm = new ImportProfileInspectionViewModel(
            inspection,
            _sharingServiceMock.Object,
            _notificationServiceMock.Object,
            NullLogger<ImportProfileInspectionViewModel>.Instance);

        // Assert
        Assert.Single(vm.Manifests);
        var item = vm.Manifests[0];
        Assert.Equal("ShockWave Mod", item.DisplayName);
        Assert.Equal("outpost.mod.shockwave.1.20", item.ManifestId);
        Assert.Equal("1.20", item.Version);
        Assert.Equal("Community Outpost", item.Publisher);
        Assert.False(item.IsCachedLocally);
        Assert.Equal("https://github.com/community-outpost/shockwave/releases/download/v1.20/ShockWave.big", item.DownloadUrl);
        Assert.Equal("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", item.Hash);
        Assert.True(item.HasDetails);
        Assert.False(item.IsExpanded);

        // Toggle Expand
        item.ToggleExpandCommand.Execute(null);
        Assert.True(item.IsExpanded);
    }
}
