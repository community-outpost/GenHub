using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>
/// Headless tests for the Play button on <see cref="GameProfileCardView"/>.
/// </summary>
public class GameProfileCardViewTests
{
    /// <summary>
    /// Verifies that the Play button is fully visible on a card at rest, without pointer hover.
    /// </summary>
    [AvaloniaFact]
    public void PlayButton_IsFullyVisibleWithoutHover()
    {
        var viewModel = CreateViewModel();
        var (window, view) = Show(viewModel);

        try
        {
            var playButton = FindButton(view, viewModel.LaunchProfileCommand);

            Assert.True(playButton.IsEffectivelyVisible);
            Assert.Equal(1.0, EffectiveOpacity(playButton, view));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that Tab focuses the Play button first and Enter launches the profile.
    /// </summary>
    [AvaloniaFact]
    public void PlayButton_IsFirstTabStopAndEnterLaunches()
    {
        var viewModel = CreateViewModel();
        var launchCount = 0;
        viewModel.LaunchAction = _ =>
        {
            launchCount++;
            return Task.CompletedTask;
        };
        var (window, view) = Show(viewModel);

        try
        {
            var playButton = FindButton(view, viewModel.LaunchProfileCommand);

            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(playButton, window.FocusManager?.GetFocusedElement());

            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, launchCount);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that a running profile hides Play and shows Stop instead.
    /// </summary>
    [AvaloniaFact]
    public void RunningProfile_ShowsStopInsteadOfPlay()
    {
        var viewModel = CreateViewModel();
        var (window, view) = Show(viewModel);

        try
        {
            viewModel.IsProcessRunning = true;
            Dispatcher.UIThread.RunJobs();

            Assert.False(FindButton(view, viewModel.LaunchProfileCommand).IsEffectivelyVisible);
            Assert.True(FindButton(view, viewModel.StopProfileCommand).IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Verifies that a read-only card preview does not show a Play button it cannot press.
    /// </summary>
    [AvaloniaFact]
    public void ReadOnlyPreview_HidesPlay()
    {
        var viewModel = CreateViewModel();
        var (window, view) = Show(viewModel, isHitTestVisible: false);

        try
        {
            Assert.False(FindButton(view, viewModel.LaunchProfileCommand).IsEffectivelyVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static GameProfileItemViewModel CreateViewModel()
    {
        var profile = new GameProfile { Id = "card-test", Name = "Card Test" };
        return new GameProfileItemViewModel(profile.Id, profile, string.Empty, string.Empty);
    }

    private static (Window Window, GameProfileCardView View) Show(GameProfileItemViewModel viewModel, bool isHitTestVisible = true)
    {
        var view = new GameProfileCardView { DataContext = viewModel, IsHitTestVisible = isHitTestVisible };
        var window = new Window { Width = 400, Height = 300, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view);
    }

    private static Button FindButton(GameProfileCardView view, object command) =>
        Assert.Single(view.GetVisualDescendants().OfType<Button>(), button => ReferenceEquals(button.Command, command));

    private static double EffectiveOpacity(Visual visual, Visual root)
    {
        var opacity = visual.Opacity;
        foreach (var ancestor in visual.GetVisualAncestors())
        {
            opacity *= ancestor.Opacity;
            if (ReferenceEquals(ancestor, root))
            {
                break;
            }
        }

        return opacity;
    }
}
