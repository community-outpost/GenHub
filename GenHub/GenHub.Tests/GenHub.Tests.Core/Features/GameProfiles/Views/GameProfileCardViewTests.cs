using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.GameProfile;
using GenHub.Features.GameProfiles.ViewModels;
using GenHub.Features.GameProfiles.Views;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.GameProfiles.Views;

/// <summary>
/// Headless tests for the Play button on <see cref="GameProfileCardView"/>.
/// </summary>
[Collection(LocalizationCultureCollection.Name)]
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
            var focused = Assert.IsAssignableFrom<Visual>(window.FocusManager?.GetFocusedElement());
            Assert.Same(playButton, focused);
            Assert.True(focused.IsEffectivelyVisible);
            Assert.Equal(1.0, EffectiveOpacity(focused, view));

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

    /// <summary>
    /// Verifies that Play stays clear of the hover tool row and the metadata panel when Russian badges wrap on the narrowest card.
    /// </summary>
    [AvaloniaFact]
    public void PlayButton_ClearsToolRowAndWrappedRussianBadgesAtMinimumWidth()
    {
        var resources = Avalonia.Application.Current!.Resources;
        var previousService = resources[LocalizationConstants.ResourceServiceKey];
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            var russian = CreateLocalizationService();
            Assert.True(russian.SetCulture(CultureInfo.GetCultureInfo("ru")).Success);
            resources[LocalizationConstants.ResourceServiceKey] = russian;

            var profile = new GameProfile
            {
                Id = "card-badges",
                Name = "Card Badges",
                Description = "Wrapped badges",
                GameClient = new GameClient { Name = "Zero Hour", Version = "1.08", GameType = GameType.ZeroHour },
            };
            var viewModel = new GameProfileItemViewModel(profile.Id, profile, string.Empty, string.Empty)
            {
                GameVersion = "1.08",
                Publisher = "Community Outpost",
                RunAsAdmin = true,
            };
            Assert.True(viewModel.HasCompatibilityBadge);
            var badgeKey = viewModel.IsRetailCompatible ? "GameProfiles.Badge.RetailCompatible" : "GameProfiles.Badge.NonRetailCompatible";
            Assert.Equal(russian[badgeKey], viewModel.CompatibilityBadgeText);

            var (window, view) = Show(viewModel, width: 292);

            try
            {
                var card = Assert.Single(view.GetVisualDescendants().OfType<Border>(), border => border.Classes.Contains("profile-card"));
                Assert.Equal(280, card.Bounds.Width);

                var metadata = Assert.Single(view.GetVisualDescendants().OfType<StackPanel>(), panel => panel.Classes.Contains("metadata-panel"));
                var badges = Assert.Single(metadata.GetVisualDescendants().OfType<WrapPanel>());
                var firstBadge = badges.Children.OfType<Border>().First(badge => badge.IsVisible);
                Assert.True(badges.Bounds.Height > firstBadge.Bounds.Height * 1.5, "The badges should wrap onto a second row.");

                var playBounds = BoundsIn(FindButton(view, viewModel.LaunchProfileCommand), view);
                var metadataBounds = BoundsIn(metadata, view);
                Assert.True(
                    playBounds.Bottom <= metadataBounds.Top,
                    $"Play {playBounds} overlaps the metadata panel {metadataBounds}.");

                var editButton = Assert.Single(
                    view.GetVisualDescendants().OfType<Button>(),
                    button => ReferenceEquals(button.Command, viewModel.EditProfileCommand) && button.IsEffectivelyVisible);
                var toolRowBounds = BoundsIn(Assert.IsType<StackPanel>(editButton.GetVisualParent()), view);
                Assert.True(
                    playBounds.Top >= toolRowBounds.Bottom,
                    $"Play {playBounds} overlaps the hover tool row {toolRowBounds}.");
            }
            finally
            {
                window.Close();
            }
        }
        finally
        {
            resources[LocalizationConstants.ResourceServiceKey] = previousService;
            CultureInfo.CurrentUICulture = previousUiCulture;
            CultureInfo.DefaultThreadCurrentUICulture = previousDefaultUiCulture;
        }
    }

    private static LocalizationService CreateLocalizationService()
    {
        var resourceAssembly = typeof(LocalizationService).Assembly;
        var resources = new LocalizationResources(
            new ResourceManager(LocalizationConstants.StringResourceBaseName, resourceAssembly),
            $"{resourceAssembly.GetName().Name}{LocalizationConstants.SatelliteAssemblySuffix}",
            AppContext.BaseDirectory,
            CultureInfo.GetCultureInfo(LocalizationConstants.DefaultCultureName));
        return new LocalizationService(resources, NullLogger<LocalizationService>.Instance);
    }

    private static Rect BoundsIn(Visual visual, Visual root)
    {
        var transform = visual.TransformToVisual(root);
        Assert.True(transform.HasValue);
        return new Rect(visual.Bounds.Size).TransformToAABB(transform.Value);
    }

    private static GameProfileItemViewModel CreateViewModel()
    {
        var profile = new GameProfile { Id = "card-test", Name = "Card Test" };
        return new GameProfileItemViewModel(profile.Id, profile, string.Empty, string.Empty);
    }

    private static (Window Window, GameProfileCardView View) Show(
        GameProfileItemViewModel viewModel,
        bool isHitTestVisible = true,
        double width = 400)
    {
        var view = new GameProfileCardView { DataContext = viewModel, IsHitTestVisible = isHitTestVisible };
        var window = new Window { Width = width, Height = 300, Content = view };
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
