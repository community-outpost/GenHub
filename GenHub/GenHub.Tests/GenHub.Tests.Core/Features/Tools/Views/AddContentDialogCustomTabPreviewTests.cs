using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Features.Downloads.Views;
using GenHub.Features.Tools.ViewModels.Dialogs;
using GenHub.Features.Tools.Views.Dialogs;
using System.Linq;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Tools.Views;

/// <summary>
/// Headless tests for the custom tab preview in the Publisher Studio content dialog.
/// </summary>
public class AddContentDialogCustomTabPreviewTests
{
    /// <summary>
    /// A long preview grows inside the dialog form, and the form's own scroller scrolls past it.
    /// </summary>
    [AvaloniaFact]
    public void LongPreview_ScrollsInDialogForm()
    {
        var item = new CatalogContentItem
        {
            Id = "mod-a",
            Name = "Mod A",
            Description = "A mod with a long publisher tab.",
            ContentType = ContentType.Mod,
            TargetGame = GameType.ZeroHour,
            CustomTabs =
            [
                new CatalogTabDefinition
                {
                    TabId = "long",
                    Header = "Long",
                    Intro = string.Join(" ", Enumerable.Repeat("Long intro text.", 400)),
                    Cards = [.. Enumerable.Range(0, 24).Select(i => new CatalogTabCardDefinition { Title = $"Card {i}" })],
                },
            ],
        };
        using var viewModel = new AddContentDialogViewModel(item, _ => { });
        viewModel.CustomTabs[0].IsPreviewVisible = true;
        var view = new AddContentDialogView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 680, Height = 720 };
        window.Show();
        try
        {
            foreach (var expander in view.GetVisualDescendants().OfType<Expander>())
            {
                expander.IsExpanded = true;
            }

            window.UpdateLayout();

            var preview = Assert.Single(view.GetVisualDescendants().OfType<CustomTabContentView>());
            var form = preview.FindAncestorOfType<ScrollViewer>();
            Assert.NotNull(form);
            Assert.Null(form.FindAncestorOfType<ScrollViewer>());
            Assert.True(preview.Bounds.Height > form.Viewport.Height, $"Preview {preview.Bounds.Height} should be taller than the form viewport {form.Viewport.Height}");
            Assert.True(form.Extent.Height > preview.Bounds.Height);

            form.Offset = new Avalonia.Vector(0, form.Extent.Height - form.Viewport.Height);
            window.UpdateLayout();
            Assert.Equal(form.Extent.Height - form.Viewport.Height, form.Offset.Y, 1);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// The tab order input is wide enough to show a four digit value without clipping.
    /// </summary>
    [AvaloniaFact]
    public void OrderInput_FitsFourDigits()
    {
        var item = new CatalogContentItem
        {
            Id = "mod-a",
            Name = "Mod A",
            Description = "A mod with a publisher tab.",
            CustomTabs = [new CatalogTabDefinition { TabId = "notes", Header = "Notes", Order = 1000 }],
        };
        using var viewModel = new AddContentDialogViewModel(item, _ => { });
        var view = new AddContentDialogView { DataContext = viewModel };
        var window = new Window { Content = view, Width = 680, Height = 720 };
        window.Show();
        try
        {
            foreach (var expander in view.GetVisualDescendants().OfType<Expander>())
            {
                expander.IsExpanded = true;
            }

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
}
