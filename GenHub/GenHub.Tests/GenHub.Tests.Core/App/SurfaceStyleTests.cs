using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Features.GameProfiles.Views;
using GenHub.Features.Info.ViewModels;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.App;

/// <summary>Checks readable profile surfaces and bounded identity artwork.</summary>
public class SurfaceStyleTests
{
    /// <summary>Changing profile identity colors cannot wash out the form's reading surface.</summary>
    /// <param name="accent">A profile color, including bright and dark extremes.</param>
    [AvaloniaTheory]
    [InlineData("#FFFFFF")]
    [InlineData("#FFFF00")]
    [InlineData("#000000")]
    [InlineData("#9C27B0")]
    public void ProfileSections_KeepOpaqueNeutralSurfaceAndAccentIdentity(string accent)
    {
        var viewModel = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab();
        var view = new GameProfileSettingsContentView { DataContext = viewModel };
        var window = new Window { Width = 1100, Height = 800, Content = view };
        window.Show();
        try
        {
            viewModel.ColorValue = accent;
            Dispatcher.UIThread.RunJobs();
            var sections = view.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("section-card")).ToArray();
            Assert.NotEmpty(sections);
            var expected = Assert.IsAssignableFrom<ISolidColorBrush>(Avalonia.Application.Current!.FindResource("CardBackground")).Color;
            Assert.All(sections, section =>
            {
                var fill = Assert.IsAssignableFrom<ISolidColorBrush>(section.Background).Color;
                Assert.Equal((byte)255, fill.A);
                Assert.Equal(expected, fill);
                Assert.Equal(new CornerRadius(12), section.CornerRadius);
            });
            Assert.All(sections.SelectMany(section => section.GetVisualDescendants().OfType<TextBlock>()).Where(label => label.Classes.Contains("form-label")), label => Assert.Equal(1, label.Opacity));
            var header = view.FindControl<Border>("HeaderBorder")!;
            Assert.Equal(1, header.Opacity);
            var artwork = view.FindControl<Image>("ProfileHeaderArtwork")!;
            Assert.True(artwork.Bounds.Height <= 70);
            Assert.InRange(artwork.Opacity, 0.1, 0.25);
            Assert.Equal(Color.Parse(accent), Assert.IsAssignableFrom<ISolidColorBrush>(header.BorderBrush).Color);
            Assert.Equal(new Thickness(4, 0, 0, 0), header.BorderThickness);
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>A long editable description stays a short header preview without hiding the form.</summary>
    [AvaloniaFact]
    public void ProfileHeader_LongDescriptionLeavesFormVisible()
    {
        var viewModel = DemoViewModelFactory.CreateDemoProfileSettingsViewModel_ProfileTab();
        viewModel.Description = string.Join("\n", Enumerable.Repeat("A long profile description line", 80));
        var view = new GameProfileSettingsContentView { DataContext = viewModel };
        var window = new Window { Width = 1400, Height = 900, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var header = view.FindControl<Border>("HeaderBorder")!;
            Assert.InRange(header.Bounds.Height, 70, 100);
            var section = view.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("section-card"));
            var scroll = section.GetVisualAncestors().OfType<ScrollViewer>().First();
            Assert.True(scroll.Bounds.Height > 400);
            Assert.Equal(viewModel.Description, Assert.Single(section.GetVisualDescendants().OfType<TextBox>(), field => field.AcceptsReturn).Text);
        }
        finally
        {
            window.Close();
        }
    }
}
