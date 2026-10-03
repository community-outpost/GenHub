using Avalonia.Headless.XUnit;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Tests that the upstream release preview in <see cref="ContentLibraryViewModel"/>
/// survives same-item selection refreshes instead of re-fetching on every edit.
/// </summary>
public sealed class ContentLibraryUpstreamPreviewTests
{
    /// <summary>
    /// Verifies that refreshing the selected content does not restart the upstream
    /// preview load or clear already loaded preview releases.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task RefreshContentDisplay_SameItem_DoesNotReloadUpstreamPreviewAsync()
    {
        var ingestionMock = new Mock<ICatalogUpstreamIngestionService>();
        ingestionMock
            .Setup(s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherCatalog, CancellationToken>((catalog, _) =>
                catalog.Content[0].Releases.Add(new ContentRelease { Version = "9.9.9" }))
            .Returns(Task.CompletedTask);

        var item = CreateUpstreamItem("item-a");
        var viewModel = CreateViewModel(item, ingestionMock.Object);
        viewModel.SelectedContent = item;

        await WaitForPreviewAsync(viewModel);
        Assert.Equal("9.9.9", Assert.Single(viewModel.UpstreamPreviewReleases).Version);

        viewModel.RefreshContentDisplay();

        // Give any (unexpected) reload a chance to clear the preview before asserting.
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.Equal("9.9.9", Assert.Single(viewModel.UpstreamPreviewReleases).Version);
        ingestionMock.Verify(
            s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that selecting a different item still reloads the upstream preview.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task SelectDifferentItem_ReloadsUpstreamPreviewAsync()
    {
        var ingestionMock = new Mock<ICatalogUpstreamIngestionService>();
        ingestionMock
            .Setup(s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var first = CreateUpstreamItem("item-a");
        var second = CreateUpstreamItem("item-b");
        var viewModel = CreateViewModel(first, ingestionMock.Object, second);
        viewModel.SelectedContent = first;
        await WaitForIngestionAsync(ingestionMock, 1);

        viewModel.SelectedContent = second;
        await WaitForIngestionAsync(ingestionMock, 2);

        ingestionMock.Verify(
            s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    /// <summary>
    /// Verifies that an upstream load producing no new releases surfaces a failure
    /// warning and keeps the static fallback list visible instead of showing static
    /// leftovers as live data.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task PreviewMiss_SetsFailedWarningAndShowsStaticAsync()
    {
        var ingestionMock = new Mock<ICatalogUpstreamIngestionService>();
        ingestionMock
            .Setup(s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var item = CreateUpstreamItem("item-miss");
        item.Releases.Add(new ContentRelease { Version = "1.0.0", IsLatest = true });
        var viewModel = CreateViewModel(item, ingestionMock.Object);
        viewModel.SelectedContent = item;
        await WaitForPreviewSettledAsync(viewModel, ingestionMock);

        Assert.True(viewModel.UpstreamPreviewFailed);
        Assert.True(viewModel.ShowUpstreamSyncFailedWarning);
        Assert.False(viewModel.HasUpstreamPreview);
        Assert.True(viewModel.ShowStaticReleasesList);
        Assert.False(viewModel.ShowFallbackReleasesNote);
    }

    /// <summary>
    /// Verifies that a successful upstream load clears the failure warning and hands
    /// the main list over to the live releases.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task PreviewSuccess_ClearsFailedAndHidesStaticAsync()
    {
        var ingestionMock = new Mock<ICatalogUpstreamIngestionService>();
        ingestionMock
            .Setup(s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherCatalog, CancellationToken>((catalog, _) =>
                catalog.Content[0].Releases.Add(new ContentRelease { Version = "9.9.9" }))
            .Returns(Task.CompletedTask);

        var item = CreateUpstreamItem("item-live");
        item.Releases.Add(new ContentRelease { Version = "1.0.0", IsLatest = true });
        var viewModel = CreateViewModel(item, ingestionMock.Object);
        viewModel.SelectedContent = item;

        await WaitForPreviewAsync(viewModel);

        Assert.False(viewModel.UpstreamPreviewFailed);
        Assert.False(viewModel.ShowUpstreamSyncFailedWarning);
        Assert.True(viewModel.HasUpstreamPreview);
        Assert.False(viewModel.ShowStaticReleasesList);
        Assert.True(viewModel.ShowFallbackReleasesNote);
        Assert.True(viewModel.ShowManualFallbackReleases);
    }

    /// <summary>
    /// Verifies that selecting an upstream-tracked item hides the manual release
    /// entry points, since releases arrive from the provider.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task SelectUpstreamItem_HidesManualReleaseEntryPointsAsync()
    {
        var ingestionMock = new Mock<ICatalogUpstreamIngestionService>();
        ingestionMock
            .Setup(s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var item = CreateUpstreamItem("item-tracked");
        var viewModel = CreateViewModel(item, ingestionMock.Object);
        viewModel.SelectedContent = item;
        await WaitForPreviewSettledAsync(viewModel, ingestionMock);

        Assert.True(viewModel.SelectedContentTracksUpstream);
        Assert.False(viewModel.CanAddManualRelease);
        Assert.False(viewModel.ShowReleasesDropZone);
    }

    /// <summary>
    /// Verifies that static fallback releases do not re-enable manual release
    /// entry points on upstream-tracked items.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task SelectUpstreamItem_WithStaticReleases_HidesManualReleaseEntryPointsAsync()
    {
        var ingestionMock = new Mock<ICatalogUpstreamIngestionService>();
        ingestionMock
            .Setup(s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var item = CreateUpstreamItem("item-fallback");
        item.Releases.Add(new ContentRelease { Version = "1.0.0", IsLatest = true });
        var viewModel = CreateViewModel(item, ingestionMock.Object);
        viewModel.SelectedContent = item;
        await WaitForPreviewSettledAsync(viewModel, ingestionMock);

        Assert.False(viewModel.CanAddManualRelease);
        Assert.False(viewModel.ShowReleasesDropZone);
    }

    /// <summary>
    /// Verifies that a plain content item keeps the manual release entry points,
    /// including the drop zone while it has no releases.
    /// </summary>
    [AvaloniaFact]
    public void SelectPlainItem_ShowsManualReleaseEntryPoints()
    {
        var item = new CatalogContentItem { Id = "plain", Name = "Plain", ContentType = ContentType.Mod };
        var viewModel = CreateViewModel(item, Mock.Of<ICatalogUpstreamIngestionService>());
        viewModel.SelectedContent = item;

        Assert.False(viewModel.SelectedContentTracksUpstream);
        Assert.True(viewModel.CanAddManualRelease);
        Assert.True(viewModel.ShowReleasesDropZone);
    }

    /// <summary>
    /// Verifies that the add-release command refuses to open the dialog for
    /// upstream-tracked items instead of creating a manual release.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task AddReleaseCommand_UpstreamTracked_DoesNotAddReleaseAsync()
    {
        var dialogMock = new Mock<IPublisherStudioDialogService>();
        var ingestionMock = new Mock<ICatalogUpstreamIngestionService>();
        ingestionMock
            .Setup(s => s.IngestCatalogAsync(It.IsAny<PublisherCatalog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var item = CreateUpstreamItem("item-guarded");
        var viewModel = CreateViewModel(item, ingestionMock.Object, dialogMock.Object);
        viewModel.SelectedContent = item;
        await WaitForPreviewSettledAsync(viewModel, ingestionMock);

        await viewModel.AddReleaseCommand.ExecuteAsync(null);

        Assert.Empty(item.Releases);
        dialogMock.Verify(
            d => d.ShowAddReleaseDialogAsync(It.IsAny<CatalogContentItem>(), It.IsAny<PublisherCatalog>(), It.IsAny<IEnumerable<string>?>()),
            Times.Never);
    }

    /// <summary>
    /// Verifies that the add-release command still creates a manual release for
    /// plain content items.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [AvaloniaFact]
    public async Task AddReleaseCommand_PlainItem_AddsReleaseAsync()
    {
        var created = new ContentRelease { Version = "1.0.0", IsLatest = true };
        var dialogMock = new Mock<IPublisherStudioDialogService>();
        dialogMock
            .Setup(d => d.ShowAddReleaseDialogAsync(It.IsAny<CatalogContentItem>(), It.IsAny<PublisherCatalog>(), It.IsAny<IEnumerable<string>?>()))
            .ReturnsAsync(created);

        var item = new CatalogContentItem { Id = "plain-add", Name = "Plain Add", ContentType = ContentType.Mod };
        var viewModel = CreateViewModel(item, Mock.Of<ICatalogUpstreamIngestionService>(), dialogMock.Object);
        viewModel.SelectedContent = item;

        await viewModel.AddReleaseCommand.ExecuteAsync(null);

        Assert.Same(created, Assert.Single(item.Releases));
    }

    /// <summary>
    /// Verifies that content bundles hide manual release creation and artifact UI,
    /// since bundle releases version a dependency graph instead of files.
    /// </summary>
    [AvaloniaFact]
    public void BundleItem_HidesManualReleaseAndArtifacts()
    {
        var bundle = new CatalogContentItem { Id = "bundle-1", Name = "Bundle", ContentType = ContentType.ContentBundle };
        var viewModel = CreateViewModel(bundle, Mock.Of<ICatalogUpstreamIngestionService>());
        viewModel.SelectedContent = bundle;

        Assert.False(viewModel.CanAddManualRelease);
        Assert.False(viewModel.ShowReleasesDropZone);
        Assert.True(viewModel.IsSelectedContentBundle);
        Assert.False(viewModel.ShowReleaseArtifacts);
    }

    /// <summary>
    /// Verifies that plain items still allow manual releases and show artifacts.
    /// </summary>
    [AvaloniaFact]
    public void PlainItem_ShowsManualReleaseAndArtifacts()
    {
        var item = new CatalogContentItem { Id = "plain-1", Name = "Plain", ContentType = ContentType.Mod };
        var viewModel = CreateViewModel(item, Mock.Of<ICatalogUpstreamIngestionService>());
        viewModel.SelectedContent = item;

        Assert.True(viewModel.CanAddManualRelease);
        Assert.False(viewModel.IsSelectedContentBundle);
        Assert.True(viewModel.ShowReleaseArtifacts);
    }

    private static CatalogContentItem CreateUpstreamItem(string id) => new()
    {
        Id = id,
        Name = id,
        ContentType = ContentType.GameClient,
        UpstreamSync = new CatalogUpstreamSync
        {
            Provider = CatalogConstants.UpstreamProviders.TheSuperHackers,
            Repository = "TheSuperHackers/GeneralsGameCode",
        },
    };

    private static ContentLibraryViewModel CreateViewModel(
        CatalogContentItem activeItem,
        ICatalogUpstreamIngestionService ingestionService,
        params CatalogContentItem[] extraItems)
    {
        return CreateViewModel(activeItem, ingestionService, Mock.Of<IPublisherStudioDialogService>(), extraItems);
    }

    private static ContentLibraryViewModel CreateViewModel(
        CatalogContentItem activeItem,
        ICatalogUpstreamIngestionService ingestionService,
        IPublisherStudioDialogService dialogService,
        params CatalogContentItem[] extraItems)
    {
        var catalog = new NamedCatalog
        {
            Id = "test",
            Name = "Test",
            Catalog = new PublisherCatalog
            {
                Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
                Content = [activeItem, .. extraItems],
            },
        };
        var project = new PublisherStudioProject
        {
            ProjectPath = "/test/path/project.json",
            Catalogs = [catalog],
        };

        return new ContentLibraryViewModel(
            project,
            catalog,
            null!,
            NullLogger.Instance,
            dialogService,
            upstreamIngestionService: ingestionService);
    }

    private static async Task WaitForPreviewAsync(ContentLibraryViewModel viewModel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (viewModel.UpstreamPreviewReleases.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static async Task WaitForPreviewSettledAsync(
        ContentLibraryViewModel viewModel,
        Mock<ICatalogUpstreamIngestionService> ingestionMock)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (ingestionMock.Invocations.Count < 1 || viewModel.IsUpstreamPreviewLoading)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }

    private static async Task WaitForIngestionAsync(Mock<ICatalogUpstreamIngestionService> ingestionMock, int expectedCalls)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (ingestionMock.Invocations.Count < expectedCalls)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
        }
    }
}
