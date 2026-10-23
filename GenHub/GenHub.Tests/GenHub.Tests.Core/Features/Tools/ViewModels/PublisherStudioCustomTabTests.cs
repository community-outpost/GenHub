using GenHub.Core.Interfaces.Notifications;
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
using System.Collections.Generic;
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
    /// Tabs authored in the tab editor survive a project save and load, export, and the runtime parser,
    /// including edited and added cards.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task EditCustomTabDialog_AuthoredTabs_SurviveSaveLoadExportAndRuntimeParseAsync()
    {
        var existing = new CatalogTabDefinition
        {
            TabId = "gallery",
            Header = "Gallery",
            Order = 20,
            Cards = [new CatalogTabCardDefinition { Title = "First card", ImageUrl = "https://example.com/card.png" }],
        };
        CatalogTabDefinition? edited = null;
        var editor = new EditCustomTabDialogViewModel(existing, [], tab => edited = tab);
        Assert.Equal("First card", Assert.Single(editor.Tab.Cards).Title);
        editor.Tab.Header = "Screenshots";
        editor.AddCardCommand.Execute(null);
        editor.Tab.Cards[1].Title = "Second card";
        editor.Tab.Cards[1].Description = "Added in Studio.";
        editor.SaveCommand.Execute(null);
        Assert.NotNull(edited);

        CatalogTabDefinition? added = null;
        var adder = new EditCustomTabDialogViewModel(null, [edited], tab => added = tab);
        adder.Tab.Header = "Install guide";
        adder.Tab.Intro = "Read this first.";
        adder.Tab.Body = "## Steps\n\n1. Install the base game.";
        Assert.Equal("## Steps\n\n1. Install the base game.", adder.Tab.Preview!.Body);
        adder.SaveCommand.Execute(null);
        Assert.NotNull(added);

        var item = CreateItem();
        item.CustomTabs = [edited, added];
        var service = new PublisherStudioService(NullLogger<PublisherStudioService>.Instance, CreateParser());
        var project = new PublisherStudioProject
        {
            ProjectPath = Path.Combine(_tempDir, "project.json"),
            Catalog = new PublisherCatalog
            {
                SchemaVersion = 1,
                Publisher = new PublisherProfile { Id = "tabs-pub", Name = "Tabs Publisher" },
                Content = [item],
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
        Assert.Equal("Screenshots", tabs[0].Header);
        Assert.Equal(["First card", "Second card"], tabs[0].Cards.Select(c => c.Title));
        Assert.Equal(30, tabs[1].Order);
        Assert.Equal("Read this first.", tabs[1].Intro);
        Assert.Equal("## Steps\n\n1. Install the base game.", tabs[1].Body);
    }

    /// <summary>
    /// The tab editor rejects a duplicate ID, a missing header, or a tab with neither, and keeps the editor open.
    /// </summary>
    /// <param name="tabId">The ID typed into the editor.</param>
    /// <param name="header">The header typed into the editor.</param>
    [Theory]
    [InlineData("NOTES", "More notes")]
    [InlineData("faq", " ")]
    [InlineData(" ", " ")]
    public void EditCustomTabDialog_InvalidTab_ShowsErrorAndDoesNotSave(string tabId, string header)
    {
        var saved = false;
        var editor = new EditCustomTabDialogViewModel(null, [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }], _ => saved = true);
        editor.Tab.TabId = tabId;
        editor.Tab.Header = header;

        editor.SaveCommand.Execute(null);

        Assert.False(saved);
        Assert.False(string.IsNullOrWhiteSpace(editor.ValidationError));
    }

    /// <summary>
    /// Card problems in the tab editor are reported with the card number, so the author can fix them in place.
    /// </summary>
    [Fact]
    public void EditCustomTabDialog_InvalidCard_ShowsCardErrorAndDoesNotSave()
    {
        var saved = false;
        var editor = new EditCustomTabDialogViewModel(
            new CatalogTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CatalogTabCardDefinition { Title = "Card", ImageUrl = "http://example.com/card.png" }],
            },
            [],
            _ => saved = true);

        editor.SaveCommand.Execute(null);

        Assert.False(saved);
        Assert.Equal("Card 1 on custom tab 'gallery' needs an image link that starts with https://.", editor.ValidationError);

        editor.Tab.Cards[0].ImageUrl = "https://example.com/card.png";
        editor.SaveCommand.Execute(null);
        Assert.True(saved);
    }

    /// <summary>
    /// Null card entries kept from a hand-written catalog are dropped like the parser does, and removed cards are not saved.
    /// </summary>
    [Fact]
    public void EditCustomTabDialog_NullAndRemovedCards_AreNotSaved()
    {
        CatalogTabDefinition? saved = null;
        var editor = new EditCustomTabDialogViewModel(
            new CatalogTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CatalogTabCardDefinition { Title = "Keep" }, null!, new CatalogTabCardDefinition { Title = "Remove" }],
            },
            [],
            tab => saved = tab);

        editor.RemoveCardCommand.Execute(editor.Tab.Cards[1]);
        editor.SaveCommand.Execute(null);

        Assert.NotNull(saved);
        Assert.Equal("Keep", Assert.Single(saved.Cards).Title);
    }

    /// <summary>
    /// A tab without a typed ID shows an ID generated from its header as the placeholder and saves with it,
    /// unique among the item's other tabs. A typed ID wins.
    /// </summary>
    [Fact]
    public void EditCustomTabDialog_EmptyTabId_UsesIdGeneratedFromHeader()
    {
        CatalogTabDefinition? saved = null;
        var editor = new EditCustomTabDialogViewModel(null, [new CatalogTabDefinition { TabId = "how-to-play", Header = "Rules" }], tab => saved = tab);
        Assert.Null(editor.Tab.SuggestedTabId);

        editor.Tab.Header = "How to Play";
        Assert.Equal("how-to-play-2", editor.Tab.SuggestedTabId);
        editor.SaveCommand.Execute(null);
        Assert.Equal("how-to-play-2", saved!.TabId);

        var typed = new EditCustomTabDialogViewModel(null, [], tab => saved = tab);
        typed.Tab.Header = "Credits";
        typed.Tab.TabId = "thanks";
        typed.SaveCommand.Execute(null);
        Assert.Equal("thanks", saved.TabId);
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
    /// The content library adds a tab through the tab editor, shows it after the built-in tabs, selects it and saves the project.
    /// Editing passes the other tabs as siblings and replaces the tab in place.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentLibrary_AddAndEditCustomTab_StoresTabsOnItemAsync()
    {
        var target = CreateItem();
        target.CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes", Order = 10 }];
        var (library, dialogService, studioService) = CreateLibrary(target);
        dialogService
            .Setup(d => d.ShowEditCustomTabDialogAsync(null, It.IsAny<IReadOnlyList<CatalogTabDefinition>>()))
            .ReturnsAsync(new CatalogTabDefinition { TabId = "guide", Header = "Guide", Order = 20 });
        IReadOnlyList<CatalogTabDefinition>? editSiblings = null;
        dialogService
            .Setup(d => d.ShowEditCustomTabDialogAsync(It.Is<CatalogTabDefinition?>(t => t != null && t.TabId == "notes"), It.IsAny<IReadOnlyList<CatalogTabDefinition>>()))
            .Callback<CatalogTabDefinition?, IReadOnlyList<CatalogTabDefinition>>((_, siblings) => editSiblings = siblings)
            .ReturnsAsync(new CatalogTabDefinition { TabId = "notes", Header = "Patch notes", Order = 10 });

        Assert.Equal(["notes"], library.SelectedContentCustomTabs.Select(t => t.TabId));
        await library.AddCustomTabCommand.ExecuteAsync(null);

        Assert.Equal(["notes", "guide"], target.CustomTabs.Select(t => t.TabId));
        Assert.Equal(["notes", "guide"], library.SelectedContentCustomTabs.Select(t => t.TabId));
        Assert.Equal(ContentLibraryViewModel.BuiltInDetailTabCount + 1, library.SelectedDetailTabIndex);

        await library.EditCustomTabCommand.ExecuteAsync(library.SelectedContentCustomTabs[0]);

        Assert.Equal(["guide"], editSiblings!.Select(t => t.TabId));
        Assert.Equal(["Patch notes", "Guide"], target.CustomTabs.Select(t => t.Header));
        Assert.Equal("Patch notes", library.SelectedContentCustomTabs[0].Header);
        studioService.Verify(s => s.SaveProjectAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    /// <summary>
    /// Deleting a tab in the content library asks for confirmation. A confirmed delete removes the tab, and a cancel keeps it.
    /// </summary>
    /// <param name="confirm">The author's answer to the confirmation prompt.</param>
    /// <returns>A task representing the asynchronous test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ContentLibrary_DeleteCustomTab_AsksForConfirmationAsync(bool confirm)
    {
        var target = CreateItem();
        target.CustomTabs =
        [
            new CatalogTabDefinition { TabId = "notes", Header = "Notes" },
            new CatalogTabDefinition { TabId = "faq", Header = "FAQ" },
        ];
        var (library, dialogService, _) = CreateLibrary(target);
        dialogService
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.Is<string>(m => m.Contains("Notes")), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(confirm);

        await library.DeleteCustomTabCommand.ExecuteAsync(library.SelectedContentCustomTabs[0]);

        Assert.Equal(confirm ? ["faq"] : ["notes", "faq"], target.CustomTabs.Select(t => t.TabId));
        Assert.Equal(confirm ? ["faq"] : ["notes", "faq"], library.SelectedContentCustomTabs.Select(t => t.TabId));
        dialogService.Verify(
            d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);
    }

    /// <summary>
    /// A tab without an ID keeps the field empty in the editor and gets an ID unique among its siblings,
    /// and the ID placeholder explains that the ID comes from the header until there is one.
    /// </summary>
    [Fact]
    public void EditCustomTabDialog_IdLessTab_GetsIdUniqueAmongSiblings()
    {
        CatalogTabDefinition? saved = null;
        var editor = new EditCustomTabDialogViewModel(
            new CatalogTabDefinition { TabId = string.Empty, Header = "Notes" },
            [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }],
            tab => saved = tab);

        Assert.Equal(string.Empty, editor.Tab.TabId);
        Assert.Equal("notes-2", editor.TabIdWatermark);
        editor.SaveCommand.Execute(null);
        Assert.Equal("notes-2", saved!.TabId);

        var blank = new EditCustomTabDialogViewModel(null, [], _ => { });
        Assert.Equal("Generated from the header", blank.TabIdWatermark);
    }

    /// <summary>
    /// With duplicate tab IDs in a hand-written catalog, edit and delete act on the tab that was clicked.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentLibrary_DuplicateTabIds_ActOnClickedTabAsync()
    {
        var target = CreateItem();
        var first = new CatalogTabDefinition { TabId = "notes", Header = "First", Order = 10 };
        var second = new CatalogTabDefinition { TabId = "notes", Header = "Second", Order = 20 };
        target.CustomTabs = [first, second];
        var (library, dialogService, _) = CreateLibrary(target);
        dialogService
            .Setup(d => d.ShowEditCustomTabDialogAsync(second, It.IsAny<IReadOnlyList<CatalogTabDefinition>>()))
            .ReturnsAsync(new CatalogTabDefinition { TabId = "notes-2", Header = "Second edited", Order = 20 });

        await library.EditCustomTabCommand.ExecuteAsync(library.SelectedContentCustomTabs[1]);

        Assert.Same(first, target.CustomTabs[0]);
        Assert.Equal("Second edited", target.CustomTabs[1].Header);
    }

    /// <summary>
    /// Deleting a tab before the selected one keeps the selected tab selected, and the author gets a success toast.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentLibrary_DeleteEarlierTab_KeepsSelectionAndNotifiesAsync()
    {
        var target = CreateItem();
        target.CustomTabs =
        [
            new CatalogTabDefinition { TabId = "notes", Header = "Notes", Order = 10 },
            new CatalogTabDefinition { TabId = "faq", Header = "FAQ", Order = 20 },
        ];
        var notifications = new Mock<INotificationService>();
        var (library, dialogService, _) = CreateLibrary(target, notifications.Object);
        dialogService
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        library.SelectedDetailTabIndex = ContentLibraryViewModel.BuiltInDetailTabCount + 1;

        await library.DeleteCustomTabCommand.ExecuteAsync(library.SelectedContentCustomTabs[0]);

        Assert.Equal(["faq"], library.SelectedContentCustomTabs.Select(t => t.TabId));
        Assert.Equal(ContentLibraryViewModel.BuiltInDetailTabCount, library.SelectedDetailTabIndex);
        notifications.Verify(n => n.ShowSuccess(It.IsAny<string>(), It.Is<string>(m => m.Contains("Notes")), It.IsAny<int?>(), It.IsAny<bool>()), Times.Once);
    }

    /// <summary>
    /// A second delete cannot start while the first is waiting for the author's confirmation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentLibrary_DeleteCustomTab_IsNotReentrantAsync()
    {
        var target = CreateItem();
        target.CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }];
        var (library, dialogService, _) = CreateLibrary(target);
        var confirmation = new TaskCompletionSource<bool>();
        dialogService
            .Setup(d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(confirmation.Task);
        var tab = library.SelectedContentCustomTabs[0];

        var pending = library.DeleteCustomTabCommand.ExecuteAsync(tab);
        Assert.False(library.DeleteCustomTabCommand.CanExecute(tab));
        confirmation.SetResult(false);
        await pending;

        Assert.True(library.DeleteCustomTabCommand.CanExecute(tab));
        dialogService.Verify(
            d => d.ShowConfirmationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Once);
    }

    /// <summary>
    /// The content dialog returns the item's custom tabs with the edited item, so no caller can drop them.
    /// </summary>
    [Fact]
    public void EditContentDialog_Save_KeepsCustomTabs()
    {
        var existing = CreateItem();
        existing.CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }];
        CatalogContentItem? edited = null;
        using var dialog = new AddContentDialogViewModel(existing, item => edited = item);

        dialog.CreateContentCommand.Execute(null);

        Assert.NotNull(edited);
        Assert.Equal("notes", Assert.Single(edited.CustomTabs).TabId);
    }

    /// <summary>
    /// Editing an item's other fields in the content dialog keeps its custom tabs.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ContentLibrary_EditContent_KeepsCustomTabsAsync()
    {
        var target = CreateItem();
        target.CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes" }];
        var (library, dialogService, _) = CreateLibrary(target);
        var edited = CreateItem();
        edited.Name = "Renamed";
        dialogService
            .Setup(d => d.ShowEditContentDialogAsync(target, It.IsAny<PublisherCatalog?>(), It.IsAny<Func<CatalogContentItem, Task>?>()))
            .ReturnsAsync(edited);

        await library.EditContentCommand.ExecuteAsync(target);

        Assert.Equal("Renamed", target.Name);
        Assert.Equal("notes", Assert.Single(target.CustomTabs).TabId);
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

    private static (ContentLibraryViewModel Library, Mock<IPublisherStudioDialogService> DialogService, Mock<IPublisherStudioService> StudioService) CreateLibrary(
        CatalogContentItem target,
        INotificationService? notifications = null)
    {
        var project = new PublisherStudioProject();
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "default",
            Name = "Content",
            FileName = "catalog.json",
            Catalog = new PublisherCatalog { Content = [target] },
        });
        var dialogService = new Mock<IPublisherStudioDialogService>();
        var studioService = new Mock<IPublisherStudioService>();
        studioService
            .Setup(s => s.SaveProjectAsync(It.IsAny<PublisherStudioProject>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        var parent = new PublisherStudioViewModel(
            new Mock<ILogger<PublisherStudioViewModel>>().Object,
            studioService.Object,
            dialogService.Object);
        parent.CurrentProject = project;
        var library = new ContentLibraryViewModel(
            project,
            project.Catalogs[0],
            parent,
            new Mock<ILogger<ContentLibraryViewModel>>().Object,
            dialogService.Object,
            notifications);
        library.SelectedContent = target;
        return (library, dialogService, studioService);
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
