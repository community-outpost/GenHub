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
using System.Collections.Generic;
using System.Threading;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Downloads;

/// <summary>
/// Builds content detail view models and catalog search results for publisher custom tab tests.
/// </summary>
internal static class ContentDetailTabTestHarness
{
    /// <summary>The manifest ID of the first test item.</summary>
    public const string ItemAManifestId = "1.0.tabs-pub.mod.mod-a";

    /// <summary>The manifest ID of the second test item, offered as a variant of the first.</summary>
    public const string ItemBManifestId = "1.0.tabs-pub.mod.mod-b";

    /// <summary>Creates a runtime tab whose header matches its ID.</summary>
    /// <param name="tabId">The tab ID.</param>
    /// <returns>The tab definition.</returns>
    public static CustomTabDefinition Tab(string tabId) => new() { TabId = tabId, Header = tabId };

    /// <summary>Creates a catalog search result for a content ID.</summary>
    /// <param name="contentId">The catalog content ID.</param>
    /// <returns>The search result.</returns>
    public static ContentSearchResult CreateSearchResult(string contentId)
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

    /// <summary>Creates a view model for item A that can switch to item B as a variant.</summary>
    /// <param name="registry">The tab provider registry.</param>
    /// <returns>The view model.</returns>
    public static ContentDetailViewModel CreateTwoItemViewModel(ITabProviderRegistry registry)
    {
        return CreateViewModel(
            CreateSearchResult("mod-a"),
            registry,
            new Dictionary<string, ContentSearchResult>(StringComparer.OrdinalIgnoreCase)
            {
                [ItemAManifestId] = CreateSearchResult("mod-a"),
                [ItemBManifestId] = CreateSearchResult("mod-b"),
            });
    }

    /// <summary>Creates a content detail view model with mocked services.</summary>
    /// <param name="searchResult">The selected search result.</param>
    /// <param name="registry">The tab provider registry.</param>
    /// <param name="variants">Optional variant search results keyed by manifest ID.</param>
    /// <returns>The view model.</returns>
    public static ContentDetailViewModel CreateViewModel(
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
