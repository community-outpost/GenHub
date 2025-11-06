using GenHub.Core.Interfaces.Localization;
using GenHub.Core.Resources.Strings;
using GenHub.Core.Services.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using System.Globalization;
using System.Resources;
using Xunit;

namespace GenHub.Tests.Core.Resources;

/// <summary>
/// Integration tests for string resource loading and localization infrastructure.
/// Tests that .resx files are properly embedded, ResourceManagers can load them,
/// and the LocalizationService can retrieve strings.
/// </summary>
public class StringResourcesTests
{
    private readonly ILanguageProvider _languageProvider;
    private readonly ILocalizationService _localizationService;

    public StringResourcesTests()
    {
        var loggerLanguageProvider = new Mock<ILogger<LanguageProvider>>();
        var loggerLocalizationService = new Mock<ILogger<LocalizationService>>();

        _languageProvider = new LanguageProvider(loggerLanguageProvider.Object);
        _localizationService = new LocalizationService(
            _languageProvider,
            loggerLocalizationService.Object);
    }

    [Fact]
    public void AllResourceSetsAreEmbedded()
    {
        // Arrange & Act - Try to create ResourceManager for each resource set
        var resourceSets = new[]
        {
            StringResources.UiCommon,
            StringResources.UiNavigation,
            StringResources.UiGameProfiles,
            StringResources.UiSettings,
            StringResources.UiUpdates,
            StringResources.ErrorsValidation,
            StringResources.ErrorsOperations,
            StringResources.MessagesSuccess,
            StringResources.MessagesConfirmations,
            StringResources.Tooltips
        };

        foreach (var resourceSet in resourceSets)
        {
            // Assert - Should not throw exception
            var resourceManager = _languageProvider.GetResourceManager(resourceSet);
            Assert.NotNull(resourceManager);
        }
    }

    [Fact]
    public void CanLoadStringFromUiCommon()
    {
        // Arrange
        var resourceManager = _languageProvider.GetResourceManager(StringResources.UiCommon);

        // Act
        var saveButton = resourceManager.GetString("Button.Save", CultureInfo.GetCultureInfo("en"));

        // Assert
        Assert.NotNull(saveButton);
        Assert.Equal("Save", saveButton);
    }

    [Fact]
    public void CanLoadStringFromUiNavigation()
    {
        // Arrange
        var resourceManager = _languageProvider.GetResourceManager(StringResources.UiNavigation);

        // Act
        var gameProfilesTab = resourceManager.GetString("Tab.GameProfiles", CultureInfo.GetCultureInfo("en"));

        // Assert
        Assert.NotNull(gameProfilesTab);
        Assert.Equal("Game Profiles", gameProfilesTab);
    }

    [Fact]
    public void CanLoadStringFromErrorsValidation()
    {
        // Arrange
        var resourceManager = _languageProvider.GetResourceManager(StringResources.ErrorsValidation);

        // Act
        var requiredField = resourceManager.GetString("RequiredField", CultureInfo.GetCultureInfo("en"));

        // Assert
        Assert.NotNull(requiredField);
        Assert.Equal("This field is required", requiredField);
    }

    [Fact]
    public void LocalizationService_CanRetrieveStringWithResourceSet()
    {
        // Act
        var saveButton = _localizationService.GetString(StringResources.UiCommon, "Button.Save");

        // Assert
        Assert.NotNull(saveButton);
        Assert.Equal("Save", saveButton);
    }

    [Fact]
    public void LocalizationService_CanFormatStringWithArguments()
    {
        // Act
        var scanComplete = _localizationService.GetString(
            StringResources.UiGameProfiles,
            "Status.ScanComplete",
            5);

        // Assert
        Assert.NotNull(scanComplete);
        Assert.Contains("5", scanComplete);
        Assert.Contains("game installations", scanComplete);
    }

    [Fact]
    public void LocalizationService_ReturnsPlaceholderForMissingKey()
    {
        // Act
        var result = _localizationService.GetString(StringResources.UiCommon, "NonExistentKey");

        // Assert
        Assert.NotNull(result);
        Assert.Contains("NonExistentKey", result);
    }

