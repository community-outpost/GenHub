using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.CommunityOutpost;
using GenHub.Features.Content.Services.GeneralsOnline;
using GenHub.Features.Content.Services.Publishers;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels.Wizard;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.GameProfiles.Services;

/// <summary>
/// Unit tests for <see cref="SetupWizardService"/>.
/// </summary>
public class SetupWizardServiceTests
{
    private readonly Mock<IGameClientProfileService> _profileServiceMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<GeneralsOnlineDiscoverer> _goDiscovererMock;
    private readonly Mock<CommunityOutpostDiscoverer> _cpDiscovererMock;

    /// <summary>
    /// Initializes a new instance of the <see cref="SetupWizardServiceTests"/> class.
    /// </summary>
    public SetupWizardServiceTests()
    {
        _goDiscovererMock = new Mock<GeneralsOnlineDiscoverer>(
            Mock.Of<Microsoft.Extensions.Logging.ILogger<GeneralsOnlineDiscoverer>>(),
            Mock.Of<GenHub.Core.Interfaces.Providers.IProviderDefinitionLoader>(),
            Mock.Of<GenHub.Core.Interfaces.Providers.ICatalogParserFactory>(),
            Mock.Of<System.Net.Http.IHttpClientFactory>());

        _cpDiscovererMock = new Mock<CommunityOutpostDiscoverer>(
            Mock.Of<System.Net.Http.IHttpClientFactory>(),
            Mock.Of<GenHub.Core.Interfaces.Providers.IProviderDefinitionLoader>(),
            Mock.Of<GenHub.Core.Interfaces.Providers.ICatalogParserFactory>(),
            Mock.Of<Microsoft.Extensions.Logging.ILogger<CommunityOutpostDiscoverer>>());
    }

