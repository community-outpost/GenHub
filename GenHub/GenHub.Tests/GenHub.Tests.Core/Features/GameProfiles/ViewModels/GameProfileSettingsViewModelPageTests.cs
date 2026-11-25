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
    /// Verifies each page selects the matching tab.
    /// </summary>
    /// <param name="page">The page to select.</param>
    /// <param name="tabIndex">The expected tab index.</param>
    [Theory]
    [InlineData(ProfileSettingsPage.General, 1)]
    [InlineData(ProfileSettingsPage.Appearance, 1)]
    [InlineData(ProfileSettingsPage.Game, 0)]
    [InlineData(ProfileSettingsPage.Content, 0)]
    [InlineData(ProfileSettingsPage.GameSettings, 2)]
    public void SelectPageCommand_MapsOntoTab(ProfileSettingsPage page, int tabIndex)
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedPage = page == ProfileSettingsPage.GameSettings ? ProfileSettingsPage.Content : ProfileSettingsPage.GameSettings;

        viewModel.SelectPageCommand.Execute(page);

        Assert.Equal(page, viewModel.SelectedPage);
        Assert.Equal(tabIndex, viewModel.SelectedTabIndex);
    }

    /// <summary>
    /// Verifies setting the tab index directly keeps the selected page in step.
    /// </summary>
    /// <param name="tabIndex">The tab index to set.</param>
    /// <param name="expected">The expected page.</param>
    [Theory]
    [InlineData(0, ProfileSettingsPage.Content)]
    [InlineData(1, ProfileSettingsPage.General)]
    [InlineData(2, ProfileSettingsPage.GameSettings)]
    public void SettingTabIndex_UpdatesSelectedPage(int tabIndex, ProfileSettingsPage expected)
    {
        var viewModel = CreateViewModel();
        viewModel.SelectedPage = tabIndex == 2 ? ProfileSettingsPage.Appearance : ProfileSettingsPage.GameSettings;

        viewModel.SelectedTabIndex = tabIndex;

        Assert.Equal(expected, viewModel.SelectedPage);
    }

    /// <summary>
    /// Verifies the Game Settings page stays open when a client's options stop applying to the profile.
    /// </summary>
    [Fact]
    public void ClientVisibilityChange_KeepsGameSettingsPage()
    {
        var viewModel = CreateViewModel();
        viewModel.GameSettingsViewModel.UpdateApplicableClientVisibility(isTheSuperHackers: true, isGeneralsOnline: true);
        viewModel.SelectedPage = ProfileSettingsPage.GameSettings;

        viewModel.GameSettingsViewModel.UpdateApplicableClientVisibility(isTheSuperHackers: false, isGeneralsOnline: false);

        Assert.Equal(ProfileSettingsPage.GameSettings, viewModel.SelectedPage);
        Assert.True(viewModel.IsGameSettingsTabVisible);
        Assert.False(viewModel.GameSettingsViewModel.IsTheSuperHackersVisible);
        Assert.False(viewModel.GameSettingsViewModel.IsGeneralsOnlineVisible);
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
