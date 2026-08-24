using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests for resource fallback, discovery, and runtime culture switching.
/// </summary>
[Collection(LocalizationCultureCollection.Name)]
public sealed class LocalizationServiceTests : IDisposable
{
    private const string TestResourceBaseName = "GenHub.Tests.Core.Resources.Localization.TestStrings";

    private readonly CultureInfo? _originalDefaultCulture;
    private readonly CultureInfo? _originalDefaultUiCulture;
    private readonly CultureInfo _originalThreadCulture;
    private readonly CultureInfo _originalThreadUiCulture;
    private readonly LocalizationService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationServiceTests"/> class.
    /// </summary>
    public LocalizationServiceTests()
    {
        _originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        _originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;
        _originalThreadCulture = CultureInfo.CurrentCulture;
        _originalThreadUiCulture = CultureInfo.CurrentUICulture;

        var resourceAssembly = typeof(LocalizationServiceTests).Assembly;
        var assemblyName = resourceAssembly.GetName().Name
            ?? throw new InvalidOperationException("The test assembly name could not be resolved.");
        var baseDirectory = Path.GetDirectoryName(resourceAssembly.Location)
            ?? throw new InvalidOperationException("The test assembly directory could not be resolved.");
        var resources = new LocalizationResources(
            new ResourceManager(TestResourceBaseName, resourceAssembly),
            $"{assemblyName}{LocalizationConstants.SatelliteAssemblySuffix}",
            baseDirectory,
            CultureInfo.GetCultureInfo(LocalizationConstants.DefaultCultureName));

        _service = new LocalizationService(resources, NullLogger<LocalizationService>.Instance);
    }

    /// <summary>
    /// Restores process-wide culture defaults changed by localization tests.
    /// </summary>
    public void Dispose()
    {
        CultureInfo.CurrentCulture = _originalThreadCulture;
        CultureInfo.CurrentUICulture = _originalThreadUiCulture;
        CultureInfo.DefaultThreadCurrentCulture = _originalDefaultCulture;
        CultureInfo.DefaultThreadCurrentUICulture = _originalDefaultUiCulture;
    }

    /// <summary>
    /// Verifies that the neutral language and deployed test satellite are discovered automatically.
    /// </summary>
    [Fact]
    public void AvailableCultures_DiscoversNeutralAndSatelliteCultures()
    {
        var cultureNames = _service.AvailableCultures.Select(culture => culture.Name).ToList();

        Assert.Equal(["en", "fr"], cultureNames);
    }

    /// <summary>
    /// Verifies that a translated value is loaded from the active satellite assembly.
    /// </summary>
    [Fact]
    public void GetString_UsesActiveCultureTranslation()
    {
        var result = _service.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.True(result.Success);
        Assert.Equal("Bonjour", _service.GetString("Greeting"));
    }

    /// <summary>
    /// Verifies that missing translated values fall back to the neutral English resource.
    /// </summary>
    [Fact]
    public void GetString_MissingTranslation_FallsBackToEnglish()
    {
        var result = _service.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.True(result.Success);
        Assert.Equal("English fallback", _service.GetString("FallbackOnly"));
    }

    /// <summary>
    /// Verifies that formatted translations use the active culture and supplied arguments.
    /// </summary>
    [Fact]
    public void GetString_WithArguments_FormatsTranslatedValue()
    {
        var result = _service.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.True(result.Success);
        Assert.Equal("Bonjour, General!", _service.GetString("FormattedGreeting", "General"));
    }

    /// <summary>
    /// Verifies that a completely unknown key remains visible for diagnostics.
    /// </summary>
    [Fact]
    public void GetString_UnknownKey_ReturnsKey()
    {
        Assert.Equal("Missing.Resource.Key", _service.GetString("Missing.Resource.Key"));
    }

    /// <summary>
    /// Verifies that changing culture refreshes both the culture and all indexer bindings.
    /// </summary>
    [Fact]
    public void SetCulture_AvailableCulture_RaisesLiveBindingNotifications()
    {
        var propertyNames = new List<string?>();
        _service.PropertyChanged += (_, eventArgs) => propertyNames.Add(eventArgs.PropertyName);

        var result = _service.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.True(result.Success);
        Assert.Equal("fr", _service.CurrentCulture.Name);
        Assert.Equal("fr", CultureInfo.CurrentCulture.Name);
        Assert.Equal("fr", CultureInfo.CurrentUICulture.Name);
        Assert.Equal(
            [nameof(_service.CurrentCulture), LocalizationConstants.IndexerPropertyName],
            propertyNames);
    }

    /// <summary>
    /// Verifies that an unavailable culture returns a failure without changing state.
    /// </summary>
    [Fact]
    public void SetCulture_UnavailableCulture_ReturnsFailureWithoutChangingCulture()
    {
        var originalCulture = _service.CurrentCulture;

        var result = _service.SetCulture(CultureInfo.GetCultureInfo("es"));

        Assert.True(result.Failed);
        Assert.Equal(originalCulture, _service.CurrentCulture);
        Assert.Contains("not available", result.FirstError, StringComparison.OrdinalIgnoreCase);
    }
}
