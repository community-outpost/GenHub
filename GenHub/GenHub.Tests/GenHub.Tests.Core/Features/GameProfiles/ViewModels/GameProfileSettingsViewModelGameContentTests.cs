using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Launching;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.Launching;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentAcquiredMessage = GenHub.Core.Models.Content.ContentAcquiredMessage;
using ContentType = GenHub.Core.Models.Enums.ContentType;
using CoreContentDisplayItem = GenHub.Core.Models.Content.ContentDisplayItem;
using GameInstallationType = GenHub.Core.Models.Enums.GameInstallationType;
using GameType = GenHub.Core.Models.Enums.GameType;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests the Game and Content pages of <see cref="GameProfileSettingsViewModel"/>.
/// </summary>
public class GameProfileSettingsViewModelGameContentTests
{
    private const string ZeroHourInstallId = "1.108.steam.gameinstallation.zerohour";
    private const string GeneralsInstallId = "1.108.steam.gameinstallation.generals";
    private const string CurrentClientId = "1.0.local.gameclient.zh";
    private const string OtherClientId = "1.0.generalsonline.gameclient.zh";
    private const string ZeroHourModId = "1.0.moddb.mod.zhmod";
    private const string GeneralsModId = "1.0.moddb.mod.genmod";
    private const string MapId = "1.0.local.map.custommap";
    private const string PatchId = "1.0.test.mod.item";
    private const string ToolId = "1.0.local.moddingtool.worldbuilder";
    private const string OtherZeroHourInstallId = "1.108.eaapp.gameinstallation.zerohour";
    private const string ProfileId = "content-profile";

