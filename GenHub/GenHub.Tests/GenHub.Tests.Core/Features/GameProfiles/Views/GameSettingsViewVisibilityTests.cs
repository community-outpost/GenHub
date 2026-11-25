using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Info.ViewModels;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>
/// Tests which rows of the single Game Settings page show for each game client.
/// </summary>
public class GameSettingsViewVisibilityTests
{
    /// <summary>
    /// Verifies TheSuperHackers and Generals Online rows follow their client flags while shared rows always show.
    /// </summary>
    /// <param name="isTheSuperHackers">Whether TheSuperHackers options apply.</param>
    /// <param name="isGeneralsOnline">Whether Generals Online options apply.</param>
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ClientRows_FollowClientVisibility(bool isTheSuperHackers, bool isGeneralsOnline)
    {
        var settings = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab().GameSettingsViewModel;
        settings.UpdateApplicableClientVisibility(isTheSuperHackers, isGeneralsOnline);
        var view = new GameSettingsView { DataContext = settings };
        var window = new Window { Width = 900, Height = 800, Content = view };
        window.Show();

        try
        {
            Dispatcher.UIThread.RunJobs();

            Assert.True(view.FindControl<Border>("ResolutionRow")!.IsEffectivelyVisible);
            Assert.Equal(isTheSuperHackers, view.FindControl<Border>("TshArchiveReplaysRow")!.IsEffectivelyVisible);
            Assert.Equal(isGeneralsOnline, view.FindControl<Border>("GoShowFpsRow")!.IsEffectivelyVisible);
            Assert.Equal(isGeneralsOnline, view.FindControl<TextBlock>("MultiplayerHeader")!.IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }
}
