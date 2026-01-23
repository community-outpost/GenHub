using System;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameProfile;
using GenHub.Core.Models.GameSettings;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for the <see cref="GameSettingsMapper"/> class.
/// </summary>
public class GameSettingsMapperTests
{
    /// <summary>
    /// Verifies that all texture quality levels map to the correct engine values.
    /// </summary>
    /// <param name="quality">The texture quality level.</param>
    /// <param name="expectedReduction">The expected texture reduction value in Options.ini.</param>
    [Theory]
    [InlineData(TextureQuality.Low, GameSettingsConstants.TextureQuality.TextureReductionLow)]
    [InlineData(TextureQuality.Medium, GameSettingsConstants.TextureQuality.TextureReductionMedium)]
    [InlineData(TextureQuality.High, GameSettingsConstants.TextureQuality.TextureReductionHigh)]
    [InlineData(TextureQuality.VeryHigh, GameSettingsConstants.TextureQuality.TextureReductionHigh)]
    public void ApplyToOptions_AllTextureQualities_SetsCorrectReduction(TextureQuality quality, int expectedReduction)
    {
        // Arrange
        var profile = new GameProfile
        {
            VideoTextureQuality = quality,
        };
        var options = new IniOptions();

        // Act
        GameSettingsMapper.ApplyToOptions(profile, options);

        // Assert
        Assert.Equal(expectedReduction, options.Video.TextureReduction);
    }

    /// <summary>
    /// Verifies that mapping from engine values correctly results in the expected texture quality.
    /// </summary>
    /// <param name="reduction">The texture reduction value from Options.ini.</param>
    /// <param name="expectedQuality">The expected texture quality level.</param>
    [Theory]
    [InlineData(GameSettingsConstants.TextureQuality.TextureReductionLow, TextureQuality.Low)]
    [InlineData(GameSettingsConstants.TextureQuality.TextureReductionMedium, TextureQuality.Medium)]
    [InlineData(GameSettingsConstants.TextureQuality.TextureReductionHigh, TextureQuality.High)]
    public void ApplyFromOptions_AllReductions_MapsToCorrectQuality(int reduction, TextureQuality expectedQuality)
    {
        // Arrange
        var options = new IniOptions();
        options.Video.TextureReduction = reduction;
        var profile = new GameProfile();

        // Act
        GameSettingsMapper.ApplyFromOptions(options, profile);

        // Assert
        Assert.Equal(expectedQuality, profile.VideoTextureQuality);
    }
}