    /// <summary>
    /// Verifies that when a managed client manifest is up-to-date in the manifest pool
    /// and a corresponding profile exists, the setup wizard skips and returns Decline.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenManagedClientUpToDateAndProfileExists_SkipsWizardWithDeclineAsync()
    {
        // Arrange
        const string latestVersion = "082826_QFE1";
        const string manifestId = "1.82826.generalsonline.gameclient.60hz";

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [new ContentSearchResult { Version = latestVersion }],
            }));

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

        var goManifest = new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            Name = "Generals Online",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
        };

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([goManifest]));

        _profileServiceMock
            .Setup(s => s.ProfileExistsForGameClientAsync(manifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService();

        // Act
        var result = await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert: Generals Online was recognized as up-to-date and profile already exists, so no action required
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.GeneralsOnlineAction);
    }

    /// <summary>
    /// Verifies that when every component is up-to-date and profiles exist, no wizard
    /// dialog is shown and the result is confirmed so callers proceed instead of
    /// treating a healthy no-op run as declined.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenAllComponentsUpToDateAndProfilesExist_ConfirmsWithoutDialogAsync()
    {
        // Arrange
        const string cpRetailVersion = "23-07-2026";
        const string cpNonRetVersion = "11-09-2026";
        const string goVersion = "082826_QFE1";
        const string shVersion = "2.0";

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items =
                [
                    new ContentSearchResult
                    {
                        Id = "generalszh_11-09-2026_NonRet.zip",
                        Name = "Community Patch 11-09-2026 (Non-Retail)",
                        Version = cpNonRetVersion,
                        Tags = { "nonretail" },
                    },
                    new ContentSearchResult
                    {
                        Id = "generalszh_23-07-2026.zip",
                        Name = "Community Patch 23-07-2026",
                        Version = cpRetailVersion,
                    },
                ],
            }));

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [new ContentSearchResult { Version = goVersion }],
            }));

        var shProviderMock = CreateSuperHackersProviderMock(shVersion);

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess(
            [
                CreateGameClientManifest("1.cp.retail.communitypatch.gameclient", "Community Patch 23-07-2026", cpRetailVersion, CommunityOutpostConstants.PublisherType),
                CreateGameClientManifest("1.cp.nonret.communitypatch.gameclient", "Community Patch 11-09-2026 (Non-Retail)", cpNonRetVersion, CommunityOutpostConstants.PublisherType),
                CreateGameClientManifest("1.82826.generalsonline.gameclient.60hz", "Generals Online", goVersion, PublisherTypeConstants.GeneralsOnline),
                CreateGameClientManifest("1.2.superhackers.gameclient.zerohour", "TheSuperHackers", shVersion, PublisherTypeConstants.TheSuperHackers),
            ]));

        _profileServiceMock
            .Setup(s => s.ProfileExistsForGameClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(shProviderMock.Object);
        bool dialogShown = false;
        service.DialogShower = _ =>
        {
            dialogShown = true;
            return Task.FromResult(true);
        };

        // Act
        var result = await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert
        Assert.False(dialogShown);
        Assert.True(result.Confirmed);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.CommunityPatchAction);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.CommunityPatchNonRetAction);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.GeneralsOnlineAction);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.SuperHackersAction);
    }

    /// <summary>
    /// Verifies that when a managed client manifest is up-to-date in the pool but its profile is missing,
    /// the setup wizard displays it as Downloaded with action Create Profile, and confirms profile creation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenManagedClientUpToDateAndProfileMissing_ShowsInWizardAsCreateProfileAsync()
    {
        // Arrange
        const string latestVersion = "082826_QFE1";
        const string manifestId = "1.82826.generalsonline.gameclient.60hz";

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [new ContentSearchResult { Version = latestVersion }],
            }));

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

        var goManifest = new ContentManifest
        {
            Id = ManifestId.Create(manifestId),
            Name = "Generals Online",
            Version = latestVersion,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = PublisherTypeConstants.GeneralsOnline },
        };

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([goManifest]));

        _profileServiceMock
            .Setup(s => s.ProfileExistsForGameClientAsync(manifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService();

        SetupWizardViewModel? capturedVm = null;
        service.DialogShower = vm =>
        {
            capturedVm = vm;
            vm.ConfirmCommand.Execute(null);
            return Task.FromResult(true);
        };

        // Act
        var result = await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert: Generals Online was shown in wizard with Create Profile action
        Assert.NotNull(capturedVm);
        var goItem = capturedVm.Items.FirstOrDefault(i => Equals(i.Metadata, PublisherTypeConstants.GeneralsOnline));
        Assert.NotNull(goItem);
        Assert.Equal(GameClientConstants.WizardStatuses.Downloaded, goItem.Status);
        Assert.Equal(GameClientConstants.WizardActionLabels.CreateProfile, goItem.ActionLabel);
        Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, goItem.ActionType);
        Assert.True(goItem.IsSelected);
        Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, result.GeneralsOnlineAction);
    }

    /// <summary>
    /// Verifies that when an up-to-date client is found in an installation but its profile is missing,
    /// the setup wizard displays it as Detected with action Create Profile, and confirms profile creation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenInstalledClientUpToDateAndProfileMissing_ShowsInWizardAsCreateProfileAsync()
    {
        // Arrange
        const string latestVersion = "082826_QFE1";
        const string clientId = "installed.generalsonline.client";

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [new ContentSearchResult { Version = latestVersion }],
            }));

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

        // Manifest pool has no up-to-date manifests
        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var installation = new GameInstallation("C:\\Games\\Generals", GameInstallationType.Retail, null);
        var client = new GameClient
        {
            Id = clientId,
            InstallationId = installation.Id,
            Name = "Generals Online",
            PublisherType = PublisherTypeConstants.GeneralsOnline,
            Version = latestVersion,
        };
        installation.AvailableGameClients = [client];

        _profileServiceMock
            .Setup(s => s.ProfileExistsForGameClientAsync(clientId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var service = CreateService();

        SetupWizardViewModel? capturedVm = null;
        service.DialogShower = vm =>
        {
            capturedVm = vm;
            vm.ConfirmCommand.Execute(null);
            return Task.FromResult(true);
        };

        // Act
        var result = await service.RunSetupWizardAsync([installation], CancellationToken.None);

        // Assert: Generals Online was shown in wizard with Status "Detected" and "Create Profile" action
        Assert.NotNull(capturedVm);
        var goItem = capturedVm.Items.FirstOrDefault(i => Equals(i.Metadata, PublisherTypeConstants.GeneralsOnline));
        Assert.NotNull(goItem);
        Assert.Equal(GameClientConstants.WizardStatuses.Detected, goItem.Status);
        Assert.Equal(GameClientConstants.WizardActionLabels.CreateProfile, goItem.ActionLabel);
        Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, goItem.ActionType);
        Assert.True(goItem.IsSelected);
        Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, result.GeneralsOnlineAction);
    }

    /// <summary>
    /// Verifies that when Community Patch offers both Retail and Non-Retail builds,
    /// the setup wizard displays both options, defaults Retail to selected and Non-Retail to unselected,
    /// and includes the incompatibility warning on Non-Retail.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenCommunityPatchesDiscovered_PresentsRetailAndNonRetailOptionsWithWarningAsync()
    {
        // Arrange
        const string retailVersion = "23-07-2026";
        const string nonRetVersion = "11-09-2026";

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items =
                [
                    new ContentSearchResult
                    {
                        Id = "generalszh_11-09-2026_NonRet.zip",
                        Name = "Community Patch 11-09-2026 (Non-Retail)",
                        Version = nonRetVersion,
                        Tags = { "nonretail" },
                    },
                    new ContentSearchResult
                    {
                        Id = "generalszh_23-07-2026.zip",
                        Name = "Community Patch 23-07-2026",
                        Version = retailVersion,
                    },
                ],
            }));

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var service = CreateService();

        SetupWizardViewModel? capturedVm = null;
        service.DialogShower = vm =>
        {
            capturedVm = vm;
            vm.ConfirmCommand.Execute(null);
            return Task.FromResult(true);
        };

        // Act
        var result = await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert
        Assert.NotNull(capturedVm);
        var retailItem = capturedVm.Items.FirstOrDefault(i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchRetailCode));
        var nonRetItem = capturedVm.Items.FirstOrDefault(i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchNonRetCode));

        Assert.NotNull(retailItem);
        Assert.NotNull(nonRetItem);

        // Retail item defaults to selected
        Assert.True(retailItem.IsSelected);
        Assert.Equal(retailVersion, retailItem.Version);
        Assert.Equal(GameClientConstants.WizardActionTypes.Install, result.CommunityPatchAction);

        // Non-retail item defaults to unchecked and contains compatibility warning
        Assert.False(nonRetItem.IsSelected);
        Assert.Equal(nonRetVersion, nonRetItem.Version);
        Assert.EndsWith(" " + GameClientConstants.WizardFallbackText[GameClientConstants.WizardLocalizationKeys.NonRetailIncompatibleNotice], nonRetItem.Description);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.CommunityPatchNonRetAction);
        Assert.True(result.Confirmed);
    }

    /// <summary>
    /// Verifies that when a user manually checks both Retail and Non-Retail in the wizard,
    /// both actions are returned as confirmed Install actions.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenUserSelectsBothRetailAndNonRetail_ReturnsBothActionsAsync()
    {
        // Arrange
        const string retailVersion = "23-07-2026";
        const string nonRetVersion = "11-09-2026";

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items =
                [
                    new ContentSearchResult
                    {
                        Id = "generalszh_11-09-2026_NonRet.zip",
                        Name = "Community Patch 11-09-2026 (Non-Retail)",
                        Version = nonRetVersion,
                        Tags = { "nonretail" },
                    },
                    new ContentSearchResult
                    {
                        Id = "generalszh_23-07-2026.zip",
                        Name = "Community Patch 23-07-2026",
                        Version = retailVersion,
                    },
                ],
            }));

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var service = CreateService();

        service.DialogShower = vm =>
        {
            var nonRetItem = vm.Items.FirstOrDefault(i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchNonRetCode));
            if (nonRetItem != null)
            {
                nonRetItem.IsSelected = true; // User opts into Non-Retail as well
            }

            vm.ConfirmCommand.Execute(null);
            return Task.FromResult(true);
        };

        // Act
        var result = await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert: Both actions are confirmed for installation
        Assert.Equal(GameClientConstants.WizardActionTypes.Install, result.CommunityPatchAction);
        Assert.Equal(GameClientConstants.WizardActionTypes.Install, result.CommunityPatchNonRetAction);
    }

    /// <summary>
    /// Verifies that a native TheSuperHackers client on macOS or Linux is offered as Create Profile,
    /// since the publisher package the Install action downloads is a Windows build.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenNativeSuperHackersClientDetected_OffersCreateProfileOnUnixHostsAsync()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"GenHub.Wizard.{Guid.NewGuid():N}")).FullName;
        try
        {
            var executablePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable));
            await File.WriteAllBytesAsync(executablePath, HostNativeExecutableHeader());

            _goDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));
            _cpDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));
            _manifestPoolMock
                .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            installation.AvailableGameClients =
            [
                new GameClient
                {
                    Id = "1.104.retail.gameclient.zerohour",
                    InstallationId = installation.Id,
                    Name = $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}",
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    GameType = GameType.ZeroHour,
                    Version = GameClientConstants.UnknownVersion,
                    ExecutablePath = executablePath,
                },
            ];

            var service = CreateService(CreateSuperHackersProviderMock("weekly-2026-09-25").Object);
            SetupWizardViewModel? capturedVm = null;
            service.DialogShower = vm =>
            {
                capturedVm = vm;
                vm.ConfirmCommand.Execute(null);
                return Task.FromResult(true);
            };

            var result = await service.RunSetupWizardAsync([installation], CancellationToken.None);

            var expectedAction = OperatingSystem.IsWindows()
                ? GameClientConstants.WizardActionTypes.Install
                : GameClientConstants.WizardActionTypes.CreateProfile;
            var shItem = Assert.Single(capturedVm!.Items, i => Equals(i.Metadata, PublisherTypeConstants.TheSuperHackers));
            Assert.Equal(expectedAction, shItem.ActionType);
            Assert.True(shItem.IsSelected);
            Assert.Equal(expectedAction, result.SuperHackersAction);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that only a profile for the native build itself makes it up to date on macOS or Linux.
    /// A profile for a Windows build of the same publisher, such as one run under Wine, still leaves
    /// the native build to be profiled; neither case offers an Update. A Windows build listed before
    /// the native one in the same installation does not hide it, and neither does a profiled Windows
    /// package that is already the latest release.
    /// </summary>
    /// <param name="nativeBuildHasProfile">Whether the native client has its own profile.</param>
    /// <param name="windowsBuildListedFirst">Whether a Windows client of the publisher precedes the native one.</param>
    /// <param name="windowsPackageIsLatest">Whether the pooled Windows package is the latest release.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public async Task RunSetupWizardAsync_WhenNativeSuperHackersBuildDetected_DecidesOnItsOwnProfileAsync(bool nativeBuildHasProfile, bool windowsBuildListedFirst, bool windowsPackageIsLatest)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"GenHub.Wizard.{Guid.NewGuid():N}")).FullName;
        try
        {
            const string nativeClientId = "1.104.retail.gameclient.zerohour";
            const string windowsManifestId = "1.20260901.thesuperhackers.gameclient.generalszh";
            var executablePath = Path.Combine(directory, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable));
            await File.WriteAllBytesAsync(executablePath, HostNativeExecutableHeader());

            _goDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));
            _cpDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

            // An older Windows package of the same publisher, profiled (for example under Wine).
            var windowsPackage = CreateGameClientManifest(windowsManifestId, "TheSuperHackers - Zero Hour", windowsPackageIsLatest ? "weekly-2026-09-25" : "weekly-2026-09-01", PublisherTypeConstants.TheSuperHackers);
            _manifestPoolMock
                .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([windowsPackage]));
            _profileServiceMock
                .Setup(s => s.ProfileExistsForGameClientAsync(windowsManifestId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _profileServiceMock
                .Setup(s => s.ProfileExistsForGameClientAsync(nativeClientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(nativeBuildHasProfile);

            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            installation.AvailableGameClients =
            [
                new GameClient
                {
                    Id = nativeClientId,
                    InstallationId = installation.Id,
                    Name = $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}",
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    GameType = GameType.ZeroHour,
                    Version = GameClientConstants.UnknownVersion,
                    ExecutablePath = executablePath,
                },
            ];
            if (windowsBuildListedFirst)
            {
                var windowsExecutablePath = Path.Combine(directory, GameClientConstants.SuperHackersZeroHourExecutable);
                await File.WriteAllBytesAsync(windowsExecutablePath, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);
                installation.AvailableGameClients.Insert(0, new GameClient
                {
                    Id = windowsManifestId,
                    InstallationId = installation.Id,
                    Name = $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}",
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    GameType = GameType.ZeroHour,
                    Version = "weekly-2026-09-01",
                    ExecutablePath = windowsExecutablePath,
                });
            }

            var service = CreateService(CreateSuperHackersProviderMock("weekly-2026-09-25").Object);
            SetupWizardViewModel? capturedVm = null;
            service.DialogShower = vm =>
            {
                capturedVm = vm;
                vm.ConfirmCommand.Execute(null);
                return Task.FromResult(true);
            };

            var result = await service.RunSetupWizardAsync([installation], CancellationToken.None);

            var shItem = capturedVm?.Items.FirstOrDefault(i => Equals(i.Metadata, PublisherTypeConstants.TheSuperHackers));
            if (OperatingSystem.IsWindows())
            {
                if (!windowsBuildListedFirst && !windowsPackageIsLatest)
                {
                    Assert.Equal(GameClientConstants.WizardActionTypes.Update, shItem?.ActionType);
                }

                return;
            }

            if (nativeBuildHasProfile)
            {
                Assert.Null(shItem);
                Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.SuperHackersAction);
            }
            else
            {
                Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, shItem?.ActionType);
                Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, result.SuperHackersAction);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that every native build of a publisher is checked for a profile on macOS or Linux,
    /// so an unprofiled Generals build is still offered when the Zero Hour build beside it has one.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WhenOneOfTwoNativeSuperHackersBuildsLacksProfile_OffersCreateProfileAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"GenHub.Wizard.{Guid.NewGuid():N}")).FullName;
        try
        {
            const string zeroHourId = "1.104.retail.gameclient.zerohour";
            const string generalsId = "1.108.retail.gameclient.generals";
            var zeroHourPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersZeroHourExecutable));
            var generalsPath = Path.Combine(directory, Path.GetFileNameWithoutExtension(GameClientConstants.SuperHackersGeneralsExecutable));
            await File.WriteAllBytesAsync(zeroHourPath, HostNativeExecutableHeader());
            await File.WriteAllBytesAsync(generalsPath, HostNativeExecutableHeader());

            _goDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));
            _cpDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));
            _manifestPoolMock
                .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
            _profileServiceMock
                .Setup(s => s.ProfileExistsForGameClientAsync(zeroHourId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);
            _profileServiceMock
                .Setup(s => s.ProfileExistsForGameClientAsync(generalsId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            installation.AvailableGameClients =
            [
                new GameClient
                {
                    Id = zeroHourId,
                    InstallationId = installation.Id,
                    Name = $"{SuperHackersConstants.PublisherName} - {SuperHackersConstants.ZeroHourDisplayName}",
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    GameType = GameType.ZeroHour,
                    Version = GameClientConstants.UnknownVersion,
                    ExecutablePath = zeroHourPath,
                },
                new GameClient
                {
                    Id = generalsId,
                    InstallationId = installation.Id,
                    Name = $"{SuperHackersConstants.PublisherName} - Generals",
                    PublisherType = PublisherTypeConstants.TheSuperHackers,
                    GameType = GameType.Generals,
                    Version = GameClientConstants.UnknownVersion,
                    ExecutablePath = generalsPath,
                },
            ];

            var service = CreateService(CreateSuperHackersProviderMock("weekly-2026-09-25").Object);
            SetupWizardViewModel? capturedVm = null;
            service.DialogShower = vm =>
            {
                capturedVm = vm;
                vm.ConfirmCommand.Execute(null);
                return Task.FromResult(true);
            };

            var result = await service.RunSetupWizardAsync([installation], CancellationToken.None);

            var shItem = capturedVm?.Items.FirstOrDefault(i => Equals(i.Metadata, PublisherTypeConstants.TheSuperHackers));
            Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, shItem?.ActionType);
            Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, result.SuperHackersAction);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that a native non-retail Community Patch client on macOS or Linux is offered only
    /// under the non-retail component, not also under the retail one, and that the Windows retail
    /// package is left unselected so confirming does not download it too.
    /// </summary>
    /// <param name="withRetailWindowsClient">Whether a Windows retail client is also installed.</param>
    /// <param name="nonRetailHasProfile">Whether the native non-retail client already has a profile.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task RunSetupWizardAsync_WhenNativeNonRetailCommunityPatchDetected_OffersOnlyNonRetailAsync(bool withRetailWindowsClient, bool nonRetailHasProfile)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"GenHub.Wizard.{Guid.NewGuid():N}")).FullName;
        try
        {
            const string nonRetClientId = "1.0.communityoutpost.gameclient.nonret";
            var executablePath = Path.Combine(directory, "generalszh-nonret");
            await File.WriteAllBytesAsync(executablePath, HostNativeExecutableHeader());
            const string retailClientId = "1.0.communityoutpost.gameclient.retail";
            var retailExecutablePath = Path.Combine(directory, "generalszh-retail.exe");
            await File.WriteAllBytesAsync(retailExecutablePath, [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]);

            _goDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));
            _cpDiscovererMock
                .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));
            _manifestPoolMock
                .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
            _profileServiceMock
                .Setup(s => s.ProfileExistsForGameClientAsync(nonRetClientId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(nonRetailHasProfile);

            var installation = new GameInstallation(directory, GameInstallationType.Retail, null);
            installation.AvailableGameClients =
            [
                new GameClient
                {
                    Id = nonRetClientId,
                    InstallationId = installation.Id,
                    Name = "Community Patch Non-Retail",
                    PublisherType = CommunityOutpostConstants.PublisherType,
                    GameType = GameType.ZeroHour,
                    Version = GameClientConstants.UnknownVersion,
                    ExecutablePath = executablePath,
                },
            ];
            if (withRetailWindowsClient)
            {
                installation.AvailableGameClients.Add(new GameClient
                {
                    Id = retailClientId,
                    InstallationId = installation.Id,
                    Name = "Community Patch",
                    PublisherType = CommunityOutpostConstants.PublisherType,
                    GameType = GameType.ZeroHour,
                    Version = GameClientConstants.UnknownVersion,
                    ExecutablePath = retailExecutablePath,
                });
            }

            var service = CreateService();
            SetupWizardViewModel? capturedVm = null;
            service.DialogShower = vm =>
            {
                capturedVm = vm;
                vm.ConfirmCommand.Execute(null);
                return Task.FromResult(true);
            };

            var result = await service.RunSetupWizardAsync([installation], CancellationToken.None);

            var retailItem = capturedVm?.Items.FirstOrDefault(i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchRetailCode));
            var nonRetItem = capturedVm?.Items.FirstOrDefault(i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchNonRetCode));
            Assert.NotNull(retailItem);
            Assert.Equal(GameClientConstants.WizardActionTypes.Install, retailItem.ActionType);
            Assert.False(retailItem.IsSelected);
            Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.CommunityPatchAction);
            if (nonRetailHasProfile)
            {
                Assert.Null(nonRetItem);
                Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.CommunityPatchNonRetAction);
            }
            else
            {
                Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, nonRetItem?.ActionType);
                Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, result.CommunityPatchNonRetAction);
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// Verifies that every wizard item's title, description, action label and status label, and the
    /// wizard's own labels, are resolved through localization keys, while the selection decisions
    /// still follow the item metadata.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WithLocalizationService_ResolvesItemTextThroughKeysAsync()
    {
        // Arrange
        const string retailVersion = "23-07-2026";
        const string nonRetVersion = "11-09-2026";

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items =
                [
                    new ContentSearchResult
                    {
                        Id = "generalszh_11-09-2026_NonRet.zip",
                        Name = "Community Patch 11-09-2026 (Non-Retail)",
                        Version = nonRetVersion,
                        Tags = { "nonretail" },
                    },
                    new ContentSearchResult
                    {
                        Id = "generalszh_23-07-2026.zip",
                        Name = "Community Patch 23-07-2026",
                        Version = retailVersion,
                    },
                ],
            }));

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var service = CreateService(localizationService: new MarkerLocalizationService());

        SetupWizardViewModel? capturedVm = null;
        service.DialogShower = vm =>
        {
            capturedVm = vm;
            vm.ConfirmCommand.Execute(null);
            return Task.FromResult(true);
        };

        // Act
        var result = await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert
        Assert.NotNull(capturedVm);
        var retailTitle = MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.CommunityPatchRetailTitle);
        var nonRetTitle = MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.CommunityPatchNonRetailTitle);
        var goTitle = MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.GeneralsOnlineTitle);
        var shTitle = MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.SuperHackersTitle);

        var retailItem = Assert.Single(capturedVm.Items, i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchRetailCode));
        Assert.Equal(retailTitle, retailItem.Title);
        Assert.Equal(
            MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.InstallVersionDescription, retailTitle, retailVersion),
            retailItem.Description);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.DownloadAndInstallAction), retailItem.ActionLabel);
        Assert.Equal(GameClientConstants.WizardStatuses.Missing, retailItem.Status);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.MissingStatus), retailItem.StatusLabel);

        var nonRetItem = Assert.Single(capturedVm.Items, i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchNonRetCode));
        Assert.Equal(nonRetTitle, nonRetItem.Title);
        Assert.Equal(
            MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.InstallVersionDescription, nonRetTitle, nonRetVersion)
                + " " + MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.NonRetailIncompatibleNotice),
            nonRetItem.Description);

        // Discovery found no Generals Online or TheSuperHackers version, so the unversioned descriptions are used
        var goItem = Assert.Single(capturedVm.Items, i => Equals(i.Metadata, PublisherTypeConstants.GeneralsOnline));
        Assert.Equal(goTitle, goItem.Title);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.InstallDescription, goTitle), goItem.Description);

        var shItem = Assert.Single(capturedVm.Items, i => Equals(i.Metadata, PublisherTypeConstants.TheSuperHackers));
        Assert.Equal(shTitle, shItem.Title);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.InstallDescription, shTitle), shItem.Description);

        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.Title), capturedVm.Title);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.Skip), capturedVm.CancelLabel);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.ContinueWithCount, 2), capturedVm.ConfirmLabel);

        Assert.True(result.Confirmed);
        Assert.Equal(GameClientConstants.WizardActionTypes.Install, result.CommunityPatchAction);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.CommunityPatchNonRetAction);
        Assert.Equal(GameClientConstants.WizardActionTypes.Install, result.GeneralsOnlineAction);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.SuperHackersAction);
    }

    /// <summary>
    /// Verifies that the update and create profile item texts are resolved through localization keys.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RunSetupWizardAsync_WithLocalizationService_ResolvesUpdateAndCreateProfileTextThroughKeysAsync()
    {
        // Arrange
        const string latestVersion = "082826_QFE1";
        const string staleVersion = "070126";
        const string staleManifestId = "1.70126.generalsonline.gameclient.60hz";
        const string retailVersion = "23-07-2026";
        const string retailManifestId = "1.0.communityoutpost.gameclient.communitypatch";

        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [new ContentSearchResult { Version = latestVersion }],
            }));

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items =
                [
                    new ContentSearchResult
                    {
                        Id = "generalszh_23-07-2026.zip",
                        Name = "Community Patch 23-07-2026",
                        Version = retailVersion,
                    },
                ],
            }));

        var staleGoManifest = CreateGameClientManifest(staleManifestId, "Generals Online", staleVersion, PublisherTypeConstants.GeneralsOnline);
        var retailManifest = CreateGameClientManifest(retailManifestId, "Community Patch", retailVersion, CommunityOutpostConstants.PublisherType);

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([staleGoManifest, retailManifest]));

        _profileServiceMock
            .Setup(s => s.ProfileExistsForGameClientAsync(staleManifestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var service = CreateService(localizationService: new MarkerLocalizationService());

        SetupWizardViewModel? capturedVm = null;
        service.DialogShower = vm =>
        {
            capturedVm = vm;
            vm.ConfirmCommand.Execute(null);
            return Task.FromResult(true);
        };

        // Act
        await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert
        Assert.NotNull(capturedVm);
        var goTitle = MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.GeneralsOnlineTitle);
        var goItem = Assert.Single(capturedVm.Items, i => Equals(i.Metadata, PublisherTypeConstants.GeneralsOnline));
        Assert.Equal(GameClientConstants.WizardActionTypes.Update, goItem.ActionType);
        Assert.Equal(
            MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.UpdateVersionDescription, goTitle, latestVersion),
            goItem.Description);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.UpdateReinstallAction), goItem.ActionLabel);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.InstalledStatus), goItem.StatusLabel);

        var retailTitle = MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.CommunityPatchRetailTitle);
        var retailItem = Assert.Single(capturedVm.Items, i => Equals(i.Metadata, CommunityOutpostConstants.CommunityPatchRetailCode));
        Assert.Equal(GameClientConstants.WizardActionTypes.CreateProfile, retailItem.ActionType);
        Assert.Equal(
            MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.CreateProfileVersionDescription, retailTitle, retailVersion),
            retailItem.Description);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.CreateProfileAction), retailItem.ActionLabel);
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.DownloadedStatus), retailItem.StatusLabel);
    }

    /// <summary>
    /// Verifies that every wizard action type gets a non-empty localized action label, and that an
    /// action type without a specific label falls back to the default action key.
    /// </summary>
    [Fact]
    public void ApplyDisplayLabels_ForEveryActionType_SetsNonEmptyLocalizedActionLabel()
    {
        var service = CreateService(localizationService: new MarkerLocalizationService());
        var actionTypes = typeof(GameClientConstants.WizardActionTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(actionTypes);
        foreach (var actionType in actionTypes)
        {
            var item = new SetupWizardItemViewModel { ActionType = actionType };

            service.ApplyDisplayLabels(item);

            var expectedKey = actionType switch
            {
                GameClientConstants.WizardActionTypes.Update => GameClientConstants.WizardLocalizationKeys.UpdateReinstallAction,
                GameClientConstants.WizardActionTypes.CreateProfile => GameClientConstants.WizardLocalizationKeys.CreateProfileAction,
                GameClientConstants.WizardActionTypes.Install => GameClientConstants.WizardLocalizationKeys.DownloadAndInstallAction,
                _ => GameClientConstants.WizardLocalizationKeys.DefaultAction,
            };
            Assert.Equal(MarkerLocalizationService.Marker(expectedKey), item.ActionLabel);
        }
    }

    /// <summary>
    /// Verifies that without a localization service an unmapped action type still gets the English default label.
    /// </summary>
    [Fact]
    public void ApplyDisplayLabels_WithoutLocalizationServiceForUnmappedActionType_UsesEnglishDefault()
    {
        var service = CreateService();
        var item = new SetupWizardItemViewModel { ActionType = GameClientConstants.WizardActionTypes.None };

        service.ApplyDisplayLabels(item);

        Assert.Equal(GameClientConstants.WizardFallbackText[GameClientConstants.WizardLocalizationKeys.DefaultAction], item.ActionLabel);
    }

    /// <summary>
    /// Verifies that a discovered latest version of "unknown" in any casing is treated as no version,
    /// so the item gets the unversioned description instead of "Download and install Generals Online unknown.".
    /// </summary>
    /// <param name="unknownVersion">The unknown version string reported by discovery.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Theory]
    [InlineData("unknown")]
    [InlineData("UNKNOWN")]
    public async Task RunSetupWizardAsync_WhenLatestVersionIsUnknownInAnyCase_UsesUnversionedDescriptionAsync(string unknownVersion)
    {
        // Arrange
        _goDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult
            {
                Items = [new ContentSearchResult { Version = unknownVersion }],
            }));

        _cpDiscovererMock
            .Setup(d => d.DiscoverAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentDiscoveryResult>.CreateSuccess(new ContentDiscoveryResult { Items = [] }));

        _manifestPoolMock
            .Setup(p => p.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));

        var service = CreateService(localizationService: new MarkerLocalizationService());

        SetupWizardViewModel? capturedVm = null;
        service.DialogShower = vm =>
        {
            capturedVm = vm;
            return Task.FromResult(false);
        };

        // Act
        await service.RunSetupWizardAsync([], CancellationToken.None);

        // Assert
        Assert.NotNull(capturedVm);
        var goTitle = MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.GeneralsOnlineTitle);
        var goItem = Assert.Single(capturedVm.Items, i => Equals(i.Metadata, PublisherTypeConstants.GeneralsOnline));
        Assert.Equal(MarkerLocalizationService.Marker(GameClientConstants.WizardLocalizationKeys.InstallDescription, goTitle), goItem.Description);
    }

    /// <summary>
    /// Verifies that every wizard localization key has an English fallback and that each fallback
    /// equals the neutral <c>Strings.resx</c> value, so the two cannot drift apart.
    /// </summary>
    [Fact]
    public void WizardFallbackText_CoversEveryKeyAndMatchesNeutralResources()
    {
        var resourceManager = new System.Resources.ResourceManager(
            LocalizationConstants.StringResourceBaseName,
            typeof(GenHub.Common.Services.LocalizationService).Assembly);
        var keys = typeof(GameClientConstants.WizardLocalizationKeys)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

        Assert.NotEmpty(keys);
        Assert.Equal(keys.Count, GameClientConstants.WizardFallbackText.Count);
        foreach (var key in keys)
        {
            Assert.True(GameClientConstants.WizardFallbackText.TryGetValue(key, out var fallback), $"No fallback for {key}");
            Assert.Equal(resourceManager.GetString(key, System.Globalization.CultureInfo.InvariantCulture), fallback);
        }
    }

    private static byte[] HostNativeExecutableHeader() => OperatingSystem.IsLinux()
        ? [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]
        : [0xCF, 0xFA, 0xED, 0xFE, 0x0C, 0x00, 0x00, 0x01];

    private SetupWizardService CreateService(SuperHackersProvider? superHackersProvider = null, ILocalizationService? localizationService = null)
    {
        return new SetupWizardService(
            _profileServiceMock.Object,
            _cpDiscovererMock.Object,
            _goDiscovererMock.Object,
            superHackersProvider!, // Null is caught by null check / try-catch
            _manifestPoolMock.Object,
            NullLogger<SetupWizardService>.Instance,
            localizationService);
    }

    private ContentManifest CreateGameClientManifest(string id, string name, string version, string publisherType)
    {
        return new ContentManifest
        {
            Id = ManifestId.Create(id),
            Name = name,
            Version = version,
            ContentType = ContentType.GameClient,
            Publisher = new PublisherInfo { PublisherType = publisherType },
        };
    }

    private Mock<SuperHackersProvider> CreateSuperHackersProviderMock(string version)
    {
        var discovererMock = new Mock<IContentDiscoverer>();
        discovererMock.Setup(x => x.SourceName).Returns(PublisherTypeConstants.TheSuperHackers);

        var resolverMock = new Mock<IContentResolver>();
        resolverMock.Setup(x => x.ResolverId).Returns(SuperHackersConstants.ResolverId);

        var delivererMock = new Mock<IContentDeliverer>();
        delivererMock.Setup(x => x.SourceName).Returns(ContentSourceNames.GitHubDeliverer);

        var providerMock = new Mock<SuperHackersProvider>(
            Mock.Of<IProviderDefinitionLoader>(),
            new[] { discovererMock.Object },
            new[] { resolverMock.Object },
            new[] { delivererMock.Object },
            Mock.Of<IContentValidator>(),
            NullLogger<SuperHackersProvider>.Instance,
            Mock.Of<IInstallationInstructionsService>());

        providerMock
            .Setup(p => p.SearchAsync(It.IsAny<ContentSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentSearchResult>>.CreateSuccess(
                [new ContentSearchResult { Version = version }]));

        return providerMock;
    }
}
