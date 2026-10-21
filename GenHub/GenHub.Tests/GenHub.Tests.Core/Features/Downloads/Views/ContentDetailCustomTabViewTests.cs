using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Downloads.Views;
using Microsoft.Extensions.Logging;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.Views;

/// <summary>
/// Headless tests for the publisher-defined tabs generated in <see cref="ContentDetailView"/>.
/// </summary>
public class ContentDetailCustomTabViewTests
{
    /// <summary>
    /// The view declares exactly the built-in tabs the view model maps custom tab indices from.
    /// </summary>
    [AvaloniaFact]
    public void ContentDetailView_WithoutDataContext_HasOnlyBuiltInTabs()
    {
        var view = new ContentDetailView();

        var tabs = view.FindControl<TabControl>("DetailTabs");

        Assert.NotNull(tabs);
        Assert.Equal(ContentDetailViewModel.BuiltInTabCount, tabs.Items.Count);
    }

    /// <summary>
    /// Each publisher tab becomes a top-level tab after the built-in tabs, and switching to another
    /// item replaces them instead of keeping the previous item's tabs.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentDetailView_SwitchingItems_ReplacesGeneratedTabsAsync()
    {
        var view = new ContentDetailView();
        var tabControl = view.FindControl<TabControl>("DetailTabs")!;
        using var itemA = await CreateLoadedViewModelAsync("mod-a", "Install guide", "FAQ");
        using var itemB = await CreateLoadedViewModelAsync("mod-b", "Changelog");

        view.DataContext = itemA;
        Assert.Equal(["Install guide", "FAQ"], GeneratedHeaders(tabControl));
        var content = Assert.IsType<CustomTabContentView>(((TabItem)tabControl.Items[ContentDetailViewModel.BuiltInTabCount]!).Content);
        Assert.Same(itemA.CustomTabs[0], content.DataContext);
        Assert.NotNull(content.ImageCommand);
        Assert.True(content.ImageCommand.CanExecute(new CustomTabCardDefinition { Title = "Remote", ImageUrl = "https://example.com/card.png" }));
        Assert.False(content.ImageCommand.CanExecute(new CustomTabCardDefinition { Title = "Bundled", ImageUrl = "avares://GenHub/Assets/Covers/usa-cover.jpg" }));

        view.DataContext = itemB;
        Assert.Equal(["Changelog"], GeneratedHeaders(tabControl));

        itemA.CustomTabs.Clear();
        Assert.Equal(["Changelog"], GeneratedHeaders(tabControl));

        view.DataContext = null;
        Assert.Empty(GeneratedHeaders(tabControl));
    }

    private static List<string?> GeneratedHeaders(TabControl tabControl)
    {
        return [.. tabControl.Items
            .Skip(ContentDetailViewModel.BuiltInTabCount)
            .Cast<TabItem>()
            .Select(item => item.Header as string)];
    }

    private static async Task<ContentDetailViewModel> CreateLoadedViewModelAsync(string contentId, params string[] headers)
    {
        var searchResult = new ContentSearchResult
        {
            Id = $"1.0.tabs-pub.mod.{contentId}",
            Name = contentId,
            ProviderName = "Tabs Publisher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        IReadOnlyList<CustomTabDefinition> tabs = [.. headers.Select(header => new CustomTabDefinition { TabId = header.ToLowerInvariant(), Header = header })];
        var registry = new Mock<ITabProviderRegistry>();
        registry
            .Setup(r => r.GetTabsForContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tabs);
        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult result, CancellationToken _) => result.Id);

        var viewModel = new ContentDetailViewModel(
            searchResult,
            [],
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            registry.Object,
            stateService.Object,
            new Mock<IContentDownloadCoordinator>().Object,
            new Mock<IContentManifestPool>().Object,
            new Mock<ILoggerFactory>().Object,
            new Mock<ILogger<ContentDetailViewModel>>().Object);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        return viewModel;
    }
}
