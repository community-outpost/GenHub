using System.Linq;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Theming;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests for <see cref="ThemeService"/> and application theming constants.
/// </summary>
public class ThemeServiceTests
{
    private readonly Mock<IConfigurationProviderService> _mockConfigProvider;
    private readonly ThemeService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThemeServiceTests"/> class.
    /// </summary>
    public ThemeServiceTests()
    {
        _mockConfigProvider = new Mock<IConfigurationProviderService>();
        _mockConfigProvider.Setup(s => s.GetTheme()).Returns("Purple");

        _service = new ThemeService(_mockConfigProvider.Object, NullLogger<ThemeService>.Instance);
    }

    /// <summary>
    /// Verifies that all expected built-in themes are present.
    /// </summary>
    [Fact]
    public void AvailableThemes_ContainsAllExpectedPalettes()
    {
        // Assert
        Assert.NotNull(_service.AvailableThemes);
        Assert.True(_service.AvailableThemes.Count >= 8);

        var themeIds = _service.AvailableThemes.Select(t => t.Id).ToList();
        Assert.Contains("Purple", themeIds);
        Assert.Contains("Generals", themeIds);
        Assert.Contains("ZeroHour", themeIds);
        Assert.Contains("Emerald", themeIds);
        Assert.Contains("Crimson", themeIds);
        Assert.Contains("Amber", themeIds);
        Assert.Contains("Cobalt", themeIds);
        Assert.Contains("Rose", themeIds);
    }

    /// <summary>
    /// Verifies that default theme is Void Purple.
    /// </summary>
    [Fact]
    public void CurrentTheme_Initially_ReturnsDefaultTheme()
    {
        // Assert
        Assert.Equal(ThemeConstants.DefaultTheme.Id, _service.CurrentTheme.Id);
        Assert.Equal("#A855F7", _service.CurrentTheme.PrimaryHex);
    }

    /// <summary>
    /// Verifies that applying a theme by ID updates CurrentTheme.
    /// </summary>
    [Fact]
    public void ApplyTheme_ById_UpdatesCurrentTheme()
    {
        // Act
        _service.ApplyTheme("Generals");

        // Assert
        Assert.Equal("Generals", _service.CurrentTheme.Id);
        Assert.Equal("Generals Orange", _service.CurrentTheme.DisplayName);
    }

    /// <summary>
    /// Verifies that applying an invalid theme falls back to default.
    /// </summary>
    [Fact]
    public void ApplyTheme_InvalidTheme_FallsBackToDefault()
    {
        // Act
        _service.ApplyTheme("NonExistentTheme");

        // Assert
        Assert.Equal(ThemeConstants.DefaultTheme.Id, _service.CurrentTheme.Id);
    }

    /// <summary>
    /// Verifies that InitializeTheme restores saved theme from settings.
    /// </summary>
    [Fact]
    public void InitializeTheme_RestoresSavedTheme()
    {
        // Arrange
        _mockConfigProvider.Setup(s => s.GetTheme()).Returns("Emerald");

        // Act
        _service.InitializeTheme();

        // Assert
        Assert.Equal("Emerald", _service.CurrentTheme.Id);
        Assert.Equal("Emerald Green", _service.CurrentTheme.DisplayName);
    }
}
