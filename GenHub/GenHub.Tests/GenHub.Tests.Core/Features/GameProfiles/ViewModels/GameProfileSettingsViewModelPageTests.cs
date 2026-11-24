using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.GameSettings;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Features.GameProfiles.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.ViewModels;

/// <summary>
/// Tests sidebar page navigation in <see cref="GameProfileSettingsViewModel"/>.
/// </summary>
public class GameProfileSettingsViewModelPageTests
{
    /// <summary>
    /// Verifies the window opens on the General page.
    /// </summary>
    [Fact]
    public void NewViewModel_OpensOnGeneralPage()
    {
        var viewModel = CreateViewModel();

        Assert.Equal(ProfileSettingsPage.General, viewModel.SelectedPage);
        Assert.True(viewModel.IsProfileSettingsTabVisible);
    }

    /// <summary>
    /// Verifies each page selects the matching tab and category.
    /// </summary>
    /// <param name="page">The page to select.</param>
    /// <param name="tabIndex">The expected tab index.</param>
    /// <param name="generalCategory">The expected general category, when the page is a profile page.</param>
    /// <param name="gameCategory">The expected game settings category, when the page is a game page.</param>
    [Theory]
    [InlineData(ProfileSettingsPage.General, 1, GeneralSettingsCategory.Identity, null)]
    [InlineData(ProfileSettingsPage.Appearance, 1, GeneralSettingsCategory.Appearance, null)]
    [InlineData(ProfileSettingsPage.Game, 0, null, null)]
    [InlineData(ProfileSettingsPage.Content, 0, null, null)]
    [InlineData(ProfileSettingsPage.Video, 2, null, SettingsCategory.Video)]
    [InlineData(ProfileSettingsPage.Audio, 2, null, SettingsCategory.Audio)]
    [InlineData(ProfileSettingsPage.Controls, 2, null, SettingsCategory.Controls)]
    [InlineData(ProfileSettingsPage.TheSuperHackers, 2, null, SettingsCategory.TheSuperHackers)]
    [InlineData(ProfileSettingsPage.GeneralsOnline, 2, null, SettingsCategory.GeneralsOnline)]
    public void SelectPageCommand_MapsOntoTabAndCategory(ProfileSettingsPage page, int tabIndex, GeneralSettingsCategory? generalCategory, SettingsCategory? gameCategory)
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedPage = page == ProfileSettingsPage.Video ? ProfileSettingsPage.Audio : ProfileSettingsPage.Video;

        viewModel.SelectPageCommand.Execute(page);

        Assert.Equal(page, viewModel.SelectedPage);
        Assert.Equal(tabIndex, viewModel.SelectedTabIndex);
        if (generalCategory.HasValue)
        {
            Assert.Equal(generalCategory.Value, viewModel.SelectedGeneralCategory);
        }

        if (gameCategory.HasValue)
        {
            Assert.Equal(gameCategory.Value, viewModel.GameSettingsViewModel.SelectedCategory);
        }
    }

    /// <summary>
    /// Verifies setting the tab index directly keeps the selected page in step.
    /// </summary>
    /// <param name="tabIndex">The tab index to set.</param>
    /// <param name="expected">The expected page.</param>
    [Theory]
    [InlineData(0, ProfileSettingsPage.Content)]
    [InlineData(1, ProfileSettingsPage.General)]
    [InlineData(2, ProfileSettingsPage.Controls)]
    public void SettingTabIndex_UpdatesSelectedPage(int tabIndex, ProfileSettingsPage expected)
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedPage = tabIndex == 2 ? ProfileSettingsPage.Appearance : ProfileSettingsPage.Controls;
        viewModel.GameSettingsViewModel.SelectedCategory = SettingsCategory.Controls;

        viewModel.SelectedTabIndex = tabIndex;

        Assert.Equal(expected, viewModel.SelectedPage);
    }

    /// <summary>
    /// Verifies a client page falls back to Video when that client stops applying to the profile.
    /// </summary>
    /// <param name="page">The client page that becomes hidden.</param>
    [Theory]
    [InlineData(ProfileSettingsPage.TheSuperHackers)]
    [InlineData(ProfileSettingsPage.GeneralsOnline)]
    public void HiddenClientPage_FallsBackToVideo(ProfileSettingsPage page)
    {
        var viewModel = CreateViewModel();
        viewModel.GameSettingsViewModel.UpdateApplicableClientVisibility(isTheSuperHackers: true, isGeneralsOnline: true);
        viewModel.SelectedPage = page;

        viewModel.GameSettingsViewModel.UpdateApplicableClientVisibility(isTheSuperHackers: false, isGeneralsOnline: false);

        Assert.Equal(ProfileSettingsPage.Video, viewModel.SelectedPage);
        Assert.Equal(SettingsCategory.Video, viewModel.GameSettingsViewModel.SelectedCategory);
        Assert.True(viewModel.IsGameSettingsTabVisible);
    }

    private static GameProfileSettingsViewModel CreateViewModel() =>
        new(
            Mock.Of<IGameProfileManager>(),
            Mock.Of<IGameSettingsService>(),
            Mock.Of<IConfigurationProviderService>(),
            Mock.Of<IProfileContentLoader>(),
            null,
            null,
            Mock.Of<IContentManifestPool>(),
            null,
            null,
            null,
            null,
            NullLogger<GameProfileSettingsViewModel>.Instance,
            NullLogger<GameSettingsViewModel>.Instance);
}
