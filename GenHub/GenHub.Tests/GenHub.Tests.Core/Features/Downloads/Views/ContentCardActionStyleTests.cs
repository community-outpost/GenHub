using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Features.Downloads.Views;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Views;

/// <summary>Checks constrained catalog action captions using the actual content card.</summary>
public class ContentCardActionStyleTests
{
    /// <summary>Long translated labels grow within the card instead of escaping their action target.</summary>
    /// <param name="buttonName">The action to render.</param>
    [AvaloniaTheory]
    [InlineData("DownloadButton")]
    [InlineData("UpdateButton")]
    [InlineData("AddToProfileButton")]
    public void CardAction_LongCaptionWrapsWithinItsSharedTarget(string buttonName)
    {
        var view = new ContentCardView();
        var button = view.FindControl<Button>(buttonName)!;
        button.IsVisible = true;
        button.IsEnabled = true;
        var content = Assert.IsType<Grid>(button.Content);
        var caption = Assert.Single(content.Children.OfType<TextBlock>());
        caption.Text = "Установить необходимые компоненты تثبيت العناصر المطلوبة";
        var window = new Window { Width = 260, Height = 500, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(32, button.MinHeight);
            Assert.True(button.Bounds.Width <= 245);
            Assert.True(button.Bounds.Height > 32);
            Assert.True(caption.Bounds.Height > 20);
            Assert.True(caption.Bounds.Width <= button.Bounds.Width - button.Padding.Left - button.Padding.Right);
            var glyph = Assert.Single(button.GetVisualDescendants().OfType<PathIcon>());
            Assert.Equal(16, glyph.Width);
            Assert.Equal(16, glyph.Height);
        }
        finally
        {
            window.Close();
        }
    }
}
