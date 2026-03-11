using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;
using GenHub.Features.Content.Services;
using GenHub.Features.Storage.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.IO;

namespace GenHub.Tests.Core.Features.Reconciliation;

/// <summary>
/// Tests to verify that workspace strategies are preserved during profile reconciliation.
/// This addresses the critical requirement that profiles must maintain their WorkspaceStrategy
/// (e.g., HardLink) when being updated through reconciliation processes.
/// </summary>
public class ReconciliationStrategyTests : IDisposable
{
    private readonly Mock<IGameProfileManager> _profileManagerMock;
    private readonly Mock<IWorkspaceManager> _workspaceManagerMock;
    private readonly Mock<IContentManifestPool> _manifestPoolMock;
    private readonly Mock<ICasLifecycleManager> _casServiceMock;
    private readonly Mock<ILogger<ContentReconciliationService>> _loggerMock;
    private readonly ContentReconciliationService _service;
    private readonly string _tempCasPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReconciliationStrategyTests"/> class.
    /// </summary>
    public ReconciliationStrategyTests()
    {
        _profileManagerMock = new Mock<IGameProfileManager>();
        _workspaceManagerMock = new Mock<IWorkspaceManager>();
        _manifestPoolMock = new Mock<IContentManifestPool>();
        _casServiceMock = new Mock<ICasLifecycleManager>();
        _loggerMock = new Mock<ILogger<ContentReconciliationService>>();

        // Create CasReferenceTracker with required dependencies
        _tempCasPath = Path.Combine(Path.GetTempPath(), "GenHubTests", Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempCasPath);
        var casConfig = Options.Create(new CasConfiguration { CasRootPath = _tempCasPath });
        var mockCasLogger = new Mock<ILogger<CasReferenceTracker>>();
        var casReferenceTracker = new CasReferenceTracker(casConfig, mockCasLogger.Object);

        _service = new ContentReconciliationService(
            _profileManagerMock.Object,
            _workspaceManagerMock.Object,
            _manifestPoolMock.Object,
            casReferenceTracker, // Provided real instance as required
            _casServiceMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that bulk manifest replacement preserves the workspace strategy for a profile.
    /// </summary>
    /// <param name="strategy">The strategy to test.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData(WorkspaceStrategy.HardLink)]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.FullCopy)]
    public async Task ReconcileBulkManifestReplacement_ShouldPreserveStrategy(WorkspaceStrategy strategy)
    {
        // Arrange
        var profileId = $"profile_{strategy}";
        var originalProfile = new GameProfile
        {
            Id = profileId,
            Name = $"My {strategy} Profile",
            WorkspaceStrategy = strategy,
            EnabledContentIds = ["1.0.local.mod.oldcontent"],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([originalProfile]));

        _profileManagerMock.Setup(x => x.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(originalProfile));

        // Mock manifest pool to return the new manifest
        var newManifest = new ContentManifest
        {
            Id = "1.0.local.mod.newcontent",
            Name = "New Manifest",
            Version = "1.0.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            TargetGame = GameType.Generals,
        };
        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(m => m.Value == "1.0.local.mod.newcontent"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(newManifest));

        var replacements = new Dictionary<string, string> { { "1.0.local.mod.oldcontent", "1.0.local.mod.newcontent" } };

        // Act
        await _service.OrchestrateBulkUpdateAsync(replacements, removeOld: false);

        // Assert - Verify WorkspaceStrategy is NOT set (null), which preserves existing strategy
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                profileId,
                It.Is<UpdateProfileRequest>(req => req.WorkspaceStrategy == null),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that bulk manifest replacement preserves different strategies across multiple profiles.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReconcileBulkManifestReplacement_WithMultipleProfiles_ShouldPreserveAllStrategies()
    {
        // Arrange
        var profiles = new[]
        {
            new GameProfile
            {
                Id = "profile_1",
                Name = "HardLink Profile",
                WorkspaceStrategy = WorkspaceStrategy.HardLink,
                EnabledContentIds = ["1.0.local.mod.oldcontent"],
            },
            new GameProfile
            {
                Id = "profile_2",
                Name = "Symlink Profile",
                WorkspaceStrategy = WorkspaceStrategy.SymlinkOnly,
                EnabledContentIds = ["1.0.local.mod.oldcontent"],
            },
            new GameProfile
            {
                Id = "profile_3",
                Name = "Copy Profile",
                WorkspaceStrategy = WorkspaceStrategy.FullCopy,
                EnabledContentIds = ["1.0.local.mod.oldcontent"],
            },
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess(profiles));

        foreach (var profile in profiles)
        {
            _profileManagerMock.Setup(x => x.UpdateProfileAsync(profile.Id, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        }

        // Mock manifest pool to return the new manifest
        var newManifest = new ContentManifest
        {
            Id = "1.0.local.mod.newcontent",
            Name = "New Manifest",
            Version = "1.0.0",
            ContentType = GenHub.Core.Models.Enums.ContentType.Mod,
            TargetGame = GameType.Generals,
        };
        _manifestPoolMock.Setup(x => x.GetManifestAsync(It.Is<ManifestId>(m => m.Value == "1.0.local.mod.newcontent"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(newManifest));

        var replacements = new Dictionary<string, string> { { "1.0.local.mod.oldcontent", "1.0.local.mod.newcontent" } };

        // Act
        await _service.OrchestrateBulkUpdateAsync(replacements, removeOld: false);

        // Assert - Verify all profiles were updated without setting WorkspaceStrategy
        foreach (var profile in profiles)
        {
            _profileManagerMock.Verify(
                x => x.UpdateProfileAsync(
                    profile.Id,
                    It.Is<UpdateProfileRequest>(req => req.WorkspaceStrategy == null),
                    It.IsAny<CancellationToken>()),
                Times.Once,
                $"Profile {profile.Id} should be updated exactly once without changing strategy");
        }
    }

    /// <summary>
    /// Verifies that manifest removal preserves the workspace strategy.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReconcileManifestRemoval_ShouldNotSetWorkspaceStrategy()
    {
        // Arrange
        var profileId = "profile_hardlink";
        var originalProfile = new GameProfile
        {
            Id = profileId,
            Name = "My HardLink Profile",
            WorkspaceStrategy = WorkspaceStrategy.HardLink,
            EnabledContentIds = ["1.0.local.mod.toremove", "1.0.local.mod.other"],
        };

        _profileManagerMock.Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([originalProfile]));

        _profileManagerMock.Setup(x => x.UpdateProfileAsync(It.IsAny<string>(), It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(originalProfile));

        // Act
        await _service.ReconcileManifestRemovalAsync("1.0.local.mod.toremove");

        // Assert - Verify WorkspaceStrategy is NOT set during removal
        // and that the removed manifest is actually gone from the enabled list
        _profileManagerMock.Verify(
            x => x.UpdateProfileAsync(
                profileId,
                It.Is<UpdateProfileRequest>(req =>
                    req.WorkspaceStrategy == null &&
                    req.EnabledContentIds != null &&
                    !req.EnabledContentIds.Contains("1.0.local.mod.toremove") &&
                    req.EnabledContentIds.Contains("1.0.local.mod.other")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempCasPath))
            {
                Directory.Delete(_tempCasPath, true);
            }
        }
        catch (IOException)
        {
            // Ignore cleanup errors in tests
        }
    }
}
