using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
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

    private readonly Mock<IProfileContentLoader> _loaderMock = new();
    private readonly Mock<IDialogService> _dialogMock = new();
    private readonly Mock<IContentManifestPool> _manifestPoolMock = new();
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
    }

    /// <summary>
    /// Verifies the Content page lists only checklist content for the profile's game and honors the type filter.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ProfileContentRows_ShowProfileGameAndSelectedType()
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
    public async Task ToggleProfileContent_IncludesThenExcludesWithoutDeleting()
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
    public async Task SelectGameClient_ReplacingCurrentClient_RequiresConfirmation(bool confirmed)
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
    public async Task SelectGameInstallation_OfOtherGame_RequiresConfirmation(bool confirmed)
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
    public async Task SelectGameInstallation_WithoutDialogService_SwitchesGame()
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

    private static List<string> Sorted(params string[] ids) => [.. ids.Order(System.StringComparer.Ordinal)];

    private static IEnumerable<string> RowIds(IEnumerable<ProfileContentRow> rows) => rows.Select(row => row.Item.ManifestId.Value);

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

    private void AddCandidate(ContentType type, string id, GameType gameType)
    {
        if (!_candidates.TryGetValue(type, out var items))
        {
            items = [];
            _candidates[type] = items;
        }

        items.Add(new CoreContentDisplayItem
        {
            Id = id,
            ManifestId = id,
            DisplayName = id,
            ContentType = type,
            GameType = gameType,
        });
    }

    private void SetupConfirmation(bool confirmed) =>
        _dialogMock
            .Setup(dialog => dialog.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ReturnsAsync(confirmed);

    private GameProfileSettingsViewModel CreateViewModel(bool withDialog) =>
        new(
            Mock.Of<IGameProfileManager>(),
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IConfigurationProviderService>(),
            _loaderMock.Object,
            null,
            null,
            _manifestPoolMock.Object,
            null,
            null,
            null,
            withDialog ? _dialogMock.Object : null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);
}
