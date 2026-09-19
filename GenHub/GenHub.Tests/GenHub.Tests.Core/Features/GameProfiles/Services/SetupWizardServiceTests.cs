using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
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
using System.Collections.Generic;
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
        var goItem = capturedVm.Items.FirstOrDefault(i => i.Title == "Generals Online");
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
        var goItem = capturedVm.Items.FirstOrDefault(i => i.Title == "Generals Online");
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
        var retailItem = capturedVm.Items.FirstOrDefault(i => i.Title == "Community Patch (Retail)");
        var nonRetItem = capturedVm.Items.FirstOrDefault(i => i.Title == "Community Patch (Non-Retail)");

        Assert.NotNull(retailItem);
        Assert.NotNull(nonRetItem);

        // Retail item defaults to selected
        Assert.True(retailItem.IsSelected);
        Assert.Equal(retailVersion, retailItem.Version);
        Assert.Equal(GameClientConstants.WizardActionTypes.Install, result.CommunityPatchAction);

        // Non-retail item defaults to unchecked and contains compatibility warning
        Assert.False(nonRetItem.IsSelected);
        Assert.Equal(nonRetVersion, nonRetItem.Version);
        Assert.Contains("Not compatible with retail 1.04 zero hour", nonRetItem.Description);
        Assert.Equal(GameClientConstants.WizardActionTypes.Decline, result.CommunityPatchNonRetAction);
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
            var nonRetItem = vm.Items.FirstOrDefault(i => i.Title == "Community Patch (Non-Retail)");
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

    private SetupWizardService CreateService()
    {
        return new SetupWizardService(
            _profileServiceMock.Object,
            _cpDiscovererMock.Object,
            _goDiscovererMock.Object,
            null!, // superHackersProvider caught by null check / try-catch
            _manifestPoolMock.Object,
            NullLogger<SetupWizardService>.Instance);
    }
}
