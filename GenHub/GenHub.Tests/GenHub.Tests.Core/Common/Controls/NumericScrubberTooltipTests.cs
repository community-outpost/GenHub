using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GenHub.Common.Controls;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Core.Common.Controls;

/// <summary>
/// Headless tests for the localized tooltip of the global NumericUpDown scrubber style.
/// </summary>
public class NumericScrubberTooltipTests
{
    /// <summary>
    /// A NumericUpDown shows the localized scrubber hint, not the raw resource key, and an explicit tooltip still wins.
    /// </summary>
    [AvaloniaFact]
    public void NumericUpDown_ScrubberTooltip_IsLocalized()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalizationServices();
        var localization = services.BuildServiceProvider().GetRequiredService<ILocalizationService>();
        var resources = Avalonia.Application.Current!.Resources;
        var hadPrevious = resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        resources[LocalizationConstants.ResourceServiceKey] = localization;
        var styled = new NumericUpDown();
        var custom = new NumericUpDown();
        ToolTip.SetTip(custom, "Custom tip");
        var window = new Window { Content = new StackPanel { Children = { styled, custom } } };
        window.Show();
        try
        {
            Assert.Equal(localization.GetString("Common.Tooltip.NumericScrubber"), ToolTip.GetTip(styled));
            Assert.NotEqual("Common.Tooltip.NumericScrubber", ToolTip.GetTip(styled));
            Assert.Equal("Custom tip", ToolTip.GetTip(custom));
        }
        finally
        {
            window.Close();
            if (hadPrevious)
            {
                resources[LocalizationConstants.ResourceServiceKey] = previous;
            }
            else
            {
                resources.Remove(LocalizationConstants.ResourceServiceKey);
            }
        }
    }

    /// <summary>
    /// A NumericUpDown created before the localization service is available binds the localized hint once it is attached.
    /// </summary>
    [AvaloniaFact]
    public void NumericUpDown_ServiceAddedBeforeAttach_BindsTooltipOnAttach()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalizationServices();
        var localization = services.BuildServiceProvider().GetRequiredService<ILocalizationService>();
        var resources = Avalonia.Application.Current!.Resources;
        var hadPrevious = resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        resources.Remove(LocalizationConstants.ResourceServiceKey);
        var control = new NumericUpDown();
        NumericScrubber.SetEnableScrubber(control, true);
        var window = new Window();
        try
        {
            Assert.Null(ToolTip.GetTip(control));
            resources[LocalizationConstants.ResourceServiceKey] = localization;
            window.Content = control;
            window.Show();

            Assert.Equal(localization.GetString("Common.Tooltip.NumericScrubber"), ToolTip.GetTip(control));
        }
        finally
        {
            window.Close();
            if (hadPrevious)
            {
                resources[LocalizationConstants.ResourceServiceKey] = previous;
            }
            else
            {
                resources.Remove(LocalizationConstants.ResourceServiceKey);
            }
        }
    }
}
