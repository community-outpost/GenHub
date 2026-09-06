using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GenHub.Core.Interfaces.Info;
using GenHub.Features.Info.Services;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Info;

/// <summary>
/// Unit tests for <see cref="DefaultInfoContentProvider"/>.
/// </summary>
public class DefaultInfoContentProviderTests
{
    private readonly Mock<IGeneralsOnlinePatchNotesService> _patchNotesServiceMock = new();
    private readonly DefaultInfoContentProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInfoContentProviderTests"/> class.
    /// </summary>
    public DefaultInfoContentProviderTests()
    {
        _provider = new DefaultInfoContentProvider(_patchNotesServiceMock.Object);
    }

    /// <summary>
    /// Verifies that GetAllSectionsAsync returns all expected info sections.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetAllSectionsAsync_ReturnsOrderedSectionsAsync()
    {
        var sections = (await _provider.GetAllSectionsAsync()).ToList();

        sections.Should().NotBeEmpty();
        sections.Should().Contain(s => s.Id == "workspaces");
        sections.Should().Contain(s => s.Id == "quickstart");
    }

    /// <summary>
    /// Verifies that GetSectionAsync returns the workspace section with comprehensive strategy explanations.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task GetSectionAsync_WorkspaceSection_ContainsComprehensiveStrategyExplanationsAsync()
    {
        var section = await _provider.GetSectionAsync("workspaces");

        section.Should().NotBeNull();
        section!.Title.Should().Be("Virtual Workspaces");
        section.Cards.Should().NotBeEmpty();

        var titles = section.Cards.Select(c => c.Title).ToList();
        titles.Should().Contain("The Magic Mirror");
        titles.Should().Contain("Workspace Strategies Compared");
        titles.Should().Contain("Hardlinks vs Symlinks vs Copies: Deep Dive");
        titles.Should().Contain("Troubleshooting & Permissions");
        titles.Should().Contain("Performance Specs");

        var comparisonCard = section.Cards.First(c => c.Title == "Workspace Strategies Compared");
        comparisonCard.DetailedContent.Should().Contain("HardLink");
        comparisonCard.DetailedContent.Should().Contain("SymlinkOnly");
        comparisonCard.DetailedContent.Should().Contain("HybridCopySymlink");
        comparisonCard.DetailedContent.Should().Contain("FullCopy");

        var deepDiveCard = section.Cards.First(c => c.Title == "Hardlinks vs Symlinks vs Copies: Deep Dive");
        deepDiveCard.DetailedContent.Should().Contain("Hardlink");
        deepDiveCard.DetailedContent.Should().Contain("Symlink");
        deepDiveCard.DetailedContent.Should().Contain("Full Copy");
        deepDiveCard.DetailedContent.Should().Contain("Automatic Fallback");
    }
}
