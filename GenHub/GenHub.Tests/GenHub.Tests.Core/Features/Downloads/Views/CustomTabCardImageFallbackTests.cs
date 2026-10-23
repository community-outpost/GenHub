using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using GenHub.Core.Models.Content;
using GenHub.Features.Downloads.Views;
using GenHub.Infrastructure.Controls;
using GenHub.Infrastructure.Markdown;
using GenHub.Infrastructure.Services;
using Moq;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Downloads.Views;

/// <summary>
/// Headless tests for the card image fallback in <see cref="CustomTabContentView"/>.
/// </summary>
public class CustomTabCardImageFallbackTests
{
    /// <summary>
    /// A card whose image cannot load shows the image URL as a link that opens through the safe link command.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CardImage_LoadFails_ShowsSafeLinkInsteadOfEmptyBoxAsync()
    {
        const string url = "https://127.0.0.1/blocked.png";
        var view = new CustomTabContentView
        {
            DataContext = new CustomTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CustomTabCardDefinition { Title = "Card", ImageUrl = url }],
            },
        };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var image = view.GetLogicalDescendants().OfType<Image>().Single(i => i.Name == "CardImage");
            await WaitUntilAsync(() => ImageLoader.GetLoadFailed(image));
            window.UpdateLayout();

            var link = view.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CardImageLink");
            Assert.True(link.IsVisible);
            Assert.True(link.IsEnabled);
            Assert.True(link.Command!.CanExecute(link.CommandParameter));
            Assert.False(image.FindLogicalAncestorOfType<Button>()!.IsVisible);
            Assert.IsType<SafeMarkdownHyperlinkCommand>(link.Command);
            Assert.Equal(url, link.CommandParameter);
            Assert.Contains(link.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == url);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// A bundled image that fails to load shows no link, because a browser cannot open it.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CardImage_BundledImageFails_ShowsNoDeadLinkAsync()
    {
        var view = new CustomTabContentView
        {
            DataContext = new CustomTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CustomTabCardDefinition { Title = "Card", ImageUrl = "avares://GenHub/Assets/Covers/missing-cover.jpg" }],
            },
        };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var image = view.GetLogicalDescendants().OfType<Image>().Single(i => i.Name == "CardImage");
            await WaitUntilAsync(() => ImageLoader.GetLoadFailed(image));
            window.UpdateLayout();

            Assert.False(view.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CardImageLink").IsVisible);
            Assert.False(image.FindLogicalAncestorOfType<Button>()!.IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// An image load that throws marks the image as failed, so the card shows its link.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CardImage_LoadThrows_ShowsLinkAsync()
    {
        const string url = "https://example.com/throws.png";
        var previous = ImageCacheService.Instance;
        var cache = new Mock<IImageCacheService>();
        cache.Setup(c => c.GetBitmapAsync(url, It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("decoder failure"));
        ImageCacheService.Instance = cache.Object;
        var view = new CustomTabContentView
        {
            DataContext = new CustomTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CustomTabCardDefinition { Title = "Card", ImageUrl = url }],
            },
        };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var image = view.GetLogicalDescendants().OfType<Image>().Single(i => i.Name == "CardImage");
            await WaitUntilAsync(() => ImageLoader.GetLoadFailed(image));
            window.UpdateLayout();

            Assert.True(view.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CardImageLink").IsVisible);
        }
        finally
        {
            window.Close();
            ImageCacheService.Instance = previous;
        }
    }

    /// <summary>
    /// A card whose image loads keeps the image and hides the link.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [AvaloniaFact]
    public async Task CardImage_Loads_HidesLinkAsync()
    {
        var view = new CustomTabContentView
        {
            DataContext = new CustomTabDefinition
            {
                TabId = "gallery",
                Header = "Gallery",
                Cards = [new CustomTabCardDefinition { Title = "Card", ImageUrl = "avares://GenHub/Assets/Covers/usa-cover.jpg" }],
            },
        };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();
        try
        {
            var image = view.GetLogicalDescendants().OfType<Image>().Single(i => i.Name == "CardImage");
            await WaitUntilAsync(() => image.Source != null);
            window.UpdateLayout();

            Assert.False(ImageLoader.GetLoadFailed(image));
            Assert.False(view.GetLogicalDescendants().OfType<Button>().Single(b => b.Name == "CardImageLink").IsVisible);
        }
        finally
        {
            window.Close();
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), "Timed out waiting for the image load to finish.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(10);
        }
    }
}
