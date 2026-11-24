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
    [InlineData("sidebar")]
    public void NavigationConsumer_SelectionAndDisabledStatesRemainDistinct(string family)
    {
        var profile = family == "sidebar" ? GenHub.Features.Info.ViewModels.DemoViewModelFactory.CreateDemoProfileSettingsViewModel_SettingsTab() : null;
        UserControl view = family switch
        {
            "main" => new MainView { DataContext = null },
            "studio" => new PublisherStudioView(),
            _ => new GameProfileSettingsSidebarView { DataContext = profile },
        };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        try
        {
            var role = family switch { "main" => "pill-tab", "studio" => "nav-pill", _ => "sidebar-btn" };
            var button = view.GetVisualDescendants().OfType<Button>().First(item => item.Classes.Contains(role) && item.IsVisible && !item.Classes.Contains("selected") && !item.Classes.Contains("active"));
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

            var selectedClass = family is "main" or "studio" ? "active" : "selected";
            Func<IBrush?> outline = border is not null ? () => border.BorderBrush : () => presenter.BorderBrush;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavRestInk"), BrushColor(button.Foreground));

            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var target = button.TranslatePoint(new Point(Math.Min(12, button.Bounds.Width / 2), button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(target);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsPointerOver, $"{family}: {button.Bounds}; position {button.TranslatePoint(default, window)}; target {target}; hit {window.InputHitTest(target)?.GetType().Name}");
            Assert.Equal(ResourceColor("NavHoverFill"), BrushColor(button.Background));
            Assert.Equal(ResourceColor("NavHoverBorder"), BrushColor(button.BorderBrush));
            Assert.Equal(ResourceColor("NavSelectedInk"), BrushColor(button.Foreground));
            window.MouseDown(target, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavPressedFill"), BrushColor(button.Background));
            window.MouseUp(target, MouseButton.Left);
            window.MouseMove(new Point(window.Width - 1, window.Height - 1));
            TopLevel.GetTopLevel(button)!.FocusManager!.ClearFocus();
            Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsPointerOver);

            button.Classes.Add(selectedClass);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavSelectedFill"), BrushColor(button.Background));
            Assert.Equal(ResourceColor("NavSelectedInk"), BrushColor(button.Foreground));
            var selectedOutline = BrushColor(outline());
            if (family == "sidebar")
            {
                Assert.NotEqual(ResourceColor("NavSelectedBorder"), selectedOutline);
            }

            window.MouseMove(target);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsPointerOver);
            Assert.Equal(ResourceColor("NavSelectedHoverFill"), BrushColor(button.Background));
            Assert.NotEqual(ResourceColor("NavSelectedFill"), BrushColor(button.Background));
            Assert.Equal(ResourceColor("NavSelectedInk"), BrushColor(button.Foreground));
            Assert.Equal(family == "sidebar" ? selectedOutline : ResourceColor("NavSelectedHoverBorder"), BrushColor(outline()));
            Assert.True(button.Focus(NavigationMethod.Tab));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavFocusBrush"), BrushColor(outline()));
            button.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavDisabledInk"), BrushColor(button.Foreground));
            Assert.Equal(ResourceColor("NavSelectedFill"), BrushColor(button.Background));
            Assert.Equal(ResourceColor("NavDisabledBorder"), BrushColor(button.BorderBrush));

            var glyphs = button.GetVisualDescendants().OfType<PathIcon>().ToList();
            Assert.Equal(family is "main" or "sidebar", glyphs.Count == 0);
            foreach (var glyph in glyphs)
            {
                Assert.Equal(ResourceColor("NavDisabledInk"), BrushColor(glyph.Foreground));
                Assert.Equal(family == "studio" ? 16 : 20, glyph.Width);
                Assert.Equal(glyph.Width, glyph.Height);
            }

            button.Classes.Remove(selectedClass);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(ResourceColor("NavDisabledFill"), BrushColor(button.Background));
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
                var primary = Color.Parse(theme.PrimaryHex);
                Assert.Equal(Color.FromArgb(0x25, primary.R, primary.G, primary.B), BrushColor(button.Background));
                Assert.Equal(Color.FromArgb(0x20, primary.R, primary.G, primary.B), ResourceColor("NavSelectedHoverFill"));
                Assert.Equal(ResourceColor("NavSelectedInk"), BrushColor(button.Foreground));
                Assert.Equal(primary, BrushColor(button.BorderBrush));
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
