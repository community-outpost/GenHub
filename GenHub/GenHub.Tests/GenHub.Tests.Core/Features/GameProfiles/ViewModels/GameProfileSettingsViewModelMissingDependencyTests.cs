using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.CommunityOutpost;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameInstallations;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.Services;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using ViewModelContentDisplayItem = GenHub.Features.GameProfiles.ViewModels.ContentDisplayItem;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests that the profile editor Save uses the shared dependency validator and dialog.
/// </summary>
public sealed class GameProfileSettingsViewModelMissingDependencyTests
{
    private const string ControlBarProId = "1.10.communityoutpost.addon.cbpr";
    private const string ControlBarProName = "Control Bar Pro (ExiLe)";
    private const string GenToolId = "1.10.communityoutpost.addon.gent";
    private const string InstallationId = "1.104.steam.gameinstallation.zerohour";

    private readonly Mock<IGameProfileManager> _profileManager = new();
    private readonly Mock<IContentManifestPool> _manifestPool = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly Mock<IProfileContentService> _profileContent = new();
    private readonly Dictionary<string, ContentManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _dialogTitles = [];
    private readonly List<string> _dialogContents = [];
    private readonly GameProfileSettingsViewModel _viewModel;
    private CreateProfileRequest? _createRequest;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModelMissingDependencyTests"/> class.
    /// </summary>
    public GameProfileSettingsViewModelMissingDependencyTests()
    {
        _manifests[ControlBarProId] = new ContentManifest
        {
            Id = ManifestId.Create(ControlBarProId),
            Name = ControlBarProName,
            ContentType = ContentType.Addon,
            TargetGame = GameType.ZeroHour,
            Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
            Metadata = new ContentMetadata { Tags = ["contentCode:cbpr"] },
            Dependencies = GenPatcherDependencyBuilder.GetDependencies("cbpr", GenPatcherContentRegistry.GetMetadata("cbpr"))
                .Where(dependency => dependency.Name == "GenTool" || dependency.DependencyType == ContentType.GameInstallation)
                .ToList(),
        };
        _manifests[InstallationId] = new ContentManifest
        {
            Id = ManifestId.Create(InstallationId),
            Name = "Zero Hour Installation",
            ContentType = ContentType.GameInstallation,
            TargetGame = GameType.ZeroHour,
        };

        _manifestPool
            .Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManifestId id, CancellationToken _) =>
                OperationResult<ContentManifest?>.CreateSuccess(_manifests.GetValueOrDefault(id.Value)));
        _profileManager
            .Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateProfileRequest request, CancellationToken _) =>
            {
                _createRequest = request;
                return ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "created" });
            });
        _dialogs
            .Setup(x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string title, string content, IEnumerable<DialogAction> actions, bool _, CancellationToken _) =>
            {
                _dialogTitles.Add(title);
                _dialogContents.Add(content);
                return (actions.FirstOrDefault(action => action.Style == ChosenStyle), false);
            });

        _viewModel = new GameProfileSettingsViewModel(
            _profileManager.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IConfigurationProviderService>(),
            Mock.Of<IProfileContentLoader>(),
            null,
            Mock.Of<INotificationService>(),
            _manifestPool.Object,
            null,
            null,
            null,
            _dialogs.Object,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance,
            dependencyValidator: new ContentDependencyValidator(_manifestPool.Object, NullLogger<ContentDependencyValidator>.Instance),
            profileContentService: _profileContent.Object);

        var installation = new ViewModelContentDisplayItem
        {
            ManifestId = ManifestId.Create(InstallationId),
            DisplayName = "Zero Hour",
            ContentType = ContentType.GameInstallation,
            SourceId = "installation",
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Steam,
            IsEnabled = true,
        };
        _viewModel.Name = "TheSuperHackers Zero Hour";
        _viewModel.AvailableGameInstallations = [installation];
        _viewModel.SelectedGameInstallation = installation;
        _viewModel.EnabledContent.Add(installation);
        _viewModel.EnabledContent.Add(new ViewModelContentDisplayItem
        {
            ManifestId = ManifestId.Create(ControlBarProId),
            DisplayName = ControlBarProName,
            ContentType = ContentType.Addon,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = true,
        });
    }

    private NotificationActionStyle ChosenStyle { get; set; } = NotificationActionStyle.Secondary;

    /// <summary>
    /// Save shows the shared dialog, named by display name, and saves nothing on Cancel.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_ControlBarProWithoutGenTool_ShowsDialogByNameAndCancelSavesNothingAsync()
    {
        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"{ControlBarProName} needs GenTool", Assert.Single(_dialogTitles));
        var content = Assert.Single(_dialogContents);
        var body = content[..content.IndexOf("#### Details", StringComparison.Ordinal)];
        Assert.Contains("**GenTool**", body, StringComparison.Ordinal);
        Assert.DoesNotContain("communityoutpost", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.1.communityoutpost.addon.gent", content[body.Length..], StringComparison.Ordinal);
        _profileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// Install on Save downloads GenTool and saves the profile with it enabled.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_InstallRequiredItems_SavesProfileWithGenToolAsync()
    {
        ChosenStyle = NotificationActionStyle.Primary;
        _profileContent
            .Setup(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _manifests[GenToolId] = new ContentManifest
                {
                    Id = ManifestId.Create(GenToolId),
                    Name = "GenTool",
                    ContentType = ContentType.Addon,
                    Publisher = new PublisherInfo { PublisherType = "communityoutpost" },
                    Metadata = new ContentMetadata { Tags = ["contentCode:gent"] },
                };
                return OperationResult<IReadOnlyList<string>>.CreateSuccess([GenToolId]);
            });

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.IsType<CreateProfileRequest>(_createRequest);
        Assert.Contains(GenToolId, request.EnabledContentIds ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ControlBarProId, request.EnabledContentIds ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A failed download blocks the save.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_InstallFails_SavesNothingAsync()
    {
        ChosenStyle = NotificationActionStyle.Primary;
        _profileContent
            .Setup(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<string>>.CreateFailure("GenHub could not download GenTool."));

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Equal("Error: Missing required dependencies", _viewModel.StatusMessage);
        _profileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
