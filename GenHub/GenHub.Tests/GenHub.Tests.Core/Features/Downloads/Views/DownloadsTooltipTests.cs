using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Features.Downloads.Views;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Views;

/// <summary>
/// Headless tests for the tooltips on the Downloads card and detail views.
/// </summary>
public class DownloadsTooltipTests
{
    /// <summary>
    /// Verifies that no content card tooltip shows raw markup extension text.
    /// </summary>
    [AvaloniaFact]
    public void ContentCardView_Tooltips_AreNotRawMarkup()
    {
        AssertTooltips(() => new ContentCardView(), "DownloadButton", "UpdateButton", "AddToProfileButton");
    }

    /// <summary>
    /// Verifies that no content detail tooltip shows raw markup extension text.
    /// </summary>
    [AvaloniaFact]
    public void ContentDetailView_Tooltips_AreNotRawMarkup()
    {
        AssertTooltips(() => new ContentDetailView(), "UpdateButton", "AddToProfileButton", "DeleteDownloadButton", "OpenWebsiteButton");
    }

    private static void AssertTooltips(Func<Control> createView, params string[] buttonNames)
    {
        var resources = Avalonia.Application.Current!.Resources;
        var hadPrevious = resources.TryGetValue(LocalizationConstants.ResourceServiceKey, out var previous);
        var localization = new Mock<ILocalizationService>();
        localization.Setup(service => service[It.IsAny<string>()]).Returns((string key) => $"Translated: {key}");
        resources[LocalizationConstants.ResourceServiceKey] = localization.Object;
        try
        {
            var view = createView();
            foreach (var buttonName in buttonNames)
            {
                var button = view.FindControl<Button>(buttonName);
                Assert.NotNull(button);
                var tooltip = Assert.IsType<string>(ToolTip.GetTip(button));
                Assert.StartsWith("Translated: Downloads.", tooltip);
            }

            AssertNoRawMarkupTooltips(view);
        }
        finally
        {
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

    private static void AssertNoRawMarkupTooltips(Control view)
    {
        var rawTips = view.GetLogicalDescendants()
            .OfType<Control>()
            .Select(ToolTip.GetTip)
            .OfType<string>()
            .Where(tip => tip.TrimStart().StartsWith('{'))
            .ToList();

        Assert.Empty(rawTips);
    }
}
