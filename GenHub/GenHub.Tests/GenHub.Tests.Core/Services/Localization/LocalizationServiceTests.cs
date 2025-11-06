using System.Globalization;
using System.Reactive.Linq;
using GenHub.Core.Interfaces.Localization;
using GenHub.Core.Models.Localization;
using GenHub.Core.Services.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Services.Localization;

/// <summary>
/// Tests for <see cref="LocalizationService"/>.
/// </summary>
public class LocalizationServiceTests : IDisposable
{
    private readonly Mock<ILanguageProvider> _mockLanguageProvider;
    private readonly Mock<ILogger<LocalizationService>> _mockLogger;
    private readonly LocalizationService _service;
    private readonly LocalizationOptions _options;

    public LocalizationServiceTests()
    {
        _mockLanguageProvider = new Mock<ILanguageProvider>();
        _mockLogger = new Mock<ILogger<LocalizationService>>();
        _options = new LocalizationOptions
        {
            DefaultCulture = "en",
            FallbackCulture = "en",
            LogMissingTranslations = true,
            ThrowOnMissingTranslation = false
        };

        // Setup default behavior for language provider
        var availableCultures = new List<CultureInfo>
        {
            CultureInfo.GetCultureInfo("en"),
            CultureInfo.GetCultureInfo("de"),
            CultureInfo.GetCultureInfo("fr")
        };

        _mockLanguageProvider
            .Setup(p => p.DiscoverAvailableLanguages())
            .ReturnsAsync(availableCultures.AsReadOnly());

        _mockLanguageProvider
            .Setup(p => p.ValidateCulture(It.IsAny<CultureInfo>()))
            .Returns<CultureInfo>(c =>
                c.TwoLetterISOLanguageName == "en" ||
                c.TwoLetterISOLanguageName == "de" ||
                c.TwoLetterISOLanguageName == "fr");

        _service = new LocalizationService(_mockLanguageProvider.Object, _mockLogger.Object, _options);
    }

