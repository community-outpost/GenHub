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
using GenHub.Core.Models.Notifications;
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
    private readonly Mock<IProfileContentLoader> _contentLoader = new();
    private readonly Dictionary<string, ContentManifest> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _dialogTitles = [];
    private readonly List<string> _dialogContents = [];
    private readonly List<string> _dialogDetails = [];
    private readonly GameProfileSettingsViewModel _viewModel;
    private readonly List<CreateProfileRequest> _createRequests = [];

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
                _createRequests.Add(request);
                return ProfileOperationResult<GameProfile>.CreateSuccess(new GameProfile { Id = "created" });
            });
        _dialogs
            .Setup(x => x.ShowMessageWithDetailsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string title, string content, IEnumerable<DialogAction> actions, string _, string details, CancellationToken _) =>
            {
                _dialogTitles.Add(title);
                _dialogContents.Add(content);
                _dialogDetails.Add(details);
                return (actions.FirstOrDefault(action => action.Style == ChosenStyle), false);
            });

        _viewModel = new GameProfileSettingsViewModel(
            _profileManager.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IConfigurationProviderService>(),
            _contentLoader.Object,
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

        Assert.Equal("One required item is missing", Assert.Single(_dialogTitles));
        var content = Assert.Single(_dialogContents);
        var body = content;
        Assert.Contains("**\u2060GenTool**", body, StringComparison.Ordinal);
        Assert.DoesNotContain("communityoutpost", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1.1.communityoutpost.addon.gent", Assert.Single(_dialogDetails), StringComparison.Ordinal);
        Assert.Equal(string.Empty, _viewModel.StatusMessage);
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
        SetupGenToolInstall();

        await _viewModel.SaveCommand.ExecuteAsync(null);

        var request = Assert.Single(_createRequests);
        Assert.Contains(GenToolId, request.EnabledContentIds ?? [], StringComparer.OrdinalIgnoreCase);
        Assert.Contains(ControlBarProId, request.EnabledContentIds ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Installed items show as enabled in the editor, so a save that fails after the install
    /// keeps them for the next attempt.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_InstallThenSaveFails_KeepsInstalledItemEnabledAsync()
    {
        ChosenStyle = NotificationActionStyle.Primary;
        _profileManager
            .Setup(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateFailure("disk full"));
        _contentLoader
            .Setup(x => x.CreateManifestDisplayItem(It.IsAny<ContentManifest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns((ContentManifest manifest, string? _, string? _, bool _) => new GenHub.Core.Models.Content.ContentDisplayItem
            {
                Id = manifest.Id.Value,
                ManifestId = manifest.Id.Value,
                DisplayName = manifest.Name,
                ContentType = manifest.ContentType,
                GameType = GameType.ZeroHour,
                InstallationType = GameInstallationType.Unknown,
            });
        SetupGenToolInstall();

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Contains(_viewModel.EnabledContent, item => item.ManifestId.Value == GenToolId && item.IsEnabled);
    }

    /// <summary>
    /// Closing the editor while the dialog or download is pending cancels the install.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_EditorClosedDuringDialog_CancelsInstallAsync()
    {
        _dialogs
            .Setup(x => x.ShowMessageWithDetailsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, string _, IEnumerable<DialogAction> _, string _, string _, CancellationToken token) =>
            {
                _viewModel.CancelPendingOperations();
                token.ThrowIfCancellationRequested();
                return Task.FromResult<(DialogAction?, bool)>((null, false));
            });

        await _viewModel.SaveCommand.ExecuteAsync(null);

        _profileContent.Verify(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()), Times.Never);
        _profileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Never);
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

    /// <summary>
    /// Changes made in the editor while items download are saved, not the list from before the dialog.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_EditorChangedDuringInstall_SavesCurrentContentAsync()
    {
        const string mapPackId = "1.0.examplepublisher.mappack.tournamentmaps";
        _manifests[mapPackId] = new ContentManifest
        {
            Id = ManifestId.Create(mapPackId),
            Name = "Tournament Maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };
        ChosenStyle = NotificationActionStyle.Primary;
        _profileContent
            .Setup(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                _viewModel.EnabledContent.Single(item => item.ManifestId.Value == ControlBarProId).IsEnabled = false;
                _viewModel.EnabledContent.Add(new ViewModelContentDisplayItem
                {
                    ManifestId = ManifestId.Create(mapPackId),
                    DisplayName = "Tournament Maps",
                    ContentType = ContentType.MapPack,
                    GameType = GameType.ZeroHour,
                    InstallationType = GameInstallationType.Unknown,
                    IsEnabled = true,
                });
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

        var saved = Assert.Single(_createRequests).EnabledContentIds ?? [];
        Assert.Contains(mapPackId, saved, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(GenToolId, saved, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(ControlBarProId, saved, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An item still missing after the install is listed once, even when it is declared twice.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_SameItemStillMissingTwice_ListsItOnceAsync()
    {
        var controlBarPro = _manifests[ControlBarProId];
        var genTool = controlBarPro.Dependencies.Single(dependency => dependency.Name == "GenTool");
        controlBarPro.Dependencies.Add(new ContentDependency
        {
            Id = genTool.Id,
            Name = genTool.Name,
            DependencyType = genTool.DependencyType,
            InstallBehavior = genTool.InstallBehavior,
            MinVersion = "9.0",
        });
        ChosenStyle = NotificationActionStyle.Primary;
        _profileContent
            .Setup(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IReadOnlyList<string>>.CreateSuccess([]));
        var notifications = new List<NotificationMessage>();
        var subscription = _viewModel.LocalNotifications.Subscribe(new NotificationCollector(notifications));
        try
        {
            await _viewModel.SaveCommand.ExecuteAsync(null);
        }
        finally
        {
            subscription.Dispose();
        }

        var error = Assert.Single(notifications, notification => notification.Title == "Missing Dependencies");
        Assert.Single(error.Message.Split('\n'), line => line.Contains("needs GenTool", StringComparison.Ordinal));
        _profileManager.Verify(x => x.CreateProfileAsync(It.IsAny<CreateProfileRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>
    /// An installed item that is listed in the editor but unchecked is checked, so one install lets the save finish.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_InstalledItemListedButUnchecked_ChecksItAndSavesAsync()
    {
        ChosenStyle = NotificationActionStyle.Primary;
        _viewModel.EnabledContent.Add(new ViewModelContentDisplayItem
        {
            ManifestId = ManifestId.Create(GenToolId),
            DisplayName = "GenTool",
            ContentType = ContentType.Addon,
            GameType = GameType.ZeroHour,
            InstallationType = GameInstallationType.Unknown,
            IsEnabled = false,
        });
        SetupGenToolInstall();

        await _viewModel.SaveCommand.ExecuteAsync(null);

        Assert.Single(_dialogTitles);
        Assert.True(_viewModel.EnabledContent.Single(item => item.ManifestId.Value == GenToolId).IsEnabled);
        Assert.Contains(GenToolId, Assert.Single(_createRequests).EnabledContentIds ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An installed item the player removes while the save checks again is not saved behind their back.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task Save_InstalledItemRemovedDuringRecheck_IsNotSavedAsync()
    {
        const string mapPackId = "1.0.examplepublisher.mappack.tournamentmaps";
        var mapPackLookups = 0;
        _manifests[mapPackId] = new ContentManifest
        {
            Id = ManifestId.Create(mapPackId),
            Name = "Tournament Maps",
            ContentType = ContentType.MapPack,
            TargetGame = GameType.ZeroHour,
        };
        _manifestPool
            .Setup(x => x.GetManifestAsync(It.Is<ManifestId>(id => id.Value == mapPackId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (++mapPackLookups == 1)
                {
                    var genTool = _viewModel.EnabledContent.Single(item => item.ManifestId.Value == GenToolId);
                    _viewModel.EnabledContent.Remove(genTool);
                }

                return OperationResult<ContentManifest?>.CreateSuccess(_manifests[mapPackId]);
            });
        _contentLoader
            .Setup(x => x.CreateManifestDisplayItem(It.IsAny<ContentManifest>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .Returns((ContentManifest manifest, string? _, string? _, bool _) => new GenHub.Core.Models.Content.ContentDisplayItem
            {
                Id = manifest.Id.Value,
                ManifestId = manifest.Id.Value,
                DisplayName = manifest.Name,
                ContentType = manifest.ContentType,
                GameType = GameType.ZeroHour,
                InstallationType = GameInstallationType.Unknown,
            });
        ChosenStyle = NotificationActionStyle.Primary;
        _profileContent
            .Setup(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                if (_dialogTitles.Count > 1)
                {
                    return OperationResult<IReadOnlyList<string>>.CreateFailure("GenHub could not download GenTool.");
                }

                _viewModel.EnabledContent.Add(new ViewModelContentDisplayItem
                {
                    ManifestId = ManifestId.Create(mapPackId),
                    DisplayName = "Tournament Maps",
                    ContentType = ContentType.MapPack,
                    GameType = GameType.ZeroHour,
                    InstallationType = GameInstallationType.Unknown,
                    IsEnabled = true,
                });
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

        Assert.Equal(2, _dialogTitles.Count);
        Assert.Empty(_createRequests);
    }

    private void SetupGenToolInstall() =>
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

    private sealed class NotificationCollector(List<NotificationMessage> notifications) : IObserver<NotificationMessage>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }

        public void OnNext(NotificationMessage value) => notifications.Add(value);
    }
}