    [Fact]
    public void LanguageProvider_CanDiscoverEnglishCulture()
    {
        // Act
        var cultures = _languageProvider.DiscoverAvailableLanguages().GetAwaiter().GetResult();

        // Assert
        Assert.NotNull(cultures);
        Assert.NotEmpty(cultures);
        Assert.Contains(cultures, c => c.TwoLetterISOLanguageName == "en");
    }

    [Fact]
    public void LanguageProvider_ValidatesEnglishCulture()
    {
        // Arrange
        var englishCulture = CultureInfo.GetCultureInfo("en");

        // Act
        var isValid = _languageProvider.ValidateCulture(englishCulture);

        // Assert
        Assert.True(isValid);
    }

    [Theory]
    [InlineData(StringResources.UiCommon, "Button.Cancel", "Cancel")]
    [InlineData(StringResources.UiCommon, "Button.OK", "OK")]
    [InlineData(StringResources.UiCommon, "Status.Loading", "Loading...")]
    [InlineData(StringResources.UiNavigation, "Tab.Downloads", "Downloads")]
    [InlineData(StringResources.UiNavigation, "Tab.Settings", "Settings")]
    [InlineData(StringResources.UiSettings, "Button.SaveSettings", "Save Settings")]
    [InlineData(StringResources.UiUpdates, "Status.ReadyToCheck", "Ready to check for updates")]
    [InlineData(StringResources.ErrorsValidation, "InvalidFormat", "Invalid format")]
    [InlineData(StringResources.ErrorsOperations, "OperationFailed", "Operation failed")]
    [InlineData(StringResources.MessagesSuccess, "OperationCompleted", "Operation completed successfully")]
    [InlineData(StringResources.MessagesConfirmations, "DeleteProfile", "Are you sure you want to delete this profile?")]
    [InlineData(StringResources.Tooltips, "Button.Save", "Save changes")]
    public void CanLoadSpecificStringsFromAllResourceSets(string resourceSet, string key, string expected)
    {
        // Act
        var result = _localizationService.GetString(resourceSet, key);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ResourceManager_IsCachedByLanguageProvider()
    {
        // Act
        var rm1 = _languageProvider.GetResourceManager(StringResources.UiCommon);
        var rm2 = _languageProvider.GetResourceManager(StringResources.UiCommon);

        // Assert - Should return the same instance (cached)
        Assert.Same(rm1, rm2);
    }

    [Fact]
    public void LocalizationService_CurrentCultureIsEnglishByDefault()
    {
        // Act
        var currentCulture = _localizationService.CurrentCulture;

        // Assert
        Assert.NotNull(currentCulture);
        Assert.Equal("en", currentCulture.TwoLetterISOLanguageName);
    }

    [Fact]
    public void AllResourceSetsContainMultipleStrings()
    {
        // Arrange
        var resourceSets = new[]
        {
            StringResources.UiCommon,
            StringResources.UiNavigation,
            StringResources.UiGameProfiles,
            StringResources.UiSettings,
            StringResources.UiUpdates,
            StringResources.ErrorsValidation,
            StringResources.ErrorsOperations,
            StringResources.MessagesSuccess,
            StringResources.MessagesConfirmations,
            StringResources.Tooltips
        };

        foreach (var resourceSetName in resourceSets)
        {
            // Act
            var resourceManager = _languageProvider.GetResourceManager(resourceSetName);
            var resourceSet = resourceManager.GetResourceSet(CultureInfo.GetCultureInfo("en"), true, true);

            // Assert
            Assert.NotNull(resourceSet);
            
            var enumerator = resourceSet.GetEnumerator();
            var count = 0;
            while (enumerator.MoveNext())
            {
                count++;
            }

            // Each resource file should have at least 10 strings
            Assert.True(count >= 10, $"{resourceSetName} should have at least 10 strings, but has {count}");
        }
    }

    [Fact]
    public void FormatStrings_WorkWithMultipleParameters()
    {
        // Act
        var profileDescription = _localizationService.GetString(
            StringResources.UiGameProfiles,
            "Profile.AutoCreatedDescription",
            "Steam",
            "C:\\Games\\MyGame");

        // Assert
        Assert.NotNull(profileDescription);
        Assert.Contains("Steam", profileDescription);
        Assert.Contains("C:\\Games\\MyGame", profileDescription);
    }
}