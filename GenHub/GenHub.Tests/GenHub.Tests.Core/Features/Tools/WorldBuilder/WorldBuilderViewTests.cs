using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using GenHub.Features.Tools.WorldBuilder.Controls;
using GenHub.Features.Tools.WorldBuilder.ViewModels;
using GenHub.Features.Tools.WorldBuilder.Views;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Headless smoke tests proving the WorldBuilder view loads, lays out, and binds.
/// </summary>
public sealed class WorldBuilderViewTests
{
    /// <summary>
    /// Tests that the view attaches its canvas and 3D viewport controls.
    /// </summary>
    [AvaloniaFact]
    public void View_AttachesCanvasAndViewport()
    {
        // Arrange
        var view = new WorldBuilderView();
        var window = new Window { Content = view };

        try
        {
            // Act
            window.Show();

            // Assert
            view.FindControl<MapCanvasControl>("MapCanvas").Should().NotBeNull();
            view.FindControl<WbGlViewport>("GlViewport").Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>
    /// Tests that the view binds a composed view model without throwing.
    /// </summary>
    [AvaloniaFact]
    public void View_BindsComposedViewModel()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .ConfigureApplicationServices()
            .BuildServiceProvider();
        var view = new WorldBuilderView
        {
            DataContext = provider.GetRequiredService<WorldBuilderViewModel>(),
        };
        var window = new Window { Content = view };

        try
        {
            // Act
            window.Show();

            // Assert
            view.DataContext.Should().BeOfType<WorldBuilderViewModel>();
        }
        finally
        {
            window.Close();
        }
    }
}
