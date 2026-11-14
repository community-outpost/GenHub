using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>Checks action contrast independently from the navigation accent palette.</summary>
public class ActionButtonPaletteTests
{
    /// <summary>Gets every shipped accent and a few custom edge cases.</summary>
    public static IEnumerable<object[]> AccentColors => ThemeConstants.AllThemes
        .Select(theme => new object[] { theme.PrimaryHex })
        .Concat(new[] { "#000000", "#FFFFFF", "#101020", "#00FFFFFF" }.Select(hex => new object[] { hex }));

    /// <summary>White ink remains readable and states remain distinct for every palette.</summary>
    /// <param name="accentHex">The primary theme accent.</param>
    [Theory]
    [MemberData(nameof(AccentColors))]
    public void Create_AllStatesHaveWhiteTextContrast(string accentHex)
    {
        var palette = ActionButtonPalette.Create(Color.Parse(accentHex));
        foreach (var fill in new[] { palette.Rest, palette.Hover, palette.Pressed })
        {
            Assert.Equal(255, fill.A);
            Assert.True(WhiteContrast(fill) >= 4.5, $"{accentHex}: {fill} does not provide 4.5:1 white contrast.");
        }

        Assert.True(WhiteContrast(palette.Hover) >= 5.2);
        Assert.True(WhiteContrast(palette.Pressed) > WhiteContrast(palette.Rest));
        Assert.True(WhiteContrast(palette.Rest) > WhiteContrast(palette.Hover));
        Assert.NotEqual(palette.Rest, palette.Hover);
        Assert.NotEqual(palette.Rest, palette.Pressed);
        Assert.NotEqual(palette.Hover, palette.Pressed);
    }

    /// <summary>The approved default purple treatment retains the exact reviewed values.</summary>
    [Fact]
    public void Create_DefaultPurpleUsesApprovedFills()
    {
        var palette = ActionButtonPalette.Create(Color.Parse(ThemeConstants.DefaultTheme.PrimaryHex));
        Assert.Equal(Color.Parse("#7540B8"), palette.Rest);
        Assert.Equal(Color.Parse("#8651C9"), palette.Hover);
        Assert.Equal(Color.Parse("#65339F"), palette.Pressed);
    }

    /// <summary>Custom theme IDs cannot bypass action contrast or change the navigation accent.</summary>
    [AvaloniaFact]
    public void ApplyTheme_UpdatesActionResourcesFromColorAndPreservesAccent()
    {
        var application = Avalonia.Application.Current!;
        var previous = application.Resources.ToDictionary(entry => entry.Key, entry => entry.Value);
        var service = new ThemeService(Mock.Of<IConfigurationProviderService>(), NullLogger<ThemeService>.Instance);
        var icon = new PathIcon { Data = Geometry.Parse("M0,0 L8,4 L0,8 Z") };
        var button = new Button { Content = icon };
        button.Classes.Add("action-primary");
        var window = new Window { Width = 320, Height = 160, Content = button };
        try
        {
            window.Show();
            foreach (var preset in ThemeConstants.AllThemes)
            {
                service.ApplyTheme(preset with { Id = "custom-preview" });
                Dispatcher.UIThread.RunJobs();
                var expected = ActionButtonPalette.Create(Color.Parse(preset.PrimaryHex));
                Assert.Equal(expected.Rest, BrushColor(application.FindResource("ActionPrimaryRestBrush")));
                Assert.Equal(expected.Hover, BrushColor(application.FindResource("ActionPrimaryHoverBrush")));
                Assert.Equal(expected.Pressed, BrushColor(application.FindResource("ActionPrimaryPressedBrush")));
                Assert.Equal(Color.Parse(preset.PrimaryHex), BrushColor(application.FindResource("AccentBrush")));
                Assert.Equal(expected.Rest, BrushColor(button.Background));
                Assert.Equal(Colors.White, BrushColor(icon.Foreground));
            }
        }
        finally
        {
            window.Close();
            foreach (var key in application.Resources.Keys.Except(previous.Keys).ToArray())
            {
                application.Resources.Remove(key);
            }

            foreach (var entry in previous)
            {
                application.Resources[entry.Key] = entry.Value;
            }
        }
    }

    private static Color BrushColor(object? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static double WhiteContrast(Color color)
    {
        // WCAG relative luminance: deliberately computed here rather than calling the production helper.
        var channels = new[] { color.R, color.G, color.B }
            .Select(channel => channel / 255.0)
            .Select(channel => channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4))
            .ToArray();
        return 1.05 / ((channels[0] * 0.2126) + (channels[1] * 0.7152) + (channels[2] * 0.0722) + 0.05);
    }
}