    private readonly Mock<IProfileContentLoader> _loaderMock = new();
    private readonly Mock<IDialogService> _dialogMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
    private readonly Mock<IGameProfileManager> _profileManagerMock = new();
    private readonly Mock<ILaunchRegistry> _launchRegistryMock = new();
    private readonly Mock<INotificationService> _notificationMock = new();
    private readonly List<GameLaunchInfo> _activeLaunches = [];
    private readonly Dictionary<ContentType, List<CoreContentDisplayItem>> _candidates = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="GameProfileSettingsViewModelGameContentTests"/> class.
    /// </summary>
    public GameProfileSettingsViewModelGameContentTests()
    {
        _loaderMock
            .Setup(loader => loader.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ReturnsAsync((ContentType type, ObservableCollection<CoreContentDisplayItem> _, IEnumerable<string> _) =>
                new ObservableCollection<CoreContentDisplayItem>(_candidates.TryGetValue(type, out var items) ? items : []));
        _manifestPoolMock
            .Setup(pool => pool.GetAllManifestsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<IEnumerable<ContentManifest>>.CreateSuccess([]));
        _manifestPoolMock
            .Setup(pool => pool.GetManifestAsync(It.IsAny<ManifestId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<ContentManifest?>.CreateSuccess(null));
        _launchRegistryMock.Setup(registry => registry.GetAllActiveLaunchesAsync()).ReturnsAsync(() => [.. _activeLaunches]);
    }

    /// <summary>
    /// Verifies the Content page lists only checklist content for the profile's game and honors the type filter.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ProfileContentRows_ShowProfileGameAndSelectedTypeAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        AddCandidate(ContentType.Mod, GeneralsModId, GameType.Generals);
        AddCandidate(ContentType.Map, MapId, GameType.ZeroHour);
        AddCandidate(ContentType.Patch, PatchId, GameType.Unknown);
        AddCandidate(ContentType.GameClient, OtherClientId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);

        Assert.Equal(Sorted(MapId, PatchId, ZeroHourModId), Sorted([.. RowIds(viewModel.ProfileContentRows)]));
        Assert.DoesNotContain(viewModel.ProfileContentRows, row => row.Item.ContentType is ContentType.GameClient or ContentType.GameInstallation);

        viewModel.SelectedProfileContentFilter = new ProfileContentFilterOption(ContentType.Map);

        Assert.Equal([MapId], RowIds(viewModel.ProfileContentRows));

        viewModel.SelectedProfileContentFilter = ProfileContentFilterOption.All;
        viewModel.GameTypeFilter = GameType.Generals;

        Assert.Equal(Sorted(GeneralsModId, PatchId), Sorted([.. RowIds(viewModel.ProfileContentRows)]));
    }

    /// <summary>
    /// Verifies toggling a row includes it in the profile and toggling again only removes it from the profile.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ToggleProfileContent_IncludesThenExcludesWithoutDeletingAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: true);
        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        var row = Assert.Single(viewModel.ProfileContentRows);
        Assert.False(row.IsIncluded);

        await viewModel.ToggleProfileContentCommand.ExecuteAsync(row.Item);

        Assert.Contains(viewModel.EnabledContent, item => item.ManifestId.Value == ZeroHourModId);
        Assert.True(Assert.Single(viewModel.ProfileContentRows).IsIncluded);

        await viewModel.ToggleProfileContentCommand.ExecuteAsync(viewModel.ProfileContentRows[0].Item);

        Assert.DoesNotContain(viewModel.EnabledContent, item => item.ManifestId.Value == ZeroHourModId);
        Assert.False(Assert.Single(viewModel.ProfileContentRows).IsIncluded);
        _manifestPoolMock.Verify(pool => pool.RemoveManifestAsync(It.IsAny<ManifestId>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        _dialogMock.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// Verifies picking another client asks first and replaces the current client only when confirmed.
    /// </summary>
    /// <param name="confirmed">The answer to the confirmation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectGameClient_ReplacingCurrentClient_RequiresConfirmationAsync(bool confirmed)
    {
        AddCandidate(ContentType.GameClient, OtherClientId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: true);
        viewModel.EnabledContent.Add(CreateItem(ContentType.GameClient, CurrentClientId, GameType.ZeroHour, isEnabled: true));
        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        SetupConfirmation(confirmed);
        var other = viewModel.GameClientRows.Single(row => row.Item.ManifestId.Value == OtherClientId).Item;

        await viewModel.SelectGameClientCommand.ExecuteAsync(other);

        _dialogMock.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
        var selected = Assert.Single(viewModel.GameClientRows, row => row.IsIncluded);
        Assert.Equal(confirmed ? OtherClientId : CurrentClientId, selected.Item.ManifestId.Value);
        Assert.Single(viewModel.EnabledContent, item => item.ContentType == ContentType.GameClient);
    }

    /// <summary>
    /// Verifies picking an installation of the other game asks first and switches games only when confirmed.
    /// </summary>
    /// <param name="confirmed">The answer to the confirmation.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SelectGameInstallation_OfOtherGame_RequiresConfirmationAsync(bool confirmed)
    {
        var viewModel = CreateViewModel(withDialog: true);
        var zeroHour = CreateItem(ContentType.GameInstallation, ZeroHourInstallId, GameType.ZeroHour);
        var generals = CreateItem(ContentType.GameInstallation, GeneralsInstallId, GameType.Generals);
        viewModel.AvailableGameInstallations.Add(zeroHour);
        viewModel.AvailableGameInstallations.Add(generals);
        viewModel.SelectedGameInstallation = zeroHour;
        viewModel.EnabledContent.Add(CreateItem(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, isEnabled: true));
        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        SetupConfirmation(confirmed);

        await viewModel.SelectGameInstallationCommand.ExecuteAsync(generals);

        _dialogMock.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Once);
        Assert.Equal(confirmed ? GameType.Generals : GameType.ZeroHour, viewModel.GameTypeFilter);
        Assert.Equal(confirmed ? GeneralsInstallId : ZeroHourInstallId, viewModel.SelectedGameInstallation?.ManifestId.Value);
        Assert.Equal(!confirmed, viewModel.EnabledContent.Any(item => item.ManifestId.Value == ZeroHourModId));
        Assert.Equal(viewModel.SelectedGameInstallation?.ManifestId.Value, Assert.Single(viewModel.GameInstallationRows, row => row.IsIncluded).Item.ManifestId.Value);
    }

    /// <summary>
    /// Verifies switching games proceeds without a prompt when no dialog service is available.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectGameInstallation_WithoutDialogService_SwitchesGameAsync()
    {
        var viewModel = CreateViewModel(withDialog: false);
        var zeroHour = CreateItem(ContentType.GameInstallation, ZeroHourInstallId, GameType.ZeroHour);
        var generals = CreateItem(ContentType.GameInstallation, GeneralsInstallId, GameType.Generals);
        viewModel.AvailableGameInstallations.Add(zeroHour);
        viewModel.AvailableGameInstallations.Add(generals);
        viewModel.SelectedGameInstallation = zeroHour;
        viewModel.EnabledContent.Add(CreateItem(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, isEnabled: true));

        await viewModel.SelectGameInstallationCommand.ExecuteAsync(generals);

        Assert.Equal(GameType.Generals, viewModel.GameTypeFilter);
        Assert.DoesNotContain(viewModel.EnabledContent, item => item.ManifestId.Value == ZeroHourModId);
    }

    /// <summary>
    /// Verifies the Game and Content pages are filled when an existing profile opens, without a manual refresh.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeForProfile_LoadsGameAndContentRowsAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        AddCandidate(ContentType.GameClient, OtherClientId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);
        SetupExistingProfile(CreateCore(ContentType.Map, MapId, GameType.ZeroHour));

        await viewModel.InitializeForProfileAsync(ProfileId);

        Assert.Equal([MapId, ZeroHourModId], RowIds(viewModel.ProfileContentRows));
        Assert.True(viewModel.ProfileContentRows[0].IsIncluded);
        Assert.Equal([OtherClientId], RowIds(viewModel.GameClientRows));
    }

    /// <summary>
    /// Verifies the Game and Content pages are filled when a new profile opens.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task InitializeForNewProfile_LoadsGameAndContentRowsAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        AddCandidate(ContentType.GameClient, OtherClientId, GameType.ZeroHour);
        _loaderMock.Setup(loader => loader.LoadAvailableGameInstallationsAsync())
            .ReturnsAsync([CreateCore(ContentType.GameInstallation, ZeroHourInstallId, GameType.ZeroHour)]);
        var viewModel = CreateViewModel(withDialog: false);

        await viewModel.InitializeForNewProfileAsync();

        Assert.Equal([ZeroHourModId], RowIds(viewModel.ProfileContentRows));
        Assert.Equal([OtherClientId], RowIds(viewModel.GameClientRows));
        Assert.Equal(ZeroHourInstallId, Assert.Single(viewModel.GameInstallationRows, row => row.IsIncluded).Item.ManifestId.Value);
    }

