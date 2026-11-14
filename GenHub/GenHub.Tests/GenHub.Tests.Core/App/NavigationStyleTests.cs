using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Services;
using GenHub.Common.Views;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Tools.Views.PublisherStudio;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>Verifies navigation state paint in the actual tab and sidebar consumers.</summary>
public class NavigationStyleTests
{
    /// <summary>Selected navigation stays legible across hover, keyboard focus and disabling.</summary>
    /// <param name="family">The real navigation consumer to mount.</param>
    [AvaloniaTheory]
    [InlineData("main")]
    [InlineData("studio")]
    [InlineData("profile")]
    [InlineData("sidebar")]
    public void NavigationConsumer_SelectionAndDisabledStatesRemainDistinct(string family)
    {
        var profile = family is "sidebar" or "profile" ? GenHub.Features.Info.ViewModels.DemoViewModelFactory.CreateDemoProfileSettingsViewModel_SettingsTab() : null;
        using var fixture = profile as IDisposable;
        UserControl view = family switch
        {
            "main" => new MainView { DataContext = null },
            "studio" => new PublisherStudioView(),
            "profile" => new GameProfileNavigationView { DataContext = profile },
            _ => new GameSettingsView { DataContext = profile!.GameSettingsViewModel },
        };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        try
        {
            if (family == "sidebar")
            {
                view.FindControl<Grid>("RootGrid")!.ColumnDefinitions[0].Width = new GridLength(260);
                view.UpdateLayout();
            }

            var role = family switch { "main" => "pill-tab", "studio" => "nav-pill", "profile" => "tab-icon-btn", _ => "sidebar-btn" };
            var button = view.GetVisualDescendants().OfType<Button>().First(item => item.Classes.Contains(role));
            if (family != "sidebar")
            {
                Assert.Equal((CornerRadius)Avalonia.Application.Current!.FindResource("ActionCornerRadius")!, button.CornerRadius);
            }

            button.Command = new RelayCommand(() => { });
            button.IsEnabled = true;
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().First();
            button.Transitions = null;
            presenter.Transitions = null;
            var border = button.GetVisualDescendants().OfType<Border>().FirstOrDefault(item => item.Name == "PART_PillBorder");
            if (border is not null)
            {
                border.Transitions = null;
            }

            button.Classes.Add(family is "main" or "studio" ? "active" : "selected");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavSelectedFill"), BrushColor(button.Background));
            Assert.Equal(ResourceColor("NavSelectedInk"), BrushColor(button.Foreground));
            if (family == "profile")
            {
                Assert.Equal(BrushColor(button.BorderBrush), BrushColor(presenter.BorderBrush));
            }

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var target = button.TranslatePoint(new Point(Math.Min(12, button.Bounds.Width / 2), button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(target);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsPointerOver, $"{family}: {button.Bounds}; position {button.TranslatePoint(default, window)}; target {target}; hit {window.InputHitTest(target)?.GetType().Name}");
            Assert.Equal(ResourceColor("NavSelectedHoverFill"), BrushColor(button.Background));
            Assert.Equal(ResourceColor("NavSelectedInk"), BrushColor(button.Foreground));
            Assert.True(button.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavFocusBrush"), BrushColor(border?.BorderBrush ?? presenter.BorderBrush));
            button.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavDisabledInk"), BrushColor(button.Foreground));
            foreach (var glyph in button.GetVisualDescendants().OfType<PathIcon>())
            {
                Assert.Equal(ResourceColor("NavDisabledInk"), BrushColor(glyph.Foreground));
                Assert.Equal(family == "studio" ? 16 : family == "sidebar" ? 20 : family == "profile" ? 32 : 24, glyph.Width);
                Assert.Equal(glyph.Width, glyph.Height);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Profile icon masks use effective ink, including their disabled state.</summary>
    [AvaloniaFact]
    public void ProfileIconMasks_InheritNavigationInk()
    {
        var profile = GenHub.Features.Info.ViewModels.DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab();
        using var fixture = profile as IDisposable;
        var view = new GameProfileSettingsContentView { DataContext = profile };
        var window = new Window { Width = 1400, Height = 900, Content = view };
        window.Show();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>().First(item => item.Classes.Contains("tab-icon-btn") && item.Content is Panel);
            button.Command = new RelayCommand(() => { });
            button.IsEnabled = true;
            button.Classes.Add("selected");
            var mask = button.GetVisualDescendants().OfType<Border>().Single(item => item.OpacityMask is not null);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BrushColor(button.Foreground), BrushColor(mask.Background));
            button.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavDisabledInk"), BrushColor(mask.Background));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Mounted Studio selection follows live theme updates without replacing the view.</summary>
    [AvaloniaFact]
    public void StudioSelection_TracksLiveAccentTheme()
    {
        var service = new ThemeService(Mock.Of<IConfigurationProviderService>(), NullLogger<ThemeService>.Instance);
        var view = new PublisherStudioView();
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        try
        {
            var button = view.GetVisualDescendants().OfType<Button>().First(item => item.Classes.Contains("nav-pill"));
            button.Command = new RelayCommand(() => { });
            button.Transitions = null;
            button.GetVisualDescendants().OfType<ContentPresenter>().First().Transitions = null;
            button.Classes.Add("active");
            foreach (var theme in ThemeConstants.AllThemes)
            {
                service.ApplyTheme(theme);
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(ResourceColor("NavSelectedFill"), BrushColor(button.Background));
                Assert.Equal(ResourceColor("NavSelectedInk"), BrushColor(button.Foreground));
                Assert.Equal(Color.Parse(theme.PrimaryHex), BrushColor(button.BorderBrush));
            }
        }
        finally
        {
            service.ApplyTheme(ThemeConstants.DefaultTheme);
            window.Close();
        }
    }

    private static Color ResourceColor(string name) => BrushColor((IBrush)Avalonia.Application.Current!.FindResource(name)!);

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
