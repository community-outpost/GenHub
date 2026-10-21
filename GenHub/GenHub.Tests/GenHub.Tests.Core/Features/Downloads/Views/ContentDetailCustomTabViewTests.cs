using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using GenHub.Core.Constants;
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
        var window = ShowInWindow(view);
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
        window.Close();
    }

    /// <summary>
    /// A detached view stops listening to the view model's tabs and picks them up again when it is
    /// attached, without resetting the selected publisher tab when nothing changed.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentDetailView_DetachAndReattach_UnsubscribesAndRestoresTabsAsync()
    {
        using var viewModel = await CreateLoadedViewModelAsync("mod-a", "Install guide", "FAQ");
        var view = new ContentDetailView { DataContext = viewModel };
        var window = ShowInWindow(view);
        var tabControl = view.FindControl<TabControl>("DetailTabs")!;
        tabControl.SelectedIndex = ContentDetailViewModel.BuiltInTabCount + 1;

        window.Content = null;
        window.Content = view;
        Assert.Equal(ContentDetailViewModel.BuiltInTabCount + 1, viewModel.SelectedTabIndex);
        Assert.Equal(ContentDetailViewModel.BuiltInTabCount + 1, tabControl.SelectedIndex);

        window.Content = null;
        viewModel.CustomTabs.Add(new CustomTabDefinition { TabId = "changelog", Header = "Changelog" });
        Assert.Equal(["Install guide", "FAQ"], GeneratedHeaders(tabControl));

        window.Content = view;
        Assert.Equal(["Install guide", "FAQ", "Changelog"], GeneratedHeaders(tabControl));

        viewModel.CustomTabs.RemoveAt(0);
        Assert.Equal(["FAQ", "Changelog"], GeneratedHeaders(tabControl));
        window.Close();
    }

    /// <summary>
    /// With the tab control bound two-way, a selected publisher tab survives two variant switches
    /// while the tab load is pending, and a later user tab change reaches the view model.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentDetailView_VariantSwitchesWhileLoading_RestoresSelectedPublisherTabAsync()
    {
        const string itemAId = "1.0.tabs-pub.mod.mod-a";
        const string itemBId = "1.0.tabs-pub.mod.mod-b";
        var responses = new Dictionary<string, TaskCompletionSource<IReadOnlyList<CustomTabDefinition>>>();
        TaskCompletionSource<IReadOnlyList<CustomTabDefinition>> Respond(string contentId)
        {
            var source = new TaskCompletionSource<IReadOnlyList<CustomTabDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
            responses[contentId] = source;
            return source;
        }

        var registry = new Mock<ITabProviderRegistry>();
        registry
            .Setup(r => r.GetTabsForContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .Returns((ContentSearchResult result, CancellationToken _) => responses[result.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey]].Task);
        Respond("mod-a").SetResult([Tab("a-notes"), Tab("shared")]);
        using var viewModel = CreateViewModel(
            CreateSearchResult("mod-a"),
            registry.Object,
            new Dictionary<string, ContentSearchResult>
            {
                [itemAId] = CreateSearchResult("mod-a"),
                [itemBId] = CreateSearchResult("mod-b"),
            });
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        var view = new ContentDetailView { DataContext = viewModel };
        var window = ShowInWindow(view);
        var tabControl = view.FindControl<TabControl>("DetailTabs")!;
        tabControl.SelectedIndex = ContentDetailViewModel.BuiltInTabCount + 1;
        Assert.Equal(ContentDetailViewModel.BuiltInTabCount + 1, viewModel.SelectedTabIndex);

        var pendingB = Respond("mod-b");
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = itemBId, Name = "Mod B" };
        var abandonedLoad = viewModel.WaitForInitializationAsync();
        Assert.Equal(0, tabControl.SelectedIndex);
        var reloadA = Respond("mod-a");
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = itemAId, Name = "Mod A" };
        reloadA.SetResult([Tab("shared"), Tab("a-guide")]);
        await viewModel.WaitForInitializationAsync();

        Assert.Equal(ContentDetailViewModel.BuiltInTabCount, viewModel.SelectedTabIndex);
        Assert.Equal(ContentDetailViewModel.BuiltInTabCount, tabControl.SelectedIndex);

        tabControl.SelectedIndex = 0;
        Assert.Equal(0, viewModel.SelectedTabIndex);
        pendingB.SetResult([Tab("b-notes")]);
        await abandonedLoad;
        Assert.Equal(["shared", "a-guide"], viewModel.CustomTabs.Select(t => t.TabId));
        window.Close();
    }

    private static Window ShowInWindow(Control view)
    {
        var window = new Window { Content = view };
        window.Show();
        return window;
    }

    private static List<string?> GeneratedHeaders(TabControl tabControl)
    {
        return [.. tabControl.Items
            .Skip(ContentDetailViewModel.BuiltInTabCount)
            .Cast<TabItem>()
            .Select(item => item.Header as string)];
    }

    private static CustomTabDefinition Tab(string tabId) => new() { TabId = tabId, Header = tabId };

    private static ContentSearchResult CreateSearchResult(string contentId)
    {
        var result = new ContentSearchResult
        {
            Id = $"1.0.tabs-pub.mod.{contentId}",
            Name = contentId,
            ProviderName = "Tabs Publisher",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
        result.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey] = contentId;
        return result;
    }

    private static async Task<ContentDetailViewModel> CreateLoadedViewModelAsync(string contentId, params string[] headers)
    {
        IReadOnlyList<CustomTabDefinition> tabs = [.. headers.Select(header => new CustomTabDefinition { TabId = header.ToLowerInvariant(), Header = header })];
        var registry = new Mock<ITabProviderRegistry>();
        registry
            .Setup(r => r.GetTabsForContentAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tabs);
        var viewModel = CreateViewModel(CreateSearchResult(contentId), registry.Object);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        return viewModel;
    }

    private static ContentDetailViewModel CreateViewModel(
        ContentSearchResult searchResult,
        ITabProviderRegistry registry,
        IReadOnlyDictionary<string, ContentSearchResult>? variants = null)
    {
        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult result, CancellationToken _) => result.Id);

        return new ContentDetailViewModel(
            searchResult,
            [],
            new Mock<IProfileContentService>().Object,
            new Mock<IGameProfileManager>().Object,
            new Mock<INotificationService>().Object,
            registry,
            stateService.Object,
            new Mock<IContentDownloadCoordinator>().Object,
            new Mock<IContentManifestPool>().Object,
            new Mock<ILoggerFactory>().Object,
            new Mock<ILogger<ContentDetailViewModel>>().Object,
            variantSearchResults: variants);
    }
}
