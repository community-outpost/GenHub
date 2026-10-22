using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.Services;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.ViewModels.Dialogs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.ViewModels;

/// <summary>
/// Tests authoring publisher-defined custom tabs on content items in Publisher Studio.
/// </summary>
public sealed class PublisherStudioCustomTabTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "GenHubCustomTabTests_" + Guid.NewGuid().ToString("N"));

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    /// <summary>
    /// Tabs authored in the content dialog survive a project save and load, export, and the runtime parser,
    /// and cards written by hand in the catalog are kept.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EditContentDialog_AuthoredTabs_SurviveSaveLoadExportAndRuntimeParseAsync()
    {
        var existing = CreateItem();
        existing.CustomTabs =
        [
            new CatalogTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Order = 20,
                Cards = [new CatalogTabCardDefinition { Title = "Hand-written card", ImageUrl = "https://example.com/card.png" }],
            },
        ];

        CatalogContentItem? edited = null;
        using var dialog = new AddContentDialogViewModel(existing, item => edited = item);
        Assert.Equal("gallery", Assert.Single(dialog.CustomTabs).TabId);
        Assert.Equal(1, dialog.CustomTabs[0].CardCount);

        dialog.CustomTabs[0].Header = "Screenshots";
        dialog.AddCustomTabCommand.Execute(null);
        var added = dialog.CustomTabs[1];
        added.TabId = "install-guide";
        added.Header = "Install guide";
        added.Order = 5;
        added.Intro = "Read this first.";
        added.Body = "## Steps\n\n1. Install the base game.";
        Assert.Null(added.Preview);
        added.IsPreviewVisible = true;
        Assert.Equal("## Steps\n\n1. Install the base game.", added.Preview!.Body);

        dialog.CreateContentCommand.Execute(null);

        Assert.NotNull(edited);
        var service = new PublisherStudioService(NullLogger<PublisherStudioService>.Instance, CreateParser());
        var project = new PublisherStudioProject
        {
            ProjectPath = Path.Combine(_tempDir, "project.json"),
            Catalog = new PublisherCatalog
            {
                SchemaVersion = 1,
                Publisher = new PublisherProfile { Id = "tabs-pub", Name = "Tabs Publisher" },
                Content = [edited],
            },
        };

        Assert.True((await service.SaveProjectAsync(project)).Success);
        var loaded = await service.LoadProjectAsync(project.ProjectPath);
        Assert.True(loaded.Success, string.Join("; ", loaded.Errors));
        var exported = await service.ExportCatalogAsync(loaded.Data!);
        Assert.True(exported.Success, string.Join("; ", exported.Errors));
        var parsed = await CreateParser().ParseCatalogAsync(exported.Data!);

        Assert.True(parsed.Success, string.Join("; ", parsed.Errors));
        var tabs = Assert.Single(parsed.Data!.Content).CustomTabs;
        Assert.Equal(["gallery", "install-guide"], tabs.Select(t => t.TabId));
        var gallery = tabs[0];
        Assert.Equal("Screenshots", gallery.Header);
        Assert.Equal("Hand-written card", Assert.Single(gallery.Cards).Title);
        var guide = tabs[1];
        Assert.Equal(5, guide.Order);
        Assert.Equal("Read this first.", guide.Intro);
        Assert.Equal("## Steps\n\n1. Install the base game.", guide.Body);
    }

    /// <summary>
    /// The content dialog rejects tabs with duplicate IDs or a missing header and keeps the dialog open.
    /// </summary>
    /// <param name="secondTabId">The ID of the second tab.</param>
    /// <param name="secondHeader">The header of the second tab.</param>
    [Theory]
    [InlineData("NOTES", "More notes")]
    [InlineData("faq", " ")]
    [InlineData(" ", " ")]
    public void EditContentDialog_InvalidTab_ShowsErrorAndDoesNotSave(string secondTabId, string secondHeader)
    {
        var existing = CreateItem();
        existing.CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }];
        var saved = false;
        using var dialog = new AddContentDialogViewModel(existing, _ => saved = true);
        dialog.AddCustomTabCommand.Execute(null);
        dialog.CustomTabs[1].TabId = secondTabId;
        dialog.CustomTabs[1].Header = secondHeader;

        dialog.CreateContentCommand.Execute(null);

        Assert.False(saved);
        Assert.False(dialog.IsValid);
        Assert.False(string.IsNullOrWhiteSpace(dialog.ValidationError));
    }

    /// <summary>
    /// The preview is rebuilt only while it is shown, and once when it is turned on.
    /// </summary>
    [Fact]
    public void CustomTabEditorItem_Preview_NotifiesOnlyWhileVisible()
    {
        var tab = new CustomTabEditorItem(new CatalogTabDefinition { TabId = "notes", Header = "Notes" });
        var previewChanges = 0;
        tab.PropertyChanged += (_, e) => previewChanges += e.PropertyName == nameof(CustomTabEditorItem.Preview) ? 1 : 0;

        tab.Body = "hidden edit";
        tab.Header = "Hidden header";
        Assert.Equal(0, previewChanges);

        tab.IsPreviewVisible = true;
        Assert.Equal(1, previewChanges);
        Assert.Equal("Hidden header", tab.Preview!.Header);

        tab.Body = "visible edit";
        Assert.Equal(2, previewChanges);
        Assert.Equal("visible edit", tab.Preview!.Body);

        tab.IsPreviewVisible = false;
        tab.Intro = "hidden again";
        Assert.Equal(3, previewChanges);
        Assert.Null(tab.Preview);
    }

    /// <summary>
    /// A tab whose card image or body links to a signed Discord attachment warns that the link will expire.
    /// </summary>
    [Fact]
    public void CustomTabEditorItem_DiscordAttachmentLinks_Warn()
    {
        const string discord = "https://cdn.discordapp.com/attachments/1/2/card.png?ex=6720b1c3&is=671f6043&hm=abcdef";
        var cardTab = new CustomTabEditorItem(new CatalogTabDefinition
        {
            TabId = "gallery",
            Header = "Gallery",
            Cards = [new CatalogTabCardDefinition { Title = "Card", ImageUrl = discord }],
        });
        var plainTab = new CustomTabEditorItem(new CatalogTabDefinition { TabId = "notes", Header = "Notes", Body = "![shot](https://example.com/shot.png)" });

        Assert.True(cardTab.HasExpiringImageLinks);
        Assert.False(plainTab.HasExpiringImageLinks);

        plainTab.Body = $"![shot]({discord})";
        Assert.True(plainTab.HasExpiringImageLinks);
    }

    /// <summary>
    /// The content dialog applies the shared catalog tab rules, including card rules for cards kept from the catalog file.
    /// </summary>
    [Fact]
    public void EditContentDialog_InvalidKeptCard_ShowsCardErrorAndDoesNotSave()
    {
        var existing = CreateItem();
        existing.CustomTabs =
        [
            new CatalogTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CatalogTabCardDefinition { Title = "Card", ImageUrl = "http://example.com/card.png" }],
            },
        ];
        var saved = false;
        using var dialog = new AddContentDialogViewModel(existing, _ => saved = true);

        dialog.CreateContentCommand.Execute(null);

        Assert.False(saved);
        Assert.Equal("Custom tab 'gallery' has cards that break the catalog rules. Fix them in the catalog file.", dialog.ValidationError);
    }

    /// <summary>
    /// Null card entries kept from a hand-written catalog are dropped like the parser does, instead of blocking the save.
    /// </summary>
    [Fact]
    public void EditContentDialog_NullKeptCard_IsDroppedAndSaves()
    {
        var existing = CreateItem();
        existing.CustomTabs =
        [
            new CatalogTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CatalogTabCardDefinition { Title = "Card" }, null!],
            },
        ];
        CatalogContentItem? edited = null;
        using var dialog = new AddContentDialogViewModel(existing, item => edited = item);

        dialog.CreateContentCommand.Execute(null);

        Assert.NotNull(edited);
        Assert.Equal("Card", Assert.Single(Assert.Single(edited.CustomTabs).Cards).Title);
    }

    /// <summary>
    /// Removing a tab asks for confirmation. A confirmed removal drops the tab from the saved item, and a
    /// cancelled one keeps it.
    /// </summary>
    /// <param name="confirm">The author's answer to the confirmation prompt.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EditContentDialog_RemoveTab_AsksForConfirmationAsync(bool confirm)
    {
        var existing = CreateItem();
        existing.CustomTabs =
        [
            new CatalogTabDefinition { TabId = "notes", Header = "Notes" },
            new CatalogTabDefinition { TabId = "faq", Header = "FAQ" },
        ];
        var dialogService = new Mock<IPublisherStudioDialogService>();
        dialogService
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.Is<string>(m => m.Contains("Notes")), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(confirm);
        CatalogContentItem? edited = null;
        using var dialog = new AddContentDialogViewModel(existing, item => edited = item, dialogService.Object);

        await dialog.RemoveCustomTabCommand.ExecuteAsync(dialog.CustomTabs[0]);
        dialog.CreateContentCommand.Execute(null);

        Assert.NotNull(edited);
        Assert.Equal(confirm ? ["faq"] : ["notes", "faq"], edited.CustomTabs.Select(t => t.TabId));
        dialogService.Verify(
            d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);
    }

    /// <summary>
    /// A tab without a typed ID shows an ID generated from its header as the placeholder and saves with it.
    /// A typed ID wins, and generated IDs stay unique within the item.
    /// </summary>
    [Fact]
    public void EditContentDialog_EmptyTabId_UsesIdGeneratedFromHeader()
    {
        var existing = CreateItem();
        existing.CustomTabs = [new CatalogTabDefinition { TabId = "how-to-play", Header = "Rules" }];
        CatalogContentItem? edited = null;
        using var dialog = new AddContentDialogViewModel(existing, item => edited = item);

        dialog.AddCustomTabCommand.Execute(null);
        var added = dialog.CustomTabs[1];
        Assert.Null(added.SuggestedTabId);
        added.Header = "How to Play";
        Assert.Equal("how-to-play-2", added.SuggestedTabId);
        Assert.Null(dialog.CustomTabs[0].SuggestedTabId);

        dialog.AddCustomTabCommand.Execute(null);
        dialog.CustomTabs[2].Header = "Credits";
        dialog.CustomTabs[2].TabId = "thanks";

        dialog.CreateContentCommand.Execute(null);

        Assert.NotNull(edited);
        Assert.Equal(["how-to-play", "how-to-play-2", "thanks"], edited.CustomTabs.Select(t => t.TabId));
    }

    /// <summary>
    /// Editing an item in the content library stores the edited tabs on the catalog item.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentLibrary_EditContent_StoresEditedTabsOnItemAsync()
    {
        var target = CreateItem();
        target.CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }];
        var project = new PublisherStudioProject();
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "default",
            Name = "Content",
            FileName = "catalog.json",
            Catalog = new PublisherCatalog { Content = [target] },
        });

        var edited = CreateItem();
        edited.CustomTabs = [new CatalogTabDefinition { TabId = "guide", Header = "Guide", Body = "Body" }];
        var dialogService = new Mock<IPublisherStudioDialogService>();
        dialogService
            .Setup(d => d.ShowEditContentDialogAsync(target, It.IsAny<PublisherCatalog?>(), It.IsAny<Func<CatalogContentItem, Task>?>()))
            .ReturnsAsync(edited);
        var studioService = new Mock<IPublisherStudioService>();
        studioService
            .Setup(s => s.SaveProjectAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var parent = new PublisherStudioViewModel(
            new Mock<ILogger<PublisherStudioViewModel>>().Object,
            studioService.Object,
            dialogService.Object);
        var library = new ContentLibraryViewModel(
            project,
            project.Catalogs[0],
            parent,
            new Mock<ILogger<ContentLibraryViewModel>>().Object,
            dialogService.Object);

        await library.EditContentCommand.ExecuteAsync(target);

        var tab = Assert.Single(target.CustomTabs);
        Assert.Equal("guide", tab.TabId);
        Assert.Equal("Body", tab.Body);
    }

    /// <summary>
    /// Studio catalog validation still blocks card issues that the runtime parser only drops.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ValidateCatalogAsync_InvalidItemCard_StillBlocksAsync()
    {
        var item = CreateItem();
        item.CustomTabs =
        [
            new CatalogTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CatalogTabCardDefinition { Title = "Card", ImageUrl = "http://example.com/card.png" }],
            },
        ];
        var catalog = new PublisherCatalog
        {
            SchemaVersion = 1,
            Publisher = new PublisherProfile { Id = "tabs-pub", Name = "Tabs Publisher" },
            Content = [item],
        };
        var service = new PublisherStudioService(NullLogger<PublisherStudioService>.Instance, CreateParser());

        var result = await service.ValidateCatalogAsync(catalog);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("'mod-a'") && e.Contains("'gallery'") && e.Contains("invalid image URL"));
    }

    private static JsonPublisherCatalogParser CreateParser() => new(NullLogger<JsonPublisherCatalogParser>.Instance);

    private static CatalogContentItem CreateItem()
    {
        return new CatalogContentItem
        {
            Id = "mod-a",
            Name = "Mod A",
            Description = "A test mod with publisher tabs.",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            Releases =
            [
                new ContentRelease
                {
                    Version = "1.0.0",
                    Artifacts =
                    [
                        new ReleaseArtifact
                        {
                            Filename = "mod-a.zip",
                            DownloadUrl = "https://example.com/mod-a.zip",
                            Sha256 = new string('a', 64),
                            Size = 1024,
                        },
                    ],
                },
            ],
        };
    }
}
