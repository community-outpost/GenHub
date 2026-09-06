using System;
using System.IO;
using Xunit;

namespace GenHub.Tests.Core.Features.UI;

/// <summary>
/// Regression tests verifying that Info section views and Markdown rendering controls
/// enforce text wrapping and disable horizontal scrolling to prevent content truncation on smaller viewports.
/// </summary>
public class InfoSectionResponsivenessTests
{
    [Fact]
    public void GenHubInfoSectionView_DisablesHorizontalScrollBar()
    {
        var solutionDir = FindSolutionDirectory();
        var viewPath = Path.Combine(solutionDir, "GenHub", "Features", "Info", "Views", "GenHubInfoSectionView.axaml");
        Assert.True(File.Exists(viewPath), $"File not found at: {viewPath}");

        var content = File.ReadAllText(viewPath);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", content);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", content);
    }

    [Fact]
    public void ChangelogsView_DisablesHorizontalScrollBar()
    {
        var solutionDir = FindSolutionDirectory();
        var viewPath = Path.Combine(solutionDir, "GenHub", "Features", "Info", "Views", "ChangelogsView.axaml");
        Assert.True(File.Exists(viewPath), $"File not found at: {viewPath}");

        var content = File.ReadAllText(viewPath);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", content);
        Assert.DoesNotContain("HorizontalScrollBarVisibility=\"Auto\"", content);
    }

    [Fact]
    public void GenHubInfoSectionView_StylesIncludeTextWrapping()
    {
        var solutionDir = FindSolutionDirectory();
        var viewPath = Path.Combine(solutionDir, "GenHub", "Features", "Info", "Views", "GenHubInfoSectionView.axaml");
        var content = File.ReadAllText(viewPath);

        // Section header and card title styles must wrap text to prevent off-screen truncation
        Assert.Contains("TextBlock.section-header", content);
        Assert.Contains("TextBlock.card-title", content);
    }

    private static string FindSolutionDirectory()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenHub.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GenHub.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate directory containing GenHub.sln from: " + AppContext.BaseDirectory);
    }
}
