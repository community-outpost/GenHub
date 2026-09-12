using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ColorTextBlock.Avalonia;
using GenHub.Common.ViewModels.Dialogs;
using GenHub.Common.Views.Dialogs;

namespace GenHub.Tests.Core.Common.Views;

/// <summary>
/// Verifies markdown remains readable on the shared dialog's dark background.
/// </summary>
public class GenericMessageWindowTests
{
    /// <summary>
    /// Verifies the rendered markdown inherits the semantic foreground under either system theme.
    /// </summary>
    /// <param name="useDarkTheme">Whether to simulate the dark system theme.</param>
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Markdown_UsesSemanticForeground_InEitherTheme(bool useDarkTheme)
    {
        var window = new GenericMessageWindow
        {
            RequestedThemeVariant = useDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light,
            DataContext = new GenericMessageViewModel
            {
                Title = "Getting Started",
                Content = "**Welcome to GenHub!**\n\nBody paragraph\n\n* List item",
            },
        };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(window.TryFindResource("TextPrimaryBrush", out var resource));
            var expected = Assert.IsAssignableFrom<ISolidColorBrush>(resource);
            var textBlocks = window.GetVisualDescendants().OfType<CTextBlock>().ToList();
            Assert.Contains(textBlocks, text => text.Text == "Welcome to GenHub!");
            Assert.Contains(textBlocks, text => text.Text == "Body paragraph");
            Assert.Contains(textBlocks, text => text.Text == "List item");
            Assert.All(textBlocks, text =>
                Assert.Equal(expected.Color, Assert.IsAssignableFrom<ISolidColorBrush>(text.Foreground).Color));
        }
        finally
        {
            window.Close();
        }
    }
}
