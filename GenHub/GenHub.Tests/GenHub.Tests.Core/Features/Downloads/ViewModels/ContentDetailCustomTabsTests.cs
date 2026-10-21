using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Content;
using GenHub.Core.Interfaces.GameProfiles;
using GenHub.Core.Interfaces.Manifest;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Downloads.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads.ViewModels;

/// <summary>
/// Tests loading publisher-defined custom tabs into <see cref="ContentDetailViewModel"/>.
/// </summary>
public sealed class ContentDetailCustomTabsTests
{
    private const string ItemAManifestId = "1.0.tabs-pub.mod.mod-a";
    private const string VariantBManifestId = "1.0.tabs-pub.mod.mod-b";

    /// <summary>
    /// The tabs returned for the selected item populate the custom tab collection.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Initialize_LoadsTabsForSelectedItemAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", Tab("a-notes"), Tab("a-faq"));
        using var viewModel = CreateViewModel(registry);

        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();

        Assert.Equal(["a-notes", "a-faq"], viewModel.CustomTabs.Select(t => t.TabId));
        Assert.True(viewModel.HasCustomTabs);
    }

    /// <summary>
    /// Selecting another variant reloads the tabs for that item, and a late result for a
    /// previously selected item never replaces them.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectedVariantChanged_LateResultForPreviousItem_KeepsCurrentItemTabsAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", Tab("a-notes"));
        using var viewModel = CreateViewModel(registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        Assert.Equal(["a-notes"], viewModel.CustomTabs.Select(t => t.TabId));

        var pendingB = registry.Pending("mod-b");
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = VariantBManifestId, Name = "Mod B" };
        var staleLoad = viewModel.WaitForInitializationAsync();
        Assert.Empty(viewModel.CustomTabs);

        var pendingA = registry.Pending("mod-a");
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = ItemAManifestId, Name = "Mod A" };
        pendingA.SetResult([Tab("a-guide")]);
        await viewModel.WaitForInitializationAsync();
        Assert.Equal(["a-guide"], viewModel.CustomTabs.Select(t => t.TabId));

        pendingB.SetResult([Tab("b-notes")]);
        await staleLoad;

        Assert.Equal(["a-guide"], viewModel.CustomTabs.Select(t => t.TabId));
        Assert.Equal(["mod-a", "mod-b", "mod-a"], registry.RequestedContentIds);
    }

    /// <summary>
    /// A tab load that finishes after the view model is disposed leaves the collection untouched.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Dispose_BeforeTabsLoad_DiscardsLateResultAsync()
    {
        var registry = new ScriptedTabRegistry();
        var pendingA = registry.Pending("mod-a");
        var viewModel = CreateViewModel(registry);

        viewModel.Initialize();
        var initialLoad = viewModel.WaitForInitializationAsync();
        viewModel.Dispose();

        pendingA.SetResult([Tab("a-notes")]);
        await initialLoad;

        Assert.Empty(viewModel.CustomTabs);
    }

    /// <summary>
    /// A selected publisher tab stays selected by ID after a reload, and selection falls back to the
    /// first tab when the new item has no tab with that ID.
    /// </summary>
    /// <param name="newItemKeepsTab">Whether the newly selected item has a tab with the same ID.</param>
    /// <param name="expectedIndex">The expected selected tab index after the reload.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true, ContentDetailViewModel.BuiltInTabCount)]
    [InlineData(false, 0)]
    public async Task SelectedVariantChanged_RestoresSelectedTabByIdAsync(bool newItemKeepsTab, int expectedIndex)
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", Tab("a-notes"), Tab("shared"));
        if (newItemKeepsTab)
        {
            registry.Complete("mod-b", Tab("shared"), Tab("b-notes"));
        }
        else
        {
            registry.Complete("mod-b", Tab("b-notes"));
        }

        using var viewModel = CreateViewModel(registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        Assert.Equal(["mod-a"], registry.RequestedContentIds);
        viewModel.SelectedTabIndex = ContentDetailViewModel.BuiltInTabCount + 1;

        viewModel.SelectedVariant = new InstallableVariant { ManifestId = VariantBManifestId, Name = "Mod B" };
        await viewModel.WaitForInitializationAsync();

        Assert.Equal(expectedIndex, viewModel.SelectedTabIndex);
    }

    /// <summary>
    /// Switching items twice while a tab load is pending still restores the tab the user had selected.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task SelectedVariantChangedTwiceWhileLoading_RestoresOriginalSelectedTabAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", Tab("a-notes"), Tab("shared"));
        using var viewModel = CreateViewModel(registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        viewModel.SelectedTabIndex = ContentDetailViewModel.BuiltInTabCount + 1;

        registry.Pending("mod-b");
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = VariantBManifestId, Name = "Mod B" };
        Assert.Equal(0, viewModel.SelectedTabIndex);

        var pendingA = registry.Pending("mod-a");
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = ItemAManifestId, Name = "Mod A" };
        pendingA.SetResult([Tab("shared"), Tab("a-notes")]);
        await viewModel.WaitForInitializationAsync();

        Assert.Equal(ContentDetailViewModel.BuiltInTabCount, viewModel.SelectedTabIndex);
    }

    /// <summary>
    /// Choosing a built-in tab while a tab load is pending cancels restoring the previously selected publisher tab.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task BuiltInTabSelectedWhileLoading_DoesNotRestorePublisherTabAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", Tab("a-notes"), Tab("shared"));
        using var viewModel = CreateViewModel(registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        viewModel.SelectedTabIndex = ContentDetailViewModel.BuiltInTabCount + 1;

        var pendingB = registry.Pending("mod-b");
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = VariantBManifestId, Name = "Mod B" };
        viewModel.SelectedTabIndex = 2;
        viewModel.SelectedTabIndex = 0;
        pendingB.SetResult([Tab("shared")]);
        await viewModel.WaitForInitializationAsync();

        Assert.Equal(0, viewModel.SelectedTabIndex);
    }

    /// <summary>
    /// A failed tab load for the current item drops the pending tab ID, so a later item does not restore it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FailedLoadWhilePending_DoesNotRestoreTabOnLaterItemAsync()
    {
        var registry = new ScriptedTabRegistry();
        registry.Complete("mod-a", Tab("a-notes"), Tab("shared"));
        using var viewModel = CreateViewModel(registry);
        viewModel.Initialize();
        await viewModel.WaitForInitializationAsync();
        viewModel.SelectedTabIndex = ContentDetailViewModel.BuiltInTabCount + 1;

        registry.Pending("mod-b").SetException(new InvalidOperationException("registry failure"));
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = VariantBManifestId, Name = "Mod B" };
        await viewModel.WaitForInitializationAsync();

        registry.Complete("mod-a", Tab("shared"));
        viewModel.SelectedVariant = new InstallableVariant { ManifestId = ItemAManifestId, Name = "Mod A" };
        await viewModel.WaitForInitializationAsync();

        Assert.Equal(["shared"], viewModel.CustomTabs.Select(t => t.TabId));
        Assert.Equal(0, viewModel.SelectedTabIndex);
    }

    private static CustomTabDefinition Tab(string tabId) => new() { TabId = tabId, Header = tabId };

    private static ContentDetailViewModel CreateViewModel(ITabProviderRegistry registry)
    {
        var itemA = CreateSearchResult("mod-a");
        var variants = new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
        {
            [ItemAManifestId] = CreateSearchResult("mod-a"),
            [VariantBManifestId] = CreateSearchResult("mod-b"),
        };

        var stateService = new Mock<IContentStateService>();
        stateService
            .Setup(s => s.GetStateByManifestIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentState.NotDownloaded);
        stateService
            .Setup(s => s.GetLocalManifestIdAsync(It.IsAny<ContentSearchResult>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ContentSearchResult result, CancellationToken _) => result.Id);

        return new ContentDetailViewModel(
            itemA,
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

    private sealed class ScriptedTabRegistry : ITabProviderRegistry
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource<IReadOnlyList<CustomTabDefinition>>> _responses = new();
        private readonly List<string> _requested = [];

        public IReadOnlyList<string> RequestedContentIds
        {
            get
            {
                lock (_requested)
                {
                    return [.. _requested];
                }
            }
        }

        public TaskCompletionSource<IReadOnlyList<CustomTabDefinition>> Pending(string contentId)
        {
            var source = new TaskCompletionSource<IReadOnlyList<CustomTabDefinition>>(TaskCreationOptions.RunContinuationsAsynchronously);
            _responses[contentId] = source;
            return source;
        }

        public void Complete(string contentId, params CustomTabDefinition[] tabs) => Pending(contentId).SetResult(tabs);

        public void RegisterProvider(ITabProvider provider) => throw new NotSupportedException();

        public bool UnregisterProvider(string providerId) => throw new NotSupportedException();

        public IReadOnlyList<ITabProvider> GetAllProviders() => [];

        public Task<IReadOnlyList<CustomTabDefinition>> GetTabsForContentAsync(
            ContentSearchResult searchResult,
            CancellationToken cancellationToken = default)
        {
            var contentId = searchResult.ResolverMetadata[CatalogConstants.CatalogContentIdMetadataKey];
            lock (_requested)
            {
                _requested.Add(contentId);
            }

            return _responses[contentId].Task;
        }
    }
}
