using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace GenHub.Tests.Core.Features.UI;

/// <summary>
/// Regression tests verifying that Info section views and Markdown rendering controls
/// enforce text wrapping and disable horizontal scrolling to prevent content truncation on smaller viewports.
/// </summary>
public partial class InfoSectionResponsivenessTests
{
    [GeneratedRegex(@"\bHorizontalScrollBarVisibility\s*=\s*[""']Auto[""']", RegexOptions.IgnoreCase)]
    private static partial Regex AutoScrollBarRegex();

    [GeneratedRegex(@"\bHorizontalScrollBarVisibility\s*=\s*[""']Disabled[""']", RegexOptions.IgnoreCase)]
    private static partial Regex DisabledScrollBarRegex();

    [GeneratedRegex(@"<Style\s+Selector\s*=\s*""TextBlock\.section-header"">(?:(?!</Style>)[\s\S])*?<Setter\s+Property\s*=\s*""TextWrapping""\s+Value\s*=\s*""Wrap""\s*/>", RegexOptions.IgnoreCase)]
    private static partial Regex SectionHeaderWrappingRegex();

    [GeneratedRegex(@"<Style\s+Selector\s*=\s*""TextBlock\.card-title"">(?:(?!</Style>)[\s\S])*?<Setter\s+Property\s*=\s*""TextWrapping""\s+Value\s*=\s*""Wrap""\s*/>", RegexOptions.IgnoreCase)]
    private static partial Regex CardTitleWrappingRegex();

    /// <summary>
    /// Verifies that <c>GenHubInfoSectionView</c> disables horizontal scroll bars and does not allow Auto horizontal scrolling.
    /// </summary>
    [Fact]
    public void GenHubInfoSectionView_DisablesHorizontalScrollBar()
    {
        var solutionDir = UiTestPathHelper.FindSolutionDirectory();
        var viewPath = Path.Combine(solutionDir, "GenHub", "Features", "Info", "Views", "GenHubInfoSectionView.axaml");
        Assert.True(File.Exists(viewPath), $"File not found at: {viewPath}");

        var content = File.ReadAllText(viewPath);
        Assert.Matches(DisabledScrollBarRegex(), content);
        Assert.DoesNotMatch(AutoScrollBarRegex(), content);
    }

    /// <summary>
    /// Verifies that <c>ChangelogsView</c> disables horizontal scroll bars and does not allow Auto horizontal scrolling.
    /// </summary>
    [Fact]
    public void ChangelogsView_DisablesHorizontalScrollBar()
    {
        var solutionDir = UiTestPathHelper.FindSolutionDirectory();
        var viewPath = Path.Combine(solutionDir, "GenHub", "Features", "Info", "Views", "ChangelogsView.axaml");
        Assert.True(File.Exists(viewPath), $"File not found at: {viewPath}");

        var content = File.ReadAllText(viewPath);
        Assert.Matches(DisabledScrollBarRegex(), content);
        Assert.DoesNotMatch(AutoScrollBarRegex(), content);
    }

    /// <summary>
    /// Verifies that <c>GenHubInfoSectionView</c> styles explicitly define <c>TextWrapping="Wrap"</c>
    /// on section headers and card titles to prevent off-screen truncation.
    /// </summary>
    [Fact]
    public void GenHubInfoSectionView_StylesIncludeTextWrapping()
    {
        var solutionDir = UiTestPathHelper.FindSolutionDirectory();
        var viewPath = Path.Combine(solutionDir, "GenHub", "Features", "Info", "Views", "GenHubInfoSectionView.axaml");
        var content = File.ReadAllText(viewPath);

        // Section header and card title styles must explicitly set TextWrapping="Wrap" within the style block
        Assert.Matches(SectionHeaderWrappingRegex(), content);
        Assert.Matches(CardTitleWrappingRegex(), content);
    }
}
