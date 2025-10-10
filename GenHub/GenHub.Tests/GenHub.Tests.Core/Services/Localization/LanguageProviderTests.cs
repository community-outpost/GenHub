using System.Globalization;
using GenHub.Core.Services.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Services.Localization;

/// <summary>
/// Tests for <see cref="LanguageProvider"/>.
/// </summary>
public class LanguageProviderTests
{
    private readonly Mock<ILogger<LanguageProvider>> _mockLogger;
    private readonly LanguageProvider _provider;

    public LanguageProviderTests()
    {
        _mockLogger = new Mock<ILogger<LanguageProvider>>();
        _provider = new LanguageProvider(_mockLogger.Object);
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new LanguageProvider(null!));
    }

    [Fact]
    public async Task DiscoverAvailableLanguages_ReturnsAtLeastEnglish()
    {
        // Act
        var result = await _provider.DiscoverAvailableLanguages();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains(result, c => c.TwoLetterISOLanguageName == "en");
    }

    [Fact]
    public async Task DiscoverAvailableLanguages_CachesResults()
    {
        // Act
        var result1 = await _provider.DiscoverAvailableLanguages();
        var result2 = await _provider.DiscoverAvailableLanguages();

        // Assert
        Assert.Same(result1, result2);
    }

    [Fact]
    public async Task DiscoverAvailableLanguages_ReturnsDistinctCultures()
    {
        // Act
        var result = await _provider.DiscoverAvailableLanguages();

        // Assert
        var languageCodes = result.Select(c => c.TwoLetterISOLanguageName).ToList();
        var distinctCodes = languageCodes.Distinct().ToList();
        Assert.Equal(distinctCodes.Count, languageCodes.Count);
    }

    [Fact]
    public async Task DiscoverAvailableLanguages_SortsResultsByEnglishName()
    {
        // Act
        var result = await _provider.DiscoverAvailableLanguages();

        // Assert
        var englishNames = result.Select(c => c.EnglishName).ToList();
        var sortedNames = englishNames.OrderBy(n => n).ToList();
        Assert.Equal(sortedNames, englishNames);
    }

    [Fact]
    public void GetResourceManager_WithValidBaseName_ReturnsResourceManager()
    {
        // Arrange
        var baseName = "GenHub.Core.Resources.Strings";

        // Act
        var result = _provider.GetResourceManager(baseName);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetResourceManager_WithSameBaseName_ReturnsSameInstance()
    {
        // Arrange
        var baseName = "GenHub.Core.Resources.Strings";

        // Act
        var result1 = _provider.GetResourceManager(baseName);
        var result2 = _provider.GetResourceManager(baseName);

        // Assert
        Assert.Same(result1, result2);
    }

    [Fact]
    public void GetResourceManager_WithNullBaseName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _provider.GetResourceManager(null!));
    }

    [Fact]
    public void GetResourceManager_WithEmptyBaseName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _provider.GetResourceManager(string.Empty));
    }

    [Fact]
    public void GetResourceManager_WithWhitespaceBaseName_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _provider.GetResourceManager("   "));
    }

    [Fact]
    public void ValidateCulture_WithNullCulture_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _provider.ValidateCulture(null!));
    }

    [Fact]
    public void ValidateCulture_WithEnglishCulture_ReturnsTrue()
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo("en");

        // Act
        var result = _provider.ValidateCulture(culture);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateCulture_WithEnglishUSCulture_ReturnsTrue()
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo("en-US");

        // Act
        var result = _provider.ValidateCulture(culture);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateCulture_WithInvariantCulture_ReturnsTrue()
    {
        // Arrange
        var culture = CultureInfo.InvariantCulture;

        // Act
        var result = _provider.ValidateCulture(culture);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateCulture_WithUnavailableCulture_ReturnsFalse()
    {
        // Arrange
        // Use a culture that definitely won't have a satellite assembly
        var culture = CultureInfo.GetCultureInfo("zu-ZA"); // Zulu

        // Act
        var result = _provider.ValidateCulture(culture);

        // Assert
        // This will be false unless satellite assembly exists
        Assert.False(result);
    }
}