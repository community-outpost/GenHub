using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GenHub.Core.Models.Content;
using GenHub.Features.Downloads.ViewModels;
using GenHub.Features.Downloads.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using static GenHub.Tests.Core.Features.Downloads.ContentDetailTabTestHarness;

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
        using var itemA = await CreateLoadedViewModelAsync("mod-a", "Install guide", "FAQ");
        using var itemB = await CreateLoadedViewModelAsync("mod-b", "Changelog");
        var view = new ContentDetailView();
        await InWindowAsync(view, tabControl =>
        {
            view.DataContext = itemA;
            Assert.Equal(["Install guide", "FAQ"], GeneratedHeaders(tabControl));
            var scroller = Assert.IsType<ScrollViewer>(((TabItem)tabControl.Items[ContentDetailViewModel.BuiltInTabCount]!).Content);
            var content = Assert.IsType<CustomTabContentView>(scroller.Content);
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
            return Task.CompletedTask;
        });
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
        await InWindowAsync(view, tabControl =>
        {
            var window = (Window)view.Parent!;
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
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// With the tab control bound two-way, a selected publisher tab survives two variant switches
    /// while the tab load is pending, and a later user tab change reaches the view model.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentDetailView_VariantSwitchesWhileLoading_RestoresSelectedPublisherTabAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", Tab("a-notes"), Tab("shared"));
        using var viewModel = CreateTwoItemViewModel(registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        var view = new ContentDetailView { DataContext = viewModel };
        await InWindowAsync(view, async tabControl =>
        {
            tabControl.SelectedIndex = ContentDetailViewModel.BuiltInTabCount + 1;
            Assert.Equal(ContentDetailViewModel.BuiltInTabCount + 1, viewModel.SelectedTabIndex);

            var pendingB = registry.Pending("mod-b");
            viewModel.SelectedVariant = new InstallableVariant { ManifestId = ItemBManifestId, Name = "Mod B" };
            var abandonedLoad = viewModel.WaitForInitializationAsync();
            Assert.Equal(0, tabControl.SelectedIndex);
            var reloadA = registry.Pending("mod-a");
            viewModel.SelectedVariant = new InstallableVariant { ManifestId = ItemAManifestId, Name = "Mod A" };
            reloadA.SetResult([Tab("shared"), Tab("a-guide")]);
            await viewModel.WaitForInitializationAsync();

            Assert.Equal(ContentDetailViewModel.BuiltInTabCount, viewModel.SelectedTabIndex);
            Assert.Equal(ContentDetailViewModel.BuiltInTabCount, tabControl.SelectedIndex);

            tabControl.SelectedIndex = 0;
            Assert.Equal(0, viewModel.SelectedTabIndex);
            pendingB.SetResult([Tab("b-notes")]);
            await abandonedLoad;
            Assert.Equal(["shared", "a-guide"], viewModel.CustomTabs.Select(t => t.TabId));
        });
    }

    /// <summary>
    /// Long publisher tab content scrolls through the detail view's own scroller around the tab content.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentDetailView_LongPublisherTab_ScrollsInTabScrollerAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", new CustomTabDefinition
        {
            TabId = "long",
            Header = "Long",
            Intro = string.Join(" ", Enumerable.Repeat("Long intro text.", 400)),
            Cards = [.. Enumerable.Range(0, 24).Select(i => new CustomTabCardDefinition { Title = $"Card {i}", Description = "Card description" })],
        });
        using var viewModel = CreateViewModel(CreateSearchResult("mod-a"), registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        var view = new ContentDetailView { DataContext = viewModel };
        await InWindowAsync(view, tabControl =>
        {
            tabControl.SelectedIndex = ContentDetailViewModel.BuiltInTabCount;
            var window = (Window)view.Parent!;
            window.Width = 900;
            window.Height = 600;
            window.UpdateLayout();

            var scroller = Assert.IsType<ScrollViewer>(((TabItem)tabControl.Items[ContentDetailViewModel.BuiltInTabCount]!).Content);
            var ownScrollers = Assert.IsType<CustomTabContentView>(scroller.Content)
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Where(viewer => viewer.FindAncestorOfType<Markdown.Avalonia.MarkdownScrollViewer>() == null);
            Assert.Empty(ownScrollers);
            Assert.True(scroller.Extent.Height > scroller.Viewport.Height, $"Extent {scroller.Extent.Height} should exceed viewport {scroller.Viewport.Height}");

            scroller.Offset = new Vector(0, 500);
            window.UpdateLayout();
            Assert.Equal(500, scroller.Offset.Y);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The mouse wheel over a publisher tab's Markdown body scrolls the tab, instead of stopping at the Markdown viewer.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentDetailView_WheelOverMarkdownBody_ScrollsTheTabAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", new CustomTabDefinition
        {
            TabId = "guide",
            Header = "Guide",
            Body = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Paragraph {i} of the publisher guide.")),
        });
        using var viewModel = CreateViewModel(CreateSearchResult("mod-a"), registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        var view = new ContentDetailView { DataContext = viewModel };
        await InWindowAsync(view, tabControl =>
        {
            var window = (Window)view.Parent!;
            window.Width = 1000;
            window.Height = 700;
            tabControl.SelectedIndex = ContentDetailViewModel.BuiltInTabCount;
            window.UpdateLayout();

            var scroller = Assert.IsType<ScrollViewer>(((TabItem)tabControl.Items[ContentDetailViewModel.BuiltInTabCount]!).Content);
            AssertWheelScrolls(window, scroller);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// The mouse wheel over the Summary description scrolls the Summary tab.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task ContentDetailView_WheelOverSummaryDescription_ScrollsTheSummaryAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a");
        var searchResult = CreateSearchResult("mod-a");
        searchResult.Description = string.Join("\n\n", Enumerable.Range(1, 80).Select(i => $"Description paragraph {i}."));
        using var viewModel = CreateViewModel(searchResult, registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        var view = new ContentDetailView { DataContext = viewModel };
        await InWindowAsync(view, tabControl =>
        {
            var window = (Window)view.Parent!;
            window.Width = 1000;
            window.Height = 700;
            tabControl.SelectedIndex = 0;
            window.UpdateLayout();

            var scroller = Assert.IsType<ScrollViewer>(((TabItem)tabControl.Items[0]!).Content);
            AssertWheelScrolls(window, scroller);
            return Task.CompletedTask;
        });
    }

    private static void AssertWheelScrolls(Window window, ScrollViewer scroller)
    {
        var markdown = scroller.GetVisualDescendants().OfType<Markdown.Avalonia.MarkdownScrollViewer>().First(m => m.IsEffectivelyVisible);

        // Fractional layout on high-DPI screens can leave the inner viewer a sliver taller than its viewport, so it must
        // not scroll vertically at all or it takes the wheel.
        Assert.All(
            markdown.GetVisualDescendants().OfType<ScrollViewer>(),
            inner => Assert.Equal(Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled, inner.VerticalScrollBarVisibility));
        var target = markdown.TranslatePoint(new Point(40, Math.Min(40, markdown.Bounds.Height / 2)), window)!.Value;
        Assert.Equal(0, scroller.Offset.Y);

        window.MouseWheel(target, new Vector(0, -3));
        window.UpdateLayout();

        Assert.True(scroller.Offset.Y > 0, "The wheel over the Markdown viewer should scroll the outer scroller.");
    }

    private static async Task InWindowAsync(ContentDetailView view, Func<TabControl, Task> body)
    {
        var window = new Window { Content = view };
        window.Show();
        try
        {
            await body(view.FindControl<TabControl>("DetailTabs")!);
        }
        finally
        {
            window.Close();
        }
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
        var registry = new ScriptedTabRegistry();
        registry.Complete(contentId, [.. headers.Select(header => new CustomTabDefinition { TabId = header.ToLowerInvariant(), Header = header })]);
        var viewModel = CreateViewModel(CreateSearchResult(contentId), registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        return viewModel;
    }
}
