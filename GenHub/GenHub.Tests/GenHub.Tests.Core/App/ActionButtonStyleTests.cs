using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>Exercises the rendered shared action roles and live palette resources.</summary>
public class ActionButtonStyleTests
{
    /// <summary>Fluent template states must preserve the role foreground and disabled treatment.</summary>
    /// <param name="role">The shared role class.</param>
    /// <param name="hover">The pointer-over brush resource.</param>
    /// <param name="pressed">The pressed brush resource.</param>
    /// <param name="foreground">The rest-state foreground resource.</param>
    [AvaloniaTheory]
    [InlineData("action-primary", "AccentLightBrush", "AccentBrush", "ActionOnAccentBrush")]
    [InlineData("action-secondary", "SurfaceHoverBrush", "SurfaceElevatedBrush", "TextPrimary")]
    [InlineData("action-danger", "ActionDangerHoverBrush", "ErrorBrush", "ActionOnAccentBrush")]
    [InlineData("action-danger icon-action", "ActionDangerHoverBrush", "ErrorBrush", "ActionOnAccentBrush")]
    [InlineData("icon-action", "SurfaceHoverBrush", "SurfaceElevatedBrush", "TextSecondary")]
    public void ActionButton_PointerAndDisabledStatesUseSharedPalette(string role, string hover, string pressed, string foreground)
    {
        var button = new Button { Content = "Install" };
        button.Classes.AddRange(role.Split(' '));
        var window = new Window { Width = 320, Height = 160, Content = button };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(p => p.Name == "PART_ContentPresenter");

            // Inspect final state brushes without Fluent's interpolation clock.
            presenter.Transitions = null;
            Assert.Equal(role.Contains("icon-action", StringComparison.Ordinal) ? 32 : 36, button.MinHeight);
            Assert.Equal(new CornerRadius(8), button.CornerRadius);
            Assert.Equal(BrushColor((IBrush)Avalonia.Application.Current!.FindResource(foreground)!), BrushColor(presenter.Foreground));
            window.MouseMove(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsPointerOver);
            Assert.Equal(BrushColor((IBrush)Avalonia.Application.Current!.FindResource(hover)!), BrushColor(button.Background));
            Assert.Equal(BrushColor(button.Background), BrushColor(presenter.Background));
            window.MouseDown(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BrushColor((IBrush)Avalonia.Application.Current!.FindResource(pressed)!), BrushColor(presenter.Background));
            window.MouseUp(button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value, MouseButton.Left);
            button.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BrushColor((IBrush)Avalonia.Application.Current!.FindResource("SurfaceElevatedBrush")!), BrushColor(presenter.Background));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Keyboard navigation must reach the button and activate its command once.</summary>
    [AvaloniaFact]
    public void PrimaryButton_KeyboardFocusAndActivationRemainAvailable()
    {
        var clicks = 0;
        var button = new Button { Content = "Install" };
        button.Classes.Add("action-primary");
        button.Click += (_, _) => clicks++;
        var window = new Window { Width = 320, Height = 160, Content = button };
        window.Show();
        try
        {
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsKeyboardFocusWithin);
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(p => p.Name == "PART_ContentPresenter");
            Assert.Equal(new Thickness(2), presenter.BorderThickness);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.Equal(1, clicks);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Changing the application palette updates mounted controls.</summary>
    [AvaloniaFact]
    public void PrimaryButton_UpdatesWhenAccentResourceChanges()
    {
        var app = Avalonia.Application.Current!;
        var hadOverride = app.Resources.TryGetValue("AccentBrush", out var previous);
        var button = new Button { Content = "Install" };
        button.Classes.Add("action-primary");
        var window = new Window { Width = 320, Height = 160, Content = button };
        window.Show();
        try
        {
            var replacement = new SolidColorBrush(Colors.Cyan);
            app.Resources["AccentBrush"] = replacement;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.Cyan, BrushColor(button.Background));
        }
        finally
        {
            if (hadOverride)
            {
                app.Resources["AccentBrush"] = previous;
            }
            else
            {
                app.Resources.Remove("AccentBrush");
            }

            window.Close();
        }
    }

    /// <summary>All supplied accent palettes keep normal text above 4.5:1 contrast.</summary>
    [Fact]
    public void ActionForeground_HasReadableContrastAcrossShippedPalettes()
    {
        var ink = Colors.Black;
        foreach (var theme in ThemeConstants.AllThemes)
        {
            foreach (var hex in new[] { theme.PrimaryHex, theme.LightHex })
            {
                var contrast = (Luminance(Color.Parse(hex)) + 0.05) / (Luminance(ink) + 0.05);
                Assert.True(contrast >= 4.5, $"{theme.Id} {hex}: {contrast:F2}:1");
            }
        }
    }

    /// <summary>Wrapped translations can grow vertically instead of clipping at a fixed height.</summary>
    [AvaloniaFact]
    public void LongCaption_GrowsAtNarrowWidthAndUtilityIconKeepsItsTarget()
    {
        var caption = new TextBlock
        {
            Text = "Установить необходимые компоненты تثبيت العناصر المطلوبة",
            TextWrapping = TextWrapping.Wrap,
        };
        var button = new Button { Width = 180, Content = caption };
        button.Classes.Add("action-primary");
        var icon = new PathIcon { Data = Geometry.Parse("M0,0 L16,16") };
        var utility = new Button { Content = icon };
        utility.Classes.Add("icon-action");
        var panel = new StackPanel();
        panel.Children.Add(button);
        panel.Children.Add(utility);
        var window = new Window { Width = 240, Height = 300, Content = panel };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.Bounds.Height > button.MinHeight);
            Assert.True(caption.Bounds.Height >= caption.DesiredSize.Height);
            Assert.True(utility.Bounds.Height >= 32);
            Assert.True(utility.Bounds.Width >= 32);
            Assert.Equal(Colors.Transparent, BrushColor(utility.Background));
            Assert.Equal(16, icon.Width);
            Assert.Equal(16, icon.Height);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>Action glyphs follow semantic ink and command-disabled foreground.</summary>
    [AvaloniaFact]
    public void ActionIcon_FollowsButtonForegroundWhenDisabled()
    {
        var icon = new PathIcon { Data = Geometry.Parse("M0,0 L16,16") };
        var button = new Button { Content = icon };
        button.Classes.Add("action-primary");
        var window = new Window { Width = 240, Height = 160, Content = button };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Colors.Black, BrushColor(icon.Foreground));
            button.IsEnabled = false;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(BrushColor(button.Foreground), BrushColor(icon.Foreground));
            Assert.Equal(BrushColor((IBrush)Avalonia.Application.Current!.FindResource("TextMuted")!), BrushColor(icon.Foreground));
        }
        finally
        {
            window.Close();
        }
    }

    private static Color BrushColor(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static double Luminance(Color color)
    {
        static double Linear(byte channel)
        {
            var value = channel / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linear(color.R)) + (0.7152 * Linear(color.G)) + (0.0722 * Linear(color.B));
    }
}
