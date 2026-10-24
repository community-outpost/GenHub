using Avalonia.Headless.XUnit;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Tests for the optional initial release, staged path display, and item counts in Publisher Studio.
/// </summary>
public sealed class PublisherStudioContentDialogFixTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "GenHubContentDialogFix_" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// A new item with an empty initial release section saves as a draft with no releases.
    /// </summary>
    [Fact]
    public void CreateContent_NoInitialRelease_CreatesDraftWithoutReleases()
    {
        CatalogContentItem? created = null;
        using var vm = new AddContentDialogViewModel(item => created = item)
        {
            ContentName = "Draft Mod",
            ContentId = "draft-mod",
            Description = "A description that meets the length requirements",
        };

        vm.CreateContentCommand.Execute(null);

        Assert.Null(vm.ValidationError);
        Assert.NotNull(created);
        Assert.Equal("draft-mod", created.Id);
        Assert.Empty(created.Releases);
    }

    /// <summary>
    /// A download URL that is provided but invalid still blocks the save.
    /// </summary>
    [Fact]
    public void CreateContent_InvalidDownloadUrl_IsRejected()
    {
        CatalogContentItem? created = null;
        using var vm = new AddContentDialogViewModel(item => created = item)
        {
            ContentName = "Broken Mod",
            ContentId = "broken-mod",
            Description = "A description that meets the length requirements",
        };
        vm.UseDirectUrl = true;
        vm.DownloadUrl = "ftp://example.com/mod.zip";

        vm.CreateContentCommand.Execute(null);

        Assert.Null(created);
        Assert.False(string.IsNullOrEmpty(vm.ValidationError));
    }

    /// <summary>
    /// A valid download URL still creates the initial release.
    /// </summary>
    [Fact]
    public void CreateContent_DownloadUrl_CreatesInitialRelease()
    {
        CatalogContentItem? created = null;
        using var vm = new AddContentDialogViewModel(item => created = item)
        {
            ContentName = "Hosted Mod",
            ContentId = "hosted-mod",
            Description = "A description that meets the length requirements",
        };
        vm.UseDirectUrl = true;
        vm.DownloadUrl = "https://example.com/hosted-mod.zip";

        vm.CreateContentCommand.Execute(null);

        Assert.NotNull(created);
        var release = Assert.Single(created.Releases);
        var artifact = Assert.Single(release.Artifacts);
        Assert.Equal("https://example.com/hosted-mod.zip", artifact.DownloadUrl);
    }

    /// <summary>
    /// A relative path is staged as a full path so the stored artifact path does not depend on the working directory.
    /// </summary>
    [Fact]
    public void PopulateFromPath_RelativePath_StagesFullPath()
    {
        Directory.CreateDirectory(_tempDir);
        var file = Path.Combine(_tempDir, "relative-mod.big");
        File.WriteAllText(file, "payload");
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, file);
        using var vm = new AddContentDialogViewModel(_ => { });

        vm.PopulateFromPath(relative);

        var entry = Assert.Single(vm.StagedFiles);
        Assert.Equal(Path.GetFullPath(file), entry.LocalPath);
        Assert.Equal(Path.GetFullPath(file), vm.LocalFilePath);
    }

    /// <summary>
    /// A path the OS cannot resolve is ignored instead of throwing.
    /// </summary>
    [Fact]
    public void PopulateFromPath_UnresolvablePath_IsIgnored()
    {
        using var vm = new AddContentDialogViewModel(_ => { });

        vm.PopulateFromPath("bad\0path.zip");

        Assert.Empty(vm.StagedFiles);
    }

    /// <summary>
    /// The library footer uses the singular string for one item and the plural string otherwise.
    /// </summary>
    /// <param name="count">The number of catalog items.</param>
    /// <param name="expectedKey">The localization key the footer must use.</param>
    [AvaloniaTheory]
    [InlineData(1, "Tools.PublisherStudio.Library.ItemsInCatalogOne")]
    [InlineData(2, "Tools.PublisherStudio.Library.ItemsInCatalog")]
    public void CatalogSummaryText_UsesCountSpecificKey(int count, string expectedKey)
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns((string key, object?[] _) => key + ":{0}");
        var items = new CatalogContentItem[count];
        for (var i = 0; i < count; i++)
        {
            items[i] = new CatalogContentItem { Id = $"item-{i}", Name = $"Item {i}", ContentType = ContentType.Mod };
        }

        var vm = CreateLibrary(localization.Object, items);

        Assert.Equal($"{expectedKey}:{count}", vm.CatalogSummaryText);
    }

    /// <summary>
    /// The library footer falls back to a singular English string for one item.
    /// </summary>
    [AvaloniaFact]
    public void CatalogSummaryText_OneItemWithoutLocalization_IsSingular()
    {
        var vm = CreateLibrary(null, new CatalogContentItem { Id = "solo", Name = "Solo", ContentType = ContentType.Mod });

        Assert.Equal("1 item in catalog", vm.CatalogSummaryText);
    }

    /// <summary>
    /// The Publish tab catalog count uses the singular string for one item and the plural string otherwise.
    /// </summary>
    /// <param name="count">The number of content nodes.</param>
    /// <param name="expectedKey">The localization key the count must use.</param>
    [Theory]
    [InlineData(1, "Tools.PublisherStudio.Publish.ContentItemsFormatOne")]
    [InlineData(3, "Tools.PublisherStudio.Publish.ContentItemsFormat")]
    public void ContentItemsCountText_UsesCountSpecificKey(int count, string expectedKey)
    {
        var localization = new Mock<ILocalizationService>();
        string? format;
        localization.Setup(l => l.TryGetString(It.IsAny<string>(), out format, It.IsAny<object?[]>()))
            .Returns(new TryGetStringCallback((string key, out string? result, object?[] _) =>
            {
                result = key + ":{0}";
                return true;
            }));
        var node = CreateCatalogNode(count);
        node.LocalizationService = localization.Object;

        Assert.Equal($"{expectedKey}:{count}", node.ContentItemsCountText);
    }

    /// <summary>
    /// The Publish tab catalog count falls back to singular and plural English strings.
    /// </summary>
    [Fact]
    public void ContentItemsCountText_WithoutLocalization_UsesSingularForOne()
    {
        Assert.Equal("1 item", CreateCatalogNode(1).ContentItemsCountText);
        Assert.Equal("2 items", CreateCatalogNode(2).ContentItemsCountText);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private static ContentLibraryViewModel CreateLibrary(ILocalizationService? localization, params CatalogContentItem[] items)
    {
        var catalog = new NamedCatalog
        {
            Id = "test",
            Name = "Test",
            Catalog = new PublisherCatalog
            {
                Publisher = new PublisherProfile { Id = "test-pub", Name = "Test Publisher" },
                Content = [.. items],
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
            Mock.Of<IPublisherStudioDialogService>(),
            localizationService: localization);
    }

    private static UploadCatalogNodeViewModel CreateCatalogNode(int count)
    {
        var node = new UploadCatalogNodeViewModel();
        for (var i = 0; i < count; i++)
        {
            node.ContentItems.Add(new UploadContentNodeViewModel());
        }

        return node;
    }

    private delegate bool TryGetStringCallback(string key, out string? result, object?[] arguments);
}
