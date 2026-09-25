using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using GenHub.Features.Downloads.Views;
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
        AssertNoRawMarkupTooltips(new ContentCardView());
    }

    /// <summary>
    /// Verifies that no content detail tooltip shows raw markup extension text.
    /// </summary>
    [AvaloniaFact]
    public void ContentDetailView_Tooltips_AreNotRawMarkup()
    {
        AssertNoRawMarkupTooltips(new ContentDetailView());
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
