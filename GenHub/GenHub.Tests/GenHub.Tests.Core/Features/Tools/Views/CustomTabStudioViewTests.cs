using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using GenHub.Core.Interfaces.Publishers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Publishers;
using GenHub.Features.Downloads.Views;
using GenHub.Features.Tools.Interfaces;
using GenHub.Features.Tools.ViewModels;
using GenHub.Features.Tools.ViewModels.Dialogs;
using GenHub.Features.Tools.Views.Dialogs;
using GenHub.Features.Tools.Views.PublisherStudio;
using Microsoft.Extensions.Logging;
using Moq;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.Views;

/// <summary>
/// Headless tests for publisher-defined custom tabs in Publisher Studio.
/// </summary>
public class CustomTabStudioViewTests
{
    /// <summary>
    /// The Studio item preview renders each custom tab as a real tab after the built-in tabs, with the same view
    /// players see, plus edit and delete buttons on the tab and edit buttons inside it.
    /// </summary>
    [AvaloniaFact]
    public void ContentLibraryView_RendersCustomTabsInPlaceWithEditAffordances()
    {
        var item = CreateItem();
        item.CustomTabs =
        [
            new CatalogTabDefinition { TabId = "notes", Header = "Notes", Body = "Body", Cards = [new CatalogTabCardDefinition { Title = "Card" }] },
            new CatalogTabDefinition { TabId = "faq", Header = "FAQ" },
        ];
        var library = CreateLibrary(item);
        var view = new ContentLibraryView { DataContext = library };
        var window = new Window { Content = view, Width = 1400, Height = 900 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var tabs = view.GetLogicalDescendants().OfType<TabControl>().Single(t => t.Name == "DetailTabs");
            Assert.Equal(ContentLibraryViewModel.BuiltInDetailTabCount + 2, tabs.Items.Count);

            var notes = (TabItem)tabs.Items[ContentLibraryViewModel.BuiltInDetailTabCount]!;
            var headerButtons = ((StackPanel)notes.Header!).Children.OfType<Button>().ToList();
            Assert.Same(library.EditCustomTabCommand, headerButtons[0].Command);
            Assert.Same(library.DeleteCustomTabCommand, headerButtons[1].Command);
            Assert.Same(library.SelectedContentCustomTabs[0], headerButtons[0].CommandParameter);

            var content = Assert.IsType<CustomTabContentView>(Assert.IsType<ScrollViewer>(notes.Content).Content);
            Assert.Same(library.SelectedContentCustomTabs[0], content.DataContext);
            Assert.Same(library.EditCustomTabCommand, content.EditCommand);

            tabs.SelectedIndex = ContentLibraryViewModel.BuiltInDetailTabCount;
            window.UpdateLayout();
            var editButtons = content.GetLogicalDescendants().OfType<Button>().Where(b => b.Classes.Contains("custom-tab-edit")).ToList();
            Assert.Equal(3, editButtons.Count);
            Assert.All(editButtons, b => Assert.True(b.IsVisible));

            library.SelectedContent!.CustomTabs.RemoveAt(1);
            library.SelectedContent = null;
            library.SelectedContent = item;
            window.UpdateLayout();
            tabs = view.GetLogicalDescendants().OfType<TabControl>().Single(t => t.Name == "DetailTabs");
            Assert.Equal(ContentLibraryViewModel.BuiltInDetailTabCount + 1, tabs.Items.Count);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The player-facing content view hides the edit buttons when no edit command is set.
    /// </summary>
    [AvaloniaFact]
    public void CustomTabContentView_WithoutEditCommand_HidesEditButtons()
    {
        var view = new CustomTabContentView
        {
            DataContext = new GenHub.Core.Models.Content.CustomTabDefinition
            {
                TabId = "notes",
                Header = "Notes",
                Body = "Body",
                Cards = [new GenHub.Core.Models.Content.CustomTabCardDefinition { Title = "Card" }],
            },
        };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var editButtons = view.GetLogicalDescendants().OfType<Button>().Where(b => b.Classes.Contains("custom-tab-edit")).ToList();
            Assert.Equal(3, editButtons.Count);
            Assert.All(editButtons, b => Assert.False(b.IsVisible));
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// A long preview in the tab editor grows inside the editor form, and the form's own scroller scrolls past it.
    /// </summary>
    [AvaloniaFact]
    public void EditCustomTabDialog_LongPreview_ScrollsInDialogForm()
    {
        var viewModel = new EditCustomTabDialogViewModel(
            new CatalogTabDefinition
            {
                TabId = "long",
                Header = "Long",
                Intro = string.Join(" ", Enumerable.Repeat("Long intro text.", 400)),
                Cards = [.. Enumerable.Range(0, 24).Select(i => new CatalogTabCardDefinition { Title = $"Card {i}" })],
            },
            [],
            _ => { });
        var view = new EditCustomTabDialogView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 860, Height = 720 };
        window.Show();
        try
        {
            window.UpdateLayout();

            var preview = Assert.Single(view.GetVisualDescendants().OfType<CustomTabContentView>());
            var form = preview.FindAncestorOfType<ScrollViewer>();
            Assert.NotNull(form);
            Assert.Null(form.FindAncestorOfType<ScrollViewer>());
            Assert.True(preview.Bounds.Height > form.Viewport.Height, $"Preview {preview.Bounds.Height} should be taller than the form viewport {form.Viewport.Height}");

            form.Offset = new Vector(0, form.Extent.Height - form.Viewport.Height);
            window.UpdateLayout();
            Assert.Equal(form.Extent.Height - form.Viewport.Height, form.Offset.Y, 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The tab order input in the tab editor is wide enough to show a four digit value without clipping.
    /// </summary>
    [AvaloniaFact]
    public void EditCustomTabDialog_OrderInput_FitsFourDigits()
    {
        var viewModel = new EditCustomTabDialogViewModel(new CatalogTabDefinition { TabId = "notes", Header = "Notes", Order = 1000 }, [], _ => { });
        var view = new EditCustomTabDialogView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 860, Height = 720 };
        window.Show();
        try
        {
            window.UpdateLayout();

            var order = Assert.Single(view.GetVisualDescendants().OfType<NumericUpDown>(), n => n.Value == 1000);
            var textBox = order.GetVisualDescendants().OfType<TextBox>().First();
            var sample = new TextBlock { Text = "1000", FontSize = textBox.FontSize, FontFamily = textBox.FontFamily, FontWeight = textBox.FontWeight };
            sample.Measure(Size.Infinity);
            var available = textBox.Bounds.Width - textBox.Padding.Left - textBox.Padding.Right - textBox.BorderThickness.Left - textBox.BorderThickness.Right;
            Assert.True(available >= sample.DesiredSize.Width, $"Order text area {available} is narrower than four digits {sample.DesiredSize.Width}");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// A long image URL does not widen the tab editor. The editor keeps a fixed width.
    /// </summary>
    [AvaloniaFact]
    public void EditCustomTabDialog_LongImageUrl_KeepsWidth()
    {
        var viewModel = new EditCustomTabDialogViewModel(
            new CatalogTabDefinition { TabId = "gallery", Header = "Gallery", Cards = [new CatalogTabCardDefinition { Title = "Card" }] },
            [],
            _ => { });
        var view = new EditCustomTabDialogView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 2000, Height = 900 };
        window.Show();
        try
        {
            window.UpdateLayout();
            view.Measure(Size.Infinity);
            var before = view.DesiredSize.Width;

            viewModel.Tab.Cards[0].ImageUrl = "https://example.com/" + new string('a', 600) + ".png";
            window.UpdateLayout();
            view.Measure(Size.Infinity);

            Assert.Equal(before, view.DesiredSize.Width);
            Assert.True(before <= 800, $"Editor width {before} should stay bounded");
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The editor's actions sit below the form, so they never cover the end of the preview, and the order
    /// input matches the text inputs beside it.
    /// </summary>
    [AvaloniaFact]
    public void EditCustomTabDialog_FooterBelowFormAndOrderMatchesInputs()
    {
        var viewModel = new EditCustomTabDialogViewModel(new CatalogTabDefinition { TabId = "notes", Header = "Notes" }, [], _ => { });
        var view = new EditCustomTabDialogView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 800, Height = 720 };
        window.Show();
        try
        {
            window.UpdateLayout();

            var form = view.GetVisualDescendants().OfType<ScrollViewer>().First();
            var save = view.GetVisualDescendants().OfType<Button>().Single(b => b.Command == viewModel.SaveCommand);
            var formBottom = form.TranslatePoint(new Point(0, form.Bounds.Height), view)!.Value.Y;
            var saveTop = save.TranslatePoint(new Point(0, 0), view)!.Value.Y;
            Assert.True(saveTop >= formBottom, $"Save button top {saveTop} should be below the form bottom {formBottom}");

            var header = view.GetVisualDescendants().OfType<TextBox>().First(t => t.Text == "Notes");
            var order = view.GetVisualDescendants().OfType<NumericUpDown>().Single();
            var orderText = order.GetVisualDescendants().OfType<TextBox>().First();
            Assert.Equal(header.FontSize, orderText.FontSize);
            Assert.Equal(VerticalAlignment.Center, orderText.VerticalContentAlignment);
            Assert.Same(header.Background, order.Background);
            Assert.Equal(header.Bounds.Height, order.Bounds.Height, 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// On a card whose image failed, the card edit pencil sits beside the fallback text instead of over it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CustomTabContentView_CardEditButton_DoesNotOverlapFallbackTextAsync()
    {
        var view = new CustomTabContentView
        {
            EditCommand = new CommunityToolkit.Mvvm.Input.RelayCommand<object?>(_ => { }),
            DataContext = new GenHub.Core.Models.Content.CustomTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new GenHub.Core.Models.Content.CustomTabCardDefinition { Title = "Card", ImageUrl = "https://127.0.0.1/blocked.png" }],
            },
        };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var image = view.GetLogicalDescendants().OfType<Image>().Single(i => i.Name == "CardImage");
            for (var i = 0; i < 500 && !GenHub.Infrastructure.Controls.ImageLoader.GetLoadFailed(image); i++)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                await Task.Delay(10);
            }

            window.UpdateLayout();
            var link = view.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CardImageLink");
            var pencil = view.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CardEditButton");
            Assert.True(link.IsVisible);
            var linkRight = link.TranslatePoint(new Point(link.Bounds.Width, 0), view)!.Value.X;
            var pencilLeft = pencil.TranslatePoint(new Point(0, 0), view)!.Value.X;
            Assert.True(pencilLeft >= linkRight, $"Pencil left {pencilLeft} should be right of the link {linkRight}");
        }
        finally
        {
            window.Close();
        }
    }

    private static ContentLibraryViewModel CreateLibrary(CatalogContentItem item)
    {
        var project = new PublisherStudioProject();
        project.Catalogs.Add(new NamedCatalog
        {
            Id = "default",
            Name = "Content",
            FileName = "catalog.json",
            Catalog = new PublisherCatalog { Content = [item] },
        });
        var dialogService = new Mock<IPublisherStudioDialogService>();
        var parent = new PublisherStudioViewModel(
            new Mock<ILogger<PublisherStudioViewModel>>().Object,
            new Mock<IPublisherStudioService>().Object,
            dialogService.Object);
        var library = new ContentLibraryViewModel(
            project,
            project.Catalogs[0],
            parent,
            new Mock<ILogger<ContentLibraryViewModel>>().Object,
            dialogService.Object);
        library.SelectedContent = item;
        return library;
    }

    private static CatalogContentItem CreateItem()
    {
        return new CatalogContentItem
        {
            Id = "mod-a",
            Name = "Mod A",
            Description = "A mod with publisher tabs.",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
        };
    }
}
