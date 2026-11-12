using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Dialogs;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Tests the missing required items flow of <see cref="ProfileSelectionViewModel"/>.
/// </summary>
public sealed class ProfileSelectionViewModelMissingDependencyTests
{
    private const string ContentId = "1.10.communityoutpost.addon.cbpr";
    private const string ContentName = "Control Bar Pro (ExiLe)";
    private const string ProfileId = "zh-profile";
    private const string GenToolId = "1.10.communityoutpost.addon.gent";

    private static readonly ContentDependencyGap GenToolGap = new()
    {
        DisplayName = "GenTool",
        DependencyId = "1.1.communityoutpost.addon.gent",
        DependencyType = ContentType.Addon,
        CanDownload = true,
        RequiredByName = ContentName,
        RequiredById = ContentId,
    };

    private static readonly ContentDependencyGap InstallationGap = new()
    {
        DisplayName = "Zero Hour Installation (Required)",
        DependencyId = "1.104.any.gameinstallation.zerohour",
        DependencyType = ContentType.GameInstallation,
        CanDownload = false,
        RequiredByName = ContentName,
        RequiredById = ContentId,
    };

    private readonly Mock<IGameProfileManager> _profileManager = new();
    private readonly Mock<IProfileContentService> _profileContent = new();
    private readonly Mock<IContentManifestPool> _manifestPool = new();
    private readonly Mock<INotificationService> _notifications = new();
    private readonly Mock<IDialogService> _dialogs = new();
    private readonly List<string> _dialogTitles = [];
    private readonly List<string> _dialogContents = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="ProfileSelectionViewModelMissingDependencyTests"/> class.
    /// </summary>
    public ProfileSelectionViewModelMissingDependencyTests()
    {
        var profile = new GameProfile
        {
            Id = ProfileId,
            Name = "TheSuperHackers Zero Hour",
            GameClient = new GameClient { GameType = GameType.ZeroHour },
        };
        _profileManager
            .Setup(x => x.GetAllProfilesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<IReadOnlyList<GameProfile>>.CreateSuccess([profile]));
        _manifestPool
            .Setup(x => x.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
        _manifestPool
            .Setup(x => x.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(new ContentManifest
            {
                Id = ManifestId.Create(ContentId),
                Name = ContentName,
                ContentType = ContentType.Addon,
                TargetGame = GameType.ZeroHour,
            }));
    }

    /// <summary>
    /// Choosing Install downloads GenTool, then adds the content, and only then reports success.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task SelectProfile_MissingGenTool_InstallDownloadsThenAddsAsync()
    {
        _profileContent
            .SetupSequence(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateMissingDependencies("missing", [GenToolGap]))
            .ReturnsAsync(AddToProfileResult.CreateSuccess(ContentId, ContentName));
        SetupInstall(OperationResult<IReadOnlyList<string>>.CreateSuccess([GenToolId]));
        ChooseAction(NotificationActionStyle.Primary);
        var vm = await CreateViewModelAsync();

        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        Assert.True(vm.WasSuccessful, vm.ErrorMessage);
        Assert.Equal($"{ContentName} needs GenTool", Assert.Single(_dialogTitles));
        _profileContent.Verify(x => x.InstallMissingDependenciesAsync(It.Is<IReadOnlyList<ContentDependencyGap>>(g => g.Single() == GenToolGap), It.IsAny<CancellationToken>()), Times.Once);
        _profileContent.Verify(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()), Times.Exactly(2));
        _notifications.Verify(x => x.ShowSuccess("Added to Profile", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Choosing Cancel saves nothing and never reports that the content was added.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task SelectProfile_MissingGenTool_CancelSavesNothingAsync()
    {
        _profileContent
            .Setup(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateMissingDependencies("missing", [GenToolGap]));
        ChooseAction(NotificationActionStyle.Secondary);
        var vm = await CreateViewModelAsync();
        var closeRequested = false;
        vm.RequestClose += (_, _) => closeRequested = true;

        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        Assert.False(vm.WasSuccessful);
        Assert.False(closeRequested);
        _profileContent.Verify(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()), Times.Never);
        _profileContent.Verify(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// A failed download is reported and the content is not added.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task SelectProfile_DownloadFails_ReportsErrorAndDoesNotAddAsync()
    {
        _profileContent
            .Setup(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateMissingDependencies("missing", [GenToolGap]));
        SetupInstall(OperationResult<IReadOnlyList<string>>.CreateFailure("GenHub could not download GenTool."));
        ChooseAction(NotificationActionStyle.Primary);
        var vm = await CreateViewModelAsync();

        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        Assert.False(vm.WasSuccessful);
        _profileContent.Verify(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()), Times.Once);
        _notifications.Verify(x => x.ShowError("Failed to Add to Profile", "GenHub could not download GenTool.", It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
        _notifications.Verify(x => x.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// With several missing items, one Install click installs all of them before the profile is created.
    /// An item GenHub cannot download is named with where to get it.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task CreateNewProfile_SeveralItemsMissing_OneClickInstallsAllAsync()
    {
        var created = new GameProfile { Id = "new-profile", Name = "Control Bar Pro Profile" };
        var controlBarCoreGap = GenToolGap with
        {
            DisplayName = "Control Bar Pro (Core)",
            DependencyId = "1.1.communityoutpost.addon.cbpc",
        };
        _profileContent
            .SetupSequence(x => x.CreateProfileWithContentAsync(It.IsAny<string>(), ContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateMissingDependencies("missing", [GenToolGap, controlBarCoreGap, InstallationGap]))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(created));
        SetupInstall(OperationResult<IReadOnlyList<string>>.CreateSuccess([GenToolId, "1.10.communityoutpost.addon.cbpc"]));
        ChooseAction(NotificationActionStyle.Primary);
        var vm = await CreateViewModelAsync();

        await vm.CreateNewProfileCommand.ExecuteAsync(null);

        Assert.True(vm.WasSuccessful, vm.ErrorMessage);
        Assert.Equal($"{ContentName} needs 3 more items", Assert.Single(_dialogTitles));
        var content = Assert.Single(_dialogContents);
        var body = content[..content.IndexOf("#### Details", StringComparison.Ordinal)];
        Assert.Contains("**GenTool**", body, StringComparison.Ordinal);
        Assert.Contains("**Control Bar Pro (Core)**", body, StringComparison.Ordinal);
        Assert.Contains("GenHub can't download this", body, StringComparison.Ordinal);
        Assert.DoesNotContain("communityoutpost", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("gameinstallation", body, StringComparison.OrdinalIgnoreCase);
        _profileContent.Verify(x => x.InstallMissingDependenciesAsync(It.Is<IReadOnlyList<ContentDependencyGap>>(g => g.Count == 3), It.IsAny<CancellationToken>()), Times.Once);
        _profileContent.Verify(x => x.CreateProfileWithContentAsync(It.IsAny<string>(), ContentId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// When an item is still missing after installing, the content is not added and the error names the item.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task SelectProfile_ItemStillMissingAfterInstall_NamesItWithoutRawIdAsync()
    {
        _profileContent
            .Setup(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateMissingDependencies("missing", [GenToolGap, InstallationGap]));
        SetupInstall(OperationResult<IReadOnlyList<string>>.CreateSuccess([GenToolId]));
        ChooseAction(NotificationActionStyle.Primary);
        var vm = await CreateViewModelAsync();

        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        Assert.False(vm.WasSuccessful);
        Assert.Contains("Zero Hour Installation (Required)", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("1.104.any", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Single(_dialogTitles);
        _notifications.Verify(x => x.ShowSuccess(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>
    /// While the missing-items flow runs, the picker cannot start another add or create.
    /// </summary>
    /// <returns>A task that completes when the test finishes.</returns>
    [Fact]
    public async Task SelectProfile_WhileDialogOpen_DisablesPickerCommandsAsync()
    {
        _profileContent
            .Setup(x => x.AddContentToProfileAsync(ProfileId, ContentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AddToProfileResult.CreateMissingDependencies("missing", [GenToolGap]));
        var vm = await CreateViewModelAsync();
        bool? createEnabledDuringDialog = null;
        bool? selectEnabledDuringDialog = null;
        _dialogs
            .Setup(x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string _, IEnumerable<DialogAction> actions, bool _, CancellationToken _) =>
            {
                createEnabledDuringDialog = vm.CreateNewProfileCommand.CanExecute(null);
                selectEnabledDuringDialog = vm.SelectProfileCommand.CanExecute(vm.CompatibleProfiles[0]);
                return (actions.First(action => action.Style == NotificationActionStyle.Secondary), false);
            });

        await vm.SelectProfileCommand.ExecuteAsync(vm.CompatibleProfiles[0]);

        Assert.False(createEnabledDuringDialog);
        Assert.False(selectEnabledDuringDialog);
        Assert.False(vm.IsResolvingDependencies);
        Assert.True(vm.CreateNewProfileCommand.CanExecute(null));
    }

    private void SetupInstall(OperationResult<IReadOnlyList<string>> result) =>
        _profileContent
            .Setup(x => x.InstallMissingDependenciesAsync(It.IsAny<IReadOnlyList<ContentDependencyGap>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private void ChooseAction(NotificationActionStyle style) =>
        _dialogs
            .Setup(x => x.ShowMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IEnumerable<DialogAction>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string title, string content, IEnumerable<DialogAction> actions, bool _, CancellationToken _) =>
            {
                _dialogTitles.Add(title);
                _dialogContents.Add(content);
                return (actions.FirstOrDefault(action => action.Style == style), false);
            });

    private async Task<ProfileSelectionViewModel> CreateViewModelAsync()
    {
        var vm = new ProfileSelectionViewModel(
            NullLogger<ProfileSelectionViewModel>.Instance,
            _profileManager.Object,
            _profileContent.Object,
            _manifestPool.Object,
            _notifications.Object,
            dialogService: _dialogs.Object);
        await vm.LoadProfilesAsync(GameType.ZeroHour, ContentId, ContentName);
        return vm;
    }
}