    /// <summary>
    /// Verifies newly acquired content appears on the Content page.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentAcquired_ReloadsContentRowsAsync()
    {
        var viewModel = CreateViewModel(withDialog: false);
        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        Assert.Empty(viewModel.ProfileContentRows);
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);

        viewModel.Receive(new ContentAcquiredMessage(new ContentManifest { Id = ManifestId.Create(ZeroHourModId) }));

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (viewModel.ProfileContentRows.Count == 0 && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }

        Assert.Equal([ZeroHourModId], RowIds(viewModel.ProfileContentRows));
    }

    /// <summary>
    /// Verifies content stays in name order when an item is checked, and the profile's game installations come first.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Rows_StayInNameOrderWhenToggledAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, "B mod");
        AddCandidate(ContentType.Map, MapId, GameType.ZeroHour, "C map");
        AddCandidate(ContentType.Patch, PatchId, GameType.ZeroHour, "A patch");
        var viewModel = CreateViewModel(withDialog: false);
        viewModel.AvailableGameInstallations.Add(CreateItem(ContentType.GameInstallation, GeneralsInstallId, GameType.Generals));
        viewModel.AvailableGameInstallations.Add(CreateItem(ContentType.GameInstallation, ZeroHourInstallId, GameType.ZeroHour));
        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        Assert.Equal([PatchId, ZeroHourModId, MapId], RowIds(viewModel.ProfileContentRows));

        await viewModel.ToggleProfileContentCommand.ExecuteAsync(viewModel.ProfileContentRows.Single(row => row.Item.ManifestId.Value == MapId).Item);

        Assert.Equal([PatchId, ZeroHourModId, MapId], RowIds(viewModel.ProfileContentRows));
        Assert.True(viewModel.ProfileContentRows[2].IsIncluded);
        Assert.Equal([ZeroHourInstallId, GeneralsInstallId], RowIds(viewModel.GameInstallationRows));
    }

    /// <summary>
    /// Verifies content of a type the checklist does not offer still shows while it is in the profile.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Rows_ListIncludedContentOfAnyTypeAsync()
    {
        var viewModel = CreateViewModel(withDialog: false);
        viewModel.EnabledContent.Add(CreateItem(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, isEnabled: true));
        viewModel.EnabledContent.Add(CreateItem(ContentType.Video, "1.0.local.video.intro", GameType.ZeroHour, isEnabled: true));

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);

        Assert.Contains(viewModel.ProfileContentRows, row => row.Item.ContentType == ContentType.Video && row.IsIncluded);
        Assert.False(viewModel.IsStandaloneToolProfile);
    }

    /// <summary>
    /// Verifies a tool profile lists its tool and refuses a game installation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ToolProfile_ListsToolAndRefusesInstallationAsync()
    {
        var viewModel = CreateViewModel(withDialog: true);
        var installation = CreateItem(ContentType.GameInstallation, ZeroHourInstallId, GameType.ZeroHour);
        viewModel.AvailableGameInstallations.Add(installation);
        viewModel.EnabledContent.Add(CreateItem(ContentType.ModdingTool, ToolId, GameType.Unknown, isEnabled: true));
        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        var statusBefore = viewModel.StatusMessage;

        await viewModel.SelectGameInstallationCommand.ExecuteAsync(installation);

        Assert.Equal(statusBefore, viewModel.StatusMessage);
        Assert.False(viewModel.CanSelectGameInstallation);
        Assert.True(viewModel.IsStandaloneToolProfile);
        Assert.Contains(viewModel.ProfileContentRows, row => row.Item.ManifestId.Value == ToolId && row.IsIncluded);
        Assert.Null(viewModel.SelectedGameInstallation);
        Assert.DoesNotContain(viewModel.EnabledContent, item => item.ContentType == ContentType.GameInstallation);
        _dialogMock.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    /// <summary>
    /// Verifies picking an installation that does not remove content never asks.
    /// </summary>
    /// <param name="targetId">The installation to pick.</param>
    /// <param name="targetGame">The game of the installation to pick.</param>
    /// <param name="hasZeroHourContent">Whether Zero Hour content is enabled before the pick.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(OtherZeroHourInstallId, GameType.ZeroHour, true)]
    [InlineData(GeneralsInstallId, GameType.Generals, false)]
    [InlineData(GeneralsInstallId, GameType.Unknown, true)]
    [InlineData(ZeroHourInstallId, GameType.ZeroHour, true)]
    public async Task SelectGameInstallation_WithoutContentToRemove_DoesNotAskAsync(string targetId, GameType targetGame, bool hasZeroHourContent)
    {
        var viewModel = CreateViewModel(withDialog: true);
        var zeroHour = CreateItem(ContentType.GameInstallation, ZeroHourInstallId, GameType.ZeroHour);
        viewModel.AvailableGameInstallations.Add(zeroHour);
        var target = targetId == ZeroHourInstallId ? zeroHour : CreateItem(ContentType.GameInstallation, targetId, targetGame);
        if (!ReferenceEquals(target, zeroHour))
        {
            viewModel.AvailableGameInstallations.Add(target);
        }

        viewModel.SelectedGameInstallation = zeroHour;
        if (hasZeroHourContent)
        {
            viewModel.EnabledContent.Add(CreateItem(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, isEnabled: true));
        }

        await viewModel.SelectGameInstallationCommand.ExecuteAsync(target);

        _dialogMock.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.Equal(targetId, viewModel.SelectedGameInstallation?.ManifestId.Value);
    }

    /// <summary>
    /// Verifies picking a client only asks when it replaces another client and a dialog service exists.
    /// </summary>
    /// <param name="hasCurrentClient">Whether a client is already in the profile.</param>
    /// <param name="withDialog">Whether a dialog service is available.</param>
    /// <param name="reselectCurrent">Whether the current client is picked again.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public async Task SelectGameClient_WithoutReplacementPrompt_SelectsWithoutAskingAsync(bool hasCurrentClient, bool withDialog, bool reselectCurrent)
    {
        AddCandidate(ContentType.GameClient, OtherClientId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog);
        var current = CreateItem(ContentType.GameClient, CurrentClientId, GameType.ZeroHour, isEnabled: true);
        if (hasCurrentClient)
        {
            viewModel.EnabledContent.Add(current);
        }

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        var target = reselectCurrent ? current : viewModel.GameClientRows.Single(row => row.Item.ManifestId.Value == OtherClientId).Item;

        await viewModel.SelectGameClientCommand.ExecuteAsync(target);

        _dialogMock.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        var selected = Assert.Single(viewModel.EnabledContent, item => item.ContentType == ContentType.GameClient);
        Assert.Equal(reselectCurrent ? CurrentClientId : OtherClientId, selected.ManifestId.Value);
    }

    /// <summary>
    /// Verifies candidate loading skips wrong-type, duplicate, identical and invalid items.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadCandidates_SkipsWrongTypeDuplicateIdenticalAndInvalidItemsAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        AddCandidate(ContentType.Mod, MapId, GameType.ZeroHour, contentType: ContentType.Map);
        AddCandidate(ContentType.Patch, ZeroHourModId, GameType.ZeroHour, contentType: ContentType.Patch);
        AddCandidate(ContentType.Mod, "1.0.moddb.mod.zhmodcopy", GameType.ZeroHour, ZeroHourModId);
        AddCandidate(ContentType.Addon, "not a manifest id", GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.ProfileContentRows);
        Assert.Equal(ZeroHourModId, row.Item.ManifestId.Value);
        Assert.Equal(ContentType.Mod, row.Item.ContentType);
    }

    /// <summary>
    /// Verifies an identical copy of an included item is hidden while the included item stays listed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadCandidates_HidesIdenticalCopyOfIncludedItemAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);
        var included = CreateItem(ContentType.Mod, "1.0.moddb.mod.zhmodcopy", GameType.ZeroHour, isEnabled: true);
        included.DisplayName = ZeroHourModId;
        viewModel.EnabledContent.Add(included);

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);

        var row = Assert.Single(viewModel.ProfileContentRows);
        Assert.Equal("1.0.moddb.mod.zhmodcopy", row.Item.ManifestId.Value);
        Assert.True(row.IsIncluded);
    }

    /// <summary>
    /// Verifies two identical items that are both in the profile both stay listed so either can be unchecked.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Rows_KeepIdenticalItemsThatAreBothIncludedAsync()
    {
        var viewModel = CreateViewModel(withDialog: false);
        var first = CreateItem(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, isEnabled: true);
        var second = CreateItem(ContentType.Mod, "1.0.moddb.mod.zhmodcopy", GameType.ZeroHour, isEnabled: true);
        second.DisplayName = first.DisplayName;
        viewModel.EnabledContent.Add(first);
        viewModel.EnabledContent.Add(second);

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);

        Assert.Equal(Sorted(ZeroHourModId, "1.0.moddb.mod.zhmodcopy"), Sorted([.. RowIds(viewModel.ProfileContentRows)]));
        Assert.All(viewModel.ProfileContentRows, row => Assert.True(row.IsIncluded));
    }

    /// <summary>
    /// Verifies items with the same name but a different publisher or version are not treated as copies.
    /// </summary>
    /// <param name="publisher">The publisher of the second item.</param>
    /// <param name="version">The version of the second item.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData("Other Publisher", null)]
    [InlineData(null, "2.0")]
    public async Task LoadCandidates_KeepsSameNameWithDifferentPublisherOrVersionAsync(string? publisher, string? version)
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, "Shared Name");
        AddCandidate(ContentType.Mod, "1.0.moddb.mod.zhmodcopy", GameType.ZeroHour, "Shared Name");
        var copy = _candidates[ContentType.Mod][1];
        copy.Publisher = publisher ?? string.Empty;
        copy.Version = version ?? string.Empty;
        var viewModel = CreateViewModel(withDialog: false);

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);

        Assert.Equal(2, viewModel.ProfileContentRows.Count);
    }

    /// <summary>
    /// Verifies tools that are not yet in the profile are offered and can be filtered.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ProfileContentRows_OfferToolsNotYetIncludedAsync()
    {
        AddCandidate(ContentType.ModdingTool, ToolId, GameType.Unknown);
        AddCandidate(ContentType.Executable, "1.0.local.executable.launcher", GameType.ZeroHour);
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);

        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);

        Assert.Equal(Sorted(ToolId, "1.0.local.executable.launcher", ZeroHourModId), Sorted([.. RowIds(viewModel.ProfileContentRows)]));
        Assert.Contains(GameProfileSettingsViewModel.ProfileContentFilterOptions, option => option.ContentType == ContentType.ModdingTool);
        Assert.Contains(GameProfileSettingsViewModel.ProfileContentFilterOptions, option => option.ContentType == ContentType.Executable);

        viewModel.SelectedProfileContentFilter = new ProfileContentFilterOption(ContentType.ModdingTool);

        Assert.Equal([ToolId], RowIds(viewModel.ProfileContentRows));
    }

    /// <summary>
    /// Verifies the Game page refuses changes while the profile's game is running, before asking anything.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GamePage_WhileGameRuns_RefusesSelectionWithoutAskingAsync()
    {
        AddCandidate(ContentType.GameClient, OtherClientId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: true);
        SetupExistingProfile(CreateCore(ContentType.GameClient, CurrentClientId, GameType.ZeroHour));
        await viewModel.InitializeForProfileAsync(ProfileId);
        var zeroHour = CreateItem(ContentType.GameInstallation, ZeroHourInstallId, GameType.ZeroHour);
        var generals = CreateItem(ContentType.GameInstallation, GeneralsInstallId, GameType.Generals);
        viewModel.AvailableGameInstallations.Add(zeroHour);
        viewModel.AvailableGameInstallations.Add(generals);
        viewModel.SelectedGameInstallation = zeroHour;
        SetupConfirmation(true);
        _activeLaunches.Add(new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = ProfileId,
            WorkspaceId = ProfileId,
            ProcessInfo = new GameProcessInfo { ProcessId = Environment.ProcessId },
        });
        await viewModel.RefreshHotswapStateAsync();
        var otherClient = viewModel.GameClientRows.Single(row => row.Item.ManifestId.Value == OtherClientId).Item;

        await viewModel.SelectGameClientCommand.ExecuteAsync(otherClient);
        await viewModel.SelectGameInstallationCommand.ExecuteAsync(generals);

        Assert.False(otherClient.CanToggle);
        Assert.False(generals.CanToggle);
        _dialogMock.Verify(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
        Assert.Equal(CurrentClientId, Assert.Single(viewModel.EnabledContent, item => item.ContentType == ContentType.GameClient).ManifestId.Value);
        Assert.Equal(ZeroHourInstallId, viewModel.SelectedGameInstallation?.ManifestId.Value);
        Assert.Equal(GameType.ZeroHour, viewModel.GameTypeFilter);
    }

    /// <summary>
    /// Verifies a failing loader keeps the rows already shown and reports the failure.
    /// </summary>
    /// <param name="rejectsId">Whether the loader fails on an invalid manifest ID rather than an unavailable pool.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoadCandidates_WhenLoaderThrows_KeepsPreviousRowsAndReportsAsync(bool rejectsId)
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);
        await viewModel.RefreshProfileContentCommand.ExecuteAsync(null);
        _notificationMock.Invocations.Clear();
        Exception failure = rejectsId ? new ArgumentException("bad id") : new InvalidOperationException("pool unavailable");
        _loaderMock
            .Setup(loader => loader.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .ThrowsAsync(failure);

        await viewModel.LoadProfileContentCandidatesAsync();

        Assert.Equal([ZeroHourModId], RowIds(viewModel.ProfileContentRows));
        _notificationMock.Verify(notifications => notifications.ShowError(It.IsAny<string>(), failure.Message, It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// Verifies acquisitions that arrive while a reload runs trigger another reload.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentAcquired_DuringReload_ReloadsAgainAsync()
    {
        var firstLoadGate = new TaskCompletionSource();
        var firstLoadStarted = new TaskCompletionSource();
        var lastTypeLoads = 0;
        _loaderMock
            .Setup(loader => loader.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(async (ContentType type, ObservableCollection<CoreContentDisplayItem> _, IEnumerable<string> _) =>
            {
                if (type == ContentType.Executable && Interlocked.Increment(ref lastTypeLoads) == 1)
                {
                    firstLoadStarted.TrySetResult();
                    await firstLoadGate.Task;
                }

                return new ObservableCollection<CoreContentDisplayItem>(_candidates.TryGetValue(type, out var items) ? items : []);
            });
        var viewModel = CreateViewModel(withDialog: false);
        var message = new ContentAcquiredMessage(new ContentManifest { Id = ManifestId.Create(ZeroHourModId) });

        try
        {
            viewModel.Receive(message);
            await PumpUntilAsync(() => firstLoadStarted.Task.IsCompleted);
            AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
            viewModel.Receive(message);
            Dispatcher.UIThread.RunJobs();
            firstLoadGate.SetResult();
            await PumpUntilAsync(() => viewModel.ProfileContentRows.Count > 0);
        }
        finally
        {
            firstLoadGate.TrySetResult();
        }

        Assert.Equal([ZeroHourModId], RowIds(viewModel.ProfileContentRows));
    }

    /// <summary>
    /// Verifies replacing the manifest of an unchecked Content row that is only cached as a candidate reloads the row with the new ID.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ManifestReplaced_ForCandidateOnly_ReloadsRowsAsync()
    {
        const string replacementId = "1.1.moddb.mod.zhmod";
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);
        await viewModel.LoadProfileContentCandidatesAsync();
        _candidates[ContentType.Mod].Clear();
        AddCandidate(ContentType.Mod, replacementId, GameType.ZeroHour);

        viewModel.Receive(new ManifestReplacedMessage(ZeroHourModId, replacementId));
        await PumpUntilAsync(() => viewModel.ProfileContentRows.Any(row => row.Item.ManifestId.Value == replacementId));

        Assert.Equal([replacementId], RowIds(viewModel.ProfileContentRows));
    }

    /// <summary>
    /// Verifies a game started while a replacement is being confirmed blocks the replacement.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectGameClient_WhenGameStartsDuringConfirmation_KeepsCurrentClientAsync()
    {
        AddCandidate(ContentType.GameClient, OtherClientId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: true);
        SetupExistingProfile(CreateCore(ContentType.GameClient, CurrentClientId, GameType.ZeroHour));
        await viewModel.InitializeForProfileAsync(ProfileId);
        _dialogMock
            .Setup(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(() =>
            {
                _activeLaunches.Add(new GameLaunchInfo
                {
                    LaunchId = "launch-1",
                    ProfileId = ProfileId,
                    WorkspaceId = ProfileId,
                    ProcessInfo = new GameProcessInfo { ProcessId = Environment.ProcessId },
                });
                return true;
            });
        var otherClient = viewModel.GameClientRows.Single(row => row.Item.ManifestId.Value == OtherClientId).Item;

        await viewModel.SelectGameClientCommand.ExecuteAsync(otherClient);

        Assert.Equal(CurrentClientId, Assert.Single(viewModel.EnabledContent, item => item.ContentType == ContentType.GameClient).ManifestId.Value);
    }

    /// <summary>
    /// Verifies a slow earlier load cannot overwrite the result of a later one.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task LoadCandidates_WhenLoadsOverlap_LaterLoadWinsAsync()
    {
        var firstLoadGate = new TaskCompletionSource();
        var loadGeneration = new AsyncLocal<int>();
        _loaderMock
            .Setup(loader => loader.LoadAvailableContentAsync(It.IsAny<ContentType>(), It.IsAny<ObservableCollection<CoreContentDisplayItem>>(), It.IsAny<IEnumerable<string>>()))
            .Returns(async (ContentType type, ObservableCollection<CoreContentDisplayItem> _, IEnumerable<string> _) =>
            {
                if (loadGeneration.Value == 1)
                {
                    await firstLoadGate.Task;
                    return new ObservableCollection<CoreContentDisplayItem>(type == ContentType.Map ? [CreateCore(ContentType.Map, MapId, GameType.ZeroHour)] : []);
                }

                return new ObservableCollection<CoreContentDisplayItem>(type == ContentType.Mod ? [CreateCore(ContentType.Mod, ZeroHourModId, GameType.ZeroHour)] : []);
            });
        var viewModel = CreateViewModel(withDialog: false);

        try
        {
            loadGeneration.Value = 1;
            var firstLoad = viewModel.LoadProfileContentCandidatesAsync();
            loadGeneration.Value = 2;
            await viewModel.LoadProfileContentCandidatesAsync();
            firstLoadGate.SetResult();
            await firstLoad;
        }
        finally
        {
            firstLoadGate.TrySetResult();
        }

        Assert.Equal([ZeroHourModId], RowIds(viewModel.ProfileContentRows));
    }

    /// <summary>
    /// Verifies rows still list the profile's own content when no loader is available.
    /// </summary>
    [Fact]
    public void Rows_WithoutLoader_ListEnabledContent()
    {
        var viewModel = new GameProfileSettingsViewModel(null, null, null, null, null, null, null, null, null, null, null, null, null);

        viewModel.EnabledContent.Add(CreateItem(ContentType.Mod, ZeroHourModId, GameType.ZeroHour, isEnabled: true));

        Assert.True(Assert.Single(viewModel.ProfileContentRows).IsIncluded);
    }

    /// <summary>
    /// Verifies content rows follow the hotswap lock when the profile's game starts and stops.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Rows_FollowHotswapLockWhenGameStartsAndStopsAsync()
    {
        AddCandidate(ContentType.Mod, ZeroHourModId, GameType.ZeroHour);
        AddCandidate(ContentType.Map, MapId, GameType.ZeroHour);
        var viewModel = CreateViewModel(withDialog: false);
        SetupExistingProfile();
        await viewModel.InitializeForProfileAsync(ProfileId);
        Assert.All(viewModel.ProfileContentRows, row => Assert.True(row.Item.CanToggle));

        _activeLaunches.Add(new GameLaunchInfo
        {
            LaunchId = "launch-1",
            ProfileId = ProfileId,
            WorkspaceId = ProfileId,
            ProcessInfo = new GameProcessInfo { ProcessId = Environment.ProcessId },
        });
        await viewModel.RefreshHotswapStateAsync();

        Assert.False(viewModel.ProfileContentRows.Single(row => row.Item.ManifestId.Value == ZeroHourModId).Item.CanToggle);
        Assert.True(viewModel.ProfileContentRows.Single(row => row.Item.ManifestId.Value == MapId).Item.CanToggle);

        await viewModel.ToggleProfileContentCommand.ExecuteAsync(viewModel.ProfileContentRows.Single(row => row.Item.ManifestId.Value == ZeroHourModId).Item);

        Assert.DoesNotContain(viewModel.EnabledContent, item => item.ManifestId.Value == ZeroHourModId);

        _activeLaunches.Clear();
        await viewModel.RefreshHotswapStateAsync();

        Assert.All(viewModel.ProfileContentRows, row => Assert.True(row.Item.CanToggle));
    }

    private static List<string> Sorted(params string[] ids) => [.. ids.Order(System.StringComparer.Ordinal)];

    private static IEnumerable<string> RowIds(IEnumerable<ProfileContentRow> rows) => rows.Select(row => row.Item.ManifestId.Value);

    private static async Task PumpUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }

    private static ContentDisplayItem CreateItem(ContentType type, string id, GameType gameType, bool isEnabled = false) => new()
    {
        Id = id,
        ManifestId = ManifestId.Create(id),
        DisplayName = id,
        ContentType = type,
        GameType = gameType,
        InstallationType = type == ContentType.GameInstallation ? GameInstallationType.Steam : GameInstallationType.Unknown,
        IsEnabled = isEnabled,
    };

    private static CoreContentDisplayItem CreateCore(ContentType type, string id, GameType gameType, string? displayName = null) => new()
    {
        Id = id,
        ManifestId = id,
        DisplayName = displayName ?? id,
        ContentType = type,
        GameType = gameType,
        InstallationType = type == ContentType.GameInstallation ? GameInstallationType.Steam : GameInstallationType.Unknown,
    };

    private void SetupExistingProfile(params CoreContentDisplayItem[] enabled)
    {
        foreach (var item in enabled)
        {
            item.IsEnabled = true;
        }

        var profile = new GameProfile
        {
            Id = ProfileId,
            Name = "Content Profile",
            EnabledContentIds = [.. enabled.Select(item => item.ManifestId)],
            GameClient = new GenHub.Core.Models.GameClients.GameClient { Id = "client-zh", Name = "Zero Hour", GameType = GameType.ZeroHour },
        };
        _profileManagerMock.Setup(manager => manager.GetProfileAsync(ProfileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _profileManagerMock.Setup(manager => manager.UpdateProfileAsync(ProfileId, It.IsAny<UpdateProfileRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProfileOperationResult<GameProfile>.CreateSuccess(profile));
        _loaderMock.Setup(loader => loader.LoadEnabledContentForProfileAsync(profile))
            .ReturnsAsync(new ObservableCollection<CoreContentDisplayItem>(enabled));
        _loaderMock.Setup(loader => loader.LoadAvailableGameInstallationsAsync()).ReturnsAsync([]);
    }

    private void AddCandidate(ContentType requestedType, string id, GameType gameType, string? displayName = null, ContentType? contentType = null)
    {
        if (!_candidates.TryGetValue(requestedType, out var items))
        {
            items = [];
            _candidates[requestedType] = items;
        }

        items.Add(CreateCore(contentType ?? requestedType, id, gameType, displayName));
    }

    private void SetupConfirmation(bool confirmed) =>
        _dialogMock
            .Setup(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(confirmed);

    private GameProfileSettingsViewModel CreateViewModel(bool withDialog) =>
        new(
            _profileManagerMock.Object,
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IConfigurationProviderService>(),
            _loaderMock.Object,
            null,
            _notificationMock.Object,
            _manifestPoolMock.Object,
            null,
            null,
            null,
            withDialog ? _dialogMock.Object : null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance,
            launchRegistry: _launchRegistryMock.Object);
}