    public void Dispose()
    {
        _service?.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Constructor_WithNullLanguageProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new LocalizationService(null!, _mockLogger.Object, _options));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new LocalizationService(_mockLanguageProvider.Object, null!, _options));
    }

    [Fact]
    public void Constructor_WithNullOptions_UsesDefaultOptions()
    {
        // Act
        using var service = new LocalizationService(_mockLanguageProvider.Object, _mockLogger.Object, null);

        // Assert
        Assert.NotNull(service);
        Assert.NotNull(service.CurrentCulture);
    }

    [Fact]
    public void Constructor_SetsDefaultCulture()
    {
        // Assert
        Assert.Equal("en", _service.CurrentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void CurrentCulture_Get_ReturnsCurrentCulture()
    {
        // Act
        var culture = _service.CurrentCulture;

        // Assert
        Assert.NotNull(culture);
        Assert.Equal("en", culture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task CurrentCulture_Set_ChangesCurrentCulture()
    {
        // Arrange
        var germanCulture = CultureInfo.GetCultureInfo("de");

        // Act
        _service.CurrentCulture = germanCulture;
        await Task.Delay(100); // Give async operation time to complete

        // Assert
        Assert.Equal("de", _service.CurrentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void AvailableCultures_ReturnsDiscoveredCultures()
    {
        // Act
        var cultures = _service.AvailableCultures;

        // Assert
        Assert.NotNull(cultures);
        Assert.Contains(cultures, c => c.TwoLetterISOLanguageName == "en");
        Assert.Contains(cultures, c => c.TwoLetterISOLanguageName == "de");
        Assert.Contains(cultures, c => c.TwoLetterISOLanguageName == "fr");
    }

    [Fact]
    public void CultureChanged_IsObservable()
    {
        // Act & Assert
        Assert.NotNull(_service.CultureChanged);
    }

    [Fact]
    public async Task SetCulture_WithValidCulture_UpdatesCurrentCulture()
    {
        // Arrange
        var germanCulture = CultureInfo.GetCultureInfo("de");

        // Act
        await _service.SetCulture(germanCulture);

        // Assert
        Assert.Equal("de", _service.CurrentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task SetCulture_WithValidCulture_NotifiesObservers()
    {
        // Arrange
        var germanCulture = CultureInfo.GetCultureInfo("de");
        CultureInfo? notifiedCulture = null;
        using var subscription = _service.CultureChanged.Subscribe(c => notifiedCulture = c);

        // Act
        await _service.SetCulture(germanCulture);

        // Assert
        Assert.NotNull(notifiedCulture);
        Assert.Equal("de", notifiedCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task SetCulture_WithInvalidCulture_FallsBackToDefaultCulture()
    {
        // Arrange
        var invalidCulture = CultureInfo.GetCultureInfo("zu-ZA"); // Zulu

        // Act
        await _service.SetCulture(invalidCulture);

        // Assert - should fall back to English
        Assert.Equal("en", _service.CurrentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task SetCulture_WithNullCulture_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SetCulture((CultureInfo)null!));
    }

    [Fact]
    public async Task SetCulture_ByCultureName_UpdatesCurrentCulture()
    {
        // Act
        await _service.SetCulture("de");

        // Assert
        Assert.Equal("de", _service.CurrentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task SetCulture_WithNullCultureName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SetCulture((string)null!));
    }

    [Fact]
    public async Task SetCulture_WithEmptyCultureName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetCulture(string.Empty));
    }

    [Fact]
    public async Task SetCulture_WithWhitespaceCultureName_ThrowsArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SetCulture("   "));
    }

    [Fact]
    public async Task SetCulture_WithInvalidCultureName_FallsBackToDefaultCulture()
    {
        // Act
        await _service.SetCulture("invalid-culture");

        // Assert - should fall back to English since invalid culture
        Assert.Equal("en", _service.CurrentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void GetString_WithNullKey_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _service.GetString(null!));
    }

    [Fact]
    public void GetString_WithEmptyKey_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.GetString(string.Empty));
    }

    [Fact]
    public void GetString_WithWhitespaceKey_ThrowsArgumentException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => _service.GetString("   "));
    }

    [Fact]
    public void GetString_WithMissingKey_ReturnsKeyWithMarker()
    {
        // Arrange
        var key = "NonExistentKey";

        // Act
        var result = _service.GetString(key);

        // Assert - when LogMissingTranslations is true, returns [key]
        Assert.Contains(key, result);
    }

    [Fact]
    public void GetString_WithParameters_FormatsString()
    {
        // Arrange
        var key = "TestKey";
        var args = new object[] { "test", 123 };

        // Act
        var result = _service.GetString(key, args);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public void GetString_WithNullParameters_ReturnsUnformattedString()
    {
        // Arrange
        var key = "TestKey";

        // Act
        var result = _service.GetString(key, (object[])null!);

        // Assert
        Assert.NotNull(result);
    }

    [Fact]
    public async Task RefreshAvailableCultures_UpdatesAvailableCultures()
    {
        // Arrange
        var newCultures = new List<CultureInfo>
        {
            CultureInfo.GetCultureInfo("en"),
            CultureInfo.GetCultureInfo("es")
        };

        _mockLanguageProvider
            .Setup(p => p.DiscoverAvailableLanguages())
            .ReturnsAsync(newCultures.AsReadOnly());

        // Act
        await _service.RefreshAvailableCultures();

        // Assert
        var cultures = _service.AvailableCultures;
        Assert.Contains(cultures, c => c.TwoLetterISOLanguageName == "es");
    }

    [Fact]
    public async Task CultureChanged_EmitsMultipleNotifications()
    {
        // Arrange
        var notifications = new List<CultureInfo>();
        using var subscription = _service.CultureChanged.Subscribe(c => notifications.Add(c));

        // Act
        await _service.SetCulture("de");
        await _service.SetCulture("fr");
        await _service.SetCulture("en");

        // Assert
        Assert.Equal(3, notifications.Count);
        Assert.Equal("de", notifications[0].TwoLetterISOLanguageName);
        Assert.Equal("fr", notifications[1].TwoLetterISOLanguageName);
        Assert.Equal("en", notifications[2].TwoLetterISOLanguageName);
    }

    [Fact]
    public async Task SetCulture_ThreadSafe_HandlesMultipleConcurrentCalls()
    {
        // Arrange
        var cultures = new[] { "en", "de", "fr" };
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < 10; i++)
        {
            var culture = cultures[i % cultures.Length];
            tasks.Add(_service.SetCulture(culture));
        }

        await Task.WhenAll(tasks);

        // Assert
        Assert.NotNull(_service.CurrentCulture);
        Assert.Contains(_service.CurrentCulture.TwoLetterISOLanguageName, cultures);
    }

    [Fact]
    public void Dispose_CompletesObservable()
    {
        // Arrange
        bool completed = false;
        using var subscription = _service.CultureChanged.Subscribe(
            _ => { },
            () => completed = true);

        // Act
        _service.Dispose();

        // Assert
        Assert.True(completed);
    }

    [Fact]
    public void GetString_WithThrowOnMissingTranslationEnabled_ThrowsException()
    {
        // Arrange
        var options = new LocalizationOptions
        {
            ThrowOnMissingTranslation = true
        };

        using var service = new LocalizationService(_mockLanguageProvider.Object, _mockLogger.Object, options);
        var key = "NonExistentKey";

        // Act & Assert
        Assert.Throws<InvalidOperationException>(() => service.GetString(key));
    }

    [Fact]
    public void GetString_WithLogMissingTranslationsDisabled_ReturnsCleanKey()
    {
        // Arrange
        var options = new LocalizationOptions
        {
            LogMissingTranslations = false,
            ThrowOnMissingTranslation = false
        };

        using var service = new LocalizationService(_mockLanguageProvider.Object, _mockLogger.Object, options);
        var key = "NonExistentKey";

        // Act
        var result = service.GetString(key);

        // Assert
        Assert.Equal(key, result);
        Assert.DoesNotContain("[", result);
        Assert.DoesNotContain("]", result);
    }
}