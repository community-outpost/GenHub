using GenHub.Core.Models.Enums;
using GenHub.Features.Content.Services.CommunityOutpost.Models;
using Xunit;

using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.CommunityOutpost;

/// <summary>
/// Tests for <see cref="GenPatcherDependencyBuilder"/>.
/// </summary>
public class GenPatcherDependencyBuilderTests
{
    /// <summary>
    /// Verifies that GenTool content has Zero Hour 1.04 dependency.
    /// </summary>
    [Fact]
    public void GetDependencies_GenTool_RequiresZeroHour104()
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata("gent");

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies("gent", metadata);

        // Assert
        Assert.NotEmpty(dependencies);
        var gameInstallDep = dependencies.Find(d => d.DependencyType == ContentType.GameInstallation);
        Assert.NotNull(gameInstallDep);
        Assert.Equal("1.04", gameInstallDep.MinVersion);
        Assert.Contains(GameType.ZeroHour, gameInstallDep.CompatibleGameTypes);
    }

    /// <summary>
    /// Verifies that control bar addons have Zero Hour 1.04 dependency.
    /// </summary>
    /// <param name="contentCode">The control bar content code.</param>
    [Theory]
    [InlineData("cbbs")]
    [InlineData("cben")]
    [InlineData("cbpc")]
    [InlineData("cbpr")]
    [InlineData("cbpx")]
    public void GetDependencies_ControlBars_RequiresZeroHour104(string contentCode)
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies(contentCode, metadata);

        // Assert
        Assert.NotEmpty(dependencies);
        var gameInstallDep = dependencies.Find(d => d.DependencyType == ContentType.GameInstallation);
        Assert.NotNull(gameInstallDep);
        Assert.Equal("1.04", gameInstallDep.MinVersion);
    }

    /// <summary>
    /// Verifies that camera mods have correct game dependency based on target game.
    /// </summary>
    /// <param name="contentCode">The camera content code.</param>
    /// <param name="expectedVersion">The expected version requirement.</param>
    /// <param name="expectedGame">The expected target game.</param>
    [Theory]
    [InlineData("crzh", "1.04", GameType.ZeroHour)]
    [InlineData("crgn", "1.08", GameType.Generals)]
    [InlineData("dczh", "1.04", GameType.ZeroHour)]
    public void GetDependencies_CameraMods_RequiresCorrectGame(
        string contentCode,
        string expectedVersion,
        GameType expectedGame)
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies(contentCode, metadata);

        // Assert
        Assert.NotEmpty(dependencies);
        var gameInstallDep = dependencies.Find(d => d.DependencyType == ContentType.GameInstallation);
        Assert.NotNull(gameInstallDep);
        Assert.Equal(expectedVersion, gameInstallDep.MinVersion);
        Assert.Contains(expectedGame, gameInstallDep.CompatibleGameTypes);
    }

    /// <summary>
    /// Verifies that official patches have base game dependency.
    /// </summary>
    /// <param name="contentCode">The patch content code.</param>
    /// <param name="expectedGame">The expected target game.</param>
    [Theory]
    [InlineData("108e", GameType.Generals)]
    [InlineData("104e", GameType.ZeroHour)]
    [InlineData("108d", GameType.Generals)]
    [InlineData("104b", GameType.ZeroHour)]
    public void GetDependencies_OfficialPatches_RequiresBaseGame(
        string contentCode,
        GameType expectedGame)
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies(contentCode, metadata);

        // Assert
        Assert.NotEmpty(dependencies);
        var gameInstallDep = dependencies.Find(d => d.DependencyType == ContentType.GameInstallation);
        Assert.NotNull(gameInstallDep);
        Assert.Contains(expectedGame, gameInstallDep.CompatibleGameTypes);
    }

    /// <summary>
    /// Verifies that base game content (10gn, 10zh) has no dependencies.
    /// </summary>
    /// <param name="contentCode">The base game content code.</param>
    [Theory]
    [InlineData("10gn")]
    [InlineData("10zh")]
    public void GetDependencies_BaseGame_HasNoDependencies(string contentCode)
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies(contentCode, metadata);

        // Assert
        Assert.Empty(dependencies);
    }

    /// <summary>
    /// Verifies that prerequisite content (VC++ redistributables) has no in-game dependencies.
    /// </summary>
    /// <param name="contentCode">The prerequisite content code.</param>
    [Theory]
    [InlineData("vc05")]
    [InlineData("vc08")]
    [InlineData("vc10")]
    public void GetDependencies_Prerequisites_HasNoDependencies(string contentCode)
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies(contentCode, metadata);

        // Assert
        Assert.Empty(dependencies);
    }

    /// <summary>
    /// Verifies that hotkey addons require Zero Hour 1.04.
    /// </summary>
    /// <param name="contentCode">The hotkey content code.</param>
    [Theory]
    [InlineData("hlen")]
    [InlineData("hlde")]
    [InlineData("ewba")]
    [InlineData("ewbi")]
    public void GetDependencies_Hotkeys_RequiresZeroHour104(string contentCode)
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies(contentCode, metadata);

        // Assert
        Assert.NotEmpty(dependencies);
        var gameInstallDep = dependencies.Find(d => d.DependencyType == ContentType.GameInstallation);
        Assert.NotNull(gameInstallDep);
        Assert.Equal("1.04", gameInstallDep.MinVersion);
    }

    /// <summary>
    /// Verifies that control bars are marked as exclusive (conflict with each other).
    /// </summary>
    [Fact]
    public void IsCategoryExclusive_ControlBar_ReturnsTrue()
    {
        // Act
        var isExclusive = GenPatcherDependencyBuilder.IsCategoryExclusive(GenPatcherContentCategory.ControlBar);

        // Assert
        Assert.True(isExclusive);
    }

    /// <summary>
    /// Verifies that hotkeys are marked as exclusive.
    /// </summary>
    [Fact]
    public void IsCategoryExclusive_Hotkeys_ReturnsTrue()
    {
        // Act
        var isExclusive = GenPatcherDependencyBuilder.IsCategoryExclusive(GenPatcherContentCategory.Hotkeys);

        // Assert
        Assert.True(isExclusive);
    }

    /// <summary>
    /// Verifies that camera mods are marked as exclusive.
    /// </summary>
    [Fact]
    public void IsCategoryExclusive_Camera_ReturnsTrue()
    {
        // Act
        var isExclusive = GenPatcherDependencyBuilder.IsCategoryExclusive(GenPatcherContentCategory.Camera);

        // Assert
        Assert.True(isExclusive);
    }

    /// <summary>
    /// Verifies that tools are not marked as exclusive.
    /// </summary>
    [Fact]
    public void IsCategoryExclusive_Tools_ReturnsFalse()
    {
        // Act
        var isExclusive = GenPatcherDependencyBuilder.IsCategoryExclusive(GenPatcherContentCategory.Tools);

        // Assert
        Assert.False(isExclusive);
    }

    /// <summary>
    /// Verifies that control bars return conflicting codes for other control bars.
    /// </summary>
    [Fact]
    public void GetConflictingCodes_ControlBar_ReturnsOtherControlBars()
    {
        // Act
        var conflicts = GenPatcherDependencyBuilder.GetConflictingCodes("cbbs");

        // Assert
        Assert.NotEmpty(conflicts);
        Assert.DoesNotContain("cbbs", conflicts); // Should not conflict with itself
        Assert.Contains("cben", conflicts);
        Assert.Contains("cbpc", conflicts);
        Assert.Contains("cbpr", conflicts);
        Assert.Contains("cbpx", conflicts);
    }

    /// <summary>
    /// Verifies that hotkeys return conflicting codes for other hotkey configs.
    /// </summary>
    [Fact]
    public void GetConflictingCodes_Hotkeys_ReturnsOtherHotkeys()
    {
        // Act
        var conflicts = GenPatcherDependencyBuilder.GetConflictingCodes("hlen");

        // Assert
        Assert.NotEmpty(conflicts);
        Assert.DoesNotContain("hlen", conflicts); // Should not conflict with itself
        Assert.Contains("hlde", conflicts);
        Assert.Contains("ewba", conflicts);
    }

    /// <summary>
    /// Verifies that Zero Hour camera mods return conflicting codes for other ZH cameras.
    /// </summary>
    [Fact]
    public void GetConflictingCodes_ZeroHourCamera_ReturnsOtherZHCameras()
    {
        // Act
        var conflicts = GenPatcherDependencyBuilder.GetConflictingCodes("crzh");

        // Assert
        Assert.NotEmpty(conflicts);
        Assert.DoesNotContain("crzh", conflicts); // Should not conflict with itself
        Assert.Contains("dczh", conflicts);
    }

    /// <summary>
    /// Verifies that tools have no conflicting codes.
    /// </summary>
    [Fact]
    public void GetConflictingCodes_Tools_ReturnsEmpty()
    {
        // Act
        var conflicts = GenPatcherDependencyBuilder.GetConflictingCodes("gent");

        // Assert
        Assert.Empty(conflicts);
    }

    /// <summary>
    /// Verifies that CreateZeroHour104Dependency returns correct dependency.
    /// </summary>
    [Fact]
    public void CreateZeroHour104Dependency_ReturnsCorrectDependency()
    {
        // Act
        var dependency = GenPatcherDependencyBuilder.CreateZeroHour104Dependency();

        // Assert
        Assert.Equal("1.04", dependency.MinVersion);
        Assert.Equal(ContentType.GameInstallation, dependency.DependencyType);
        Assert.Equal(DependencyInstallBehavior.RequireExisting, dependency.InstallBehavior);
        Assert.False(dependency.IsOptional);
        Assert.Contains(GameType.ZeroHour, dependency.CompatibleGameTypes);
    }

    /// <summary>
    /// Verifies that CreateGenerals108Dependency returns correct dependency.
    /// </summary>
    [Fact]
    public void CreateGenerals108Dependency_ReturnsCorrectDependency()
    {
        // Act
        var dependency = GenPatcherDependencyBuilder.CreateGenerals108Dependency();

        // Assert
        Assert.Equal("1.08", dependency.MinVersion);
        Assert.Equal(ContentType.GameInstallation, dependency.DependencyType);
        Assert.Equal(DependencyInstallBehavior.RequireExisting, dependency.InstallBehavior);
        Assert.False(dependency.IsOptional);
        Assert.Contains(GameType.Generals, dependency.CompatibleGameTypes);
    }

    /// <summary>
    /// Verifies that CreateGenToolDependency returns correct dependency.
    /// </summary>
    [Fact]
    public void CreateGenToolDependency_ReturnsCorrectDependency()
    {
        // Act
        var dependency = GenPatcherDependencyBuilder.CreateGenToolDependency();

        // Assert
        Assert.Equal(ContentType.Addon, dependency.DependencyType);
        Assert.Equal(DependencyInstallBehavior.AutoInstall, dependency.InstallBehavior);
        Assert.False(dependency.IsOptional);

        // ID format: 1.0.communityoutpost.addon.gent (using 4-char content code)
        Assert.Contains("gent", dependency.Id.ToString());
    }

    /// <summary>
    /// Verifies that CreateOptionalGenToolDependency returns optional dependency.
    /// </summary>
    [Fact]
    public void CreateOptionalGenToolDependency_ReturnsOptionalDependency()
    {
        // Act
        var dependency = GenPatcherDependencyBuilder.CreateOptionalGenToolDependency();

        // Assert
        Assert.Equal(ContentType.Addon, dependency.DependencyType);
        Assert.Equal(DependencyInstallBehavior.Suggest, dependency.InstallBehavior);
        Assert.True(dependency.IsOptional);

        // ID format: 1.0.communityoutpost.addon.gent (using 4-char content code)
        Assert.Contains("gent", dependency.Id.ToString());
    }

    /// <summary>
    /// Verifies that visual addons require correct game installation.
    /// </summary>
    /// <param name="contentCode">The visual content code.</param>
    [Theory]
    [InlineData("icon")]
    [InlineData("drtx")]
    [InlineData("unct")]
    public void GetDependencies_Visuals_RequiresGameInstallation(string contentCode)
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata(contentCode);

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies(contentCode, metadata);

        // Assert
        Assert.NotEmpty(dependencies);
        var gameInstallDep = dependencies.Find(d => d.DependencyType == ContentType.GameInstallation);
        Assert.NotNull(gameInstallDep);
    }

    /// <summary>
    /// Verifies that map packs require Zero Hour 1.04.
    /// </summary>
    [Fact]
    public void GetDependencies_Maps_RequiresZeroHour104()
    {
        // Arrange
        var metadata = GenPatcherContentRegistry.GetMetadata("maod");

        // Act
        var dependencies = GenPatcherDependencyBuilder.GetDependencies("maod", metadata);

        // Assert
        Assert.NotEmpty(dependencies);
        var gameInstallDep = dependencies.Find(d => d.DependencyType == ContentType.GameInstallation);
        Assert.NotNull(gameInstallDep);
        Assert.Equal("1.04", gameInstallDep.MinVersion);
    }
}
