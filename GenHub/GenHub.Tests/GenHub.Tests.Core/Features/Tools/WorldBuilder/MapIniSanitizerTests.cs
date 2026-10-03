using FluentAssertions;
using GenHub.Core.Services.Tools.WorldBuilder;

namespace GenHub.Tests.Core.Features.Tools.WorldBuilder;

/// <summary>
/// Unit tests for <see cref="MapIniSanitizer"/>.
/// </summary>
public sealed class MapIniSanitizerTests
{
    /// <summary>
    /// Tests that duplicate removes are neutralized while line count is preserved.
    /// </summary>
    [Fact]
    public void Sanitize_DoubleRemove_NeutralizesSecond()
    {
        // Arrange
        var text = "Object MyTank\n\tDraw = Something ModuleTag_Body\n\tRemoveModule ModuleTag_Body\n\tRemoveModule ModuleTag_Body\nEnd\n";

        // Act
        var (sanitized, warnings) = MapIniSanitizer.Sanitize(text);

        // Assert
        sanitized.Split('\n').Should().HaveCount(text.Split('\n').Length);
        sanitized.Should().Contain("; sanitized:");
        warnings.Should().ContainSingle().Which.Should().Contain("already removed");
    }

    /// <summary>
    /// Tests that unverifiable removes are kept with a warning.
    /// </summary>
    [Fact]
    public void Sanitize_UnknownRemove_KeepsWithWarning()
    {
        // Arrange
        var text = "Object MyTank\n\tRemoveModule ModuleTag_NoSuch\nEnd\n";

        // Act
        var (sanitized, warnings) = MapIniSanitizer.Sanitize(text);

        // Assert
        sanitized.Should().Contain("RemoveModule ModuleTag_NoSuch");
        warnings.Should().ContainSingle().Which.Should().Contain("unverifiable");
    }

    /// <summary>
    /// Tests that clean files pass through untouched.
    /// </summary>
    [Fact]
    public void Sanitize_CleanFile_NoWarnings()
    {
        // Arrange
        var text = "; map overrides\nObject MyTank\n\tMaxHealth = 500\nEnd\n";

        // Act
        var (sanitized, warnings) = MapIniSanitizer.Sanitize(text);

        // Assert
        sanitized.Should().Be(text.Replace("\r\n", "\n"));
        warnings.Should().BeEmpty();
    }
}
