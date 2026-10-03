using FluentAssertions;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools;
using GenHub.Features.Tools.WorldBuilder.ViewModels;
using GenHub.Infrastructure.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Verifies the WorldBuilder DI module composes inside the full application graph.
/// </summary>
public sealed class WorldBuilderCompositionTests
{
    /// <summary>
    /// Tests that the full application composition resolves the WorldBuilder view model.
    /// </summary>
    [Fact]
    public void FullComposition_ResolvesWorldBuilderViewModel()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .ConfigureApplicationServices()
            .BuildServiceProvider();

        // Act
        var viewModel = provider.GetRequiredService<WorldBuilderViewModel>();

        // Assert
        viewModel.Should().NotBeNull();
    }

    /// <summary>
    /// Tests that the full application composition registers the WorldBuilder tool plugin.
    /// </summary>
    [Fact]
    public void FullComposition_RegistersWorldBuilderPlugin()
    {
        // Arrange
        using var provider = new ServiceCollection()
            .ConfigureApplicationServices()
            .BuildServiceProvider();

        // Act
        var plugins = provider.GetServices<IToolPlugin>();

        // Assert
        plugins.Should().ContainSingle(plugin => plugin.Metadata.Id == ToolConstants.WorldBuilder.Id);
    }
}
