using FluentAssertions;
using GenHub.Core.Services.Tools.WorldBuilder;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Service tests for <see cref="WorldBuilderSidecarService"/> discovery.
/// </summary>
public sealed class WorldBuilderSidecarServiceTests
{
    private readonly WorldBuilderSidecarService sut = new([], NullLogger<WorldBuilderSidecarService>.Instance);

    /// <summary>
    /// Tests that the native executable is found by name.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindNative_ExistingExe_ReturnsPathAsync()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), $"wbside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var exe = Path.Combine(folder, "WorldBuilder.exe");
        await File.WriteAllTextAsync(exe, "stub");

        // Act
        var found = await sut.FindNativeAsync(folder);

        // Assert
        found.Success.Should().BeTrue();
        found.Data.Should().Be(exe);
    }

    /// <summary>
    /// Tests that a missing executable fails cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task FindNative_MissingExe_ReturnsFailureAsync()
    {
        // Arrange
        var folder = Path.Combine(Path.GetTempPath(), $"wbside_{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);

        // Act
        var found = await sut.FindNativeAsync(folder);

        // Assert
        found.Success.Should().BeFalse();
    }

    /// <summary>
    /// Tests that launching without a runner fails cleanly.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Launch_NoRunner_ReturnsFailureAsync()
    {
        // Arrange
        var exe = Path.Combine(Path.GetTempPath(), "WorldBuilder.exe");

        // Act
        var launched = await sut.LaunchAsync(exe, null);

        // Assert
        launched.Success.Should().BeFalse();
    }
}
