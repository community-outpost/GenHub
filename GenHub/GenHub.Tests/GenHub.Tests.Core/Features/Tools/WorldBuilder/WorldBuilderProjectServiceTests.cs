using FluentAssertions;
using GenHub.Core.Models.Tools.WorldBuilder;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Service tests for <see cref="WorldBuilderProjectService"/>.
/// </summary>
public sealed class WorldBuilderProjectServiceTests
{
    private readonly WorldBuilderProjectService sut = new(NullLogger<WorldBuilderProjectService>.Instance);

    /// <summary>
    /// Tests that projects create, reopen, and import game-data maps.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Project_CreateOpenImport_WorksAsync()
    {
        // Arrange
        var folder = TempFolder();
        var source = TempFolder();
        await File.WriteAllTextAsync(Path.Combine(source, "a.map"), "map");
        await File.WriteAllTextAsync(Path.Combine(source, "a.ini"), "ini");
        await File.WriteAllTextAsync(Path.Combine(source, "readme.txt"), "skip");

        // Act
        var created = await sut.CreateAsync(folder, "MyProject", "game-1");
        var imported = await sut.ImportFromFolderAsync(created.Data!, source);
        var opened = await sut.OpenAsync(folder);

        // Assert
        created.Success.Should().BeTrue();
        created.Data!.Name.Should().Be("MyProject");
        imported.Success.Should().BeTrue();
        imported.Data.Should().BeEquivalentTo(["a.ini", "a.map"]);
        File.Exists(Path.Combine(folder, "a.map")).Should().BeTrue();
        opened.Success.Should().BeTrue();
        opened.Data!.MapFiles.Should().Contain("a.map");
    }

    /// <summary>
    /// Tests that opening a folder without a manifest fails cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Open_MissingManifest_ReturnsFailureAsync()
    {
        // Arrange
        var folder = TempFolder();

        // Act
        var opened = await sut.OpenAsync(folder);

        // Assert
        opened.Success.Should().BeFalse();
    }

    private static string TempFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"wbproj_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }
}
