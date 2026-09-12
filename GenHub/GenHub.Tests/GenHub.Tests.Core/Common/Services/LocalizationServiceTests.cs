using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using Avalonia;
using GenHub.Common.Markup;
using GenHub.Common.Services;
using GenHub.Core.Constants;
using GenHub.Tests.Core.Collections;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenHub.Tests.Core.Common.Services;

/// <summary>
/// Unit tests for resource fallback, discovery, and runtime culture switching.
/// </summary>
[Collection(LocalizationCultureCollection.Name)]
public sealed class LocalizationServiceTests : IDisposable
{
    private sealed class BindingTarget : AvaloniaObject
    {
        internal static readonly StyledProperty<string?> ValueProperty =
            AvaloniaProperty.Register<BindingTarget, string?>(nameof(Value));

        internal string? Value
        {
            get => GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }

    private sealed class CountingLogger<T> : ILogger<T>
    {
        internal int WarningCount { get; private set; }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                WarningCount++;
            }
        }
    }

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

        _service = CreateService(AppContext.BaseDirectory, NullLogger<LocalizationService>.Instance);
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
    /// Verifies that null format arguments remain supported by the public contract.
    /// </summary>
    [Fact]
    public void GetString_WithNullArgument_FormatsAsEmptyText()
    {
        Assert.Equal("Hello, !", _service.GetString("FormattedGreeting", (object?)null));
    }

    /// <summary>
    /// Verifies that an invalid translated format string remains visible instead of throwing.
    /// </summary>
    [Fact]
    public void GetString_InvalidFormatString_ReturnsUnformattedValue()
    {
        Assert.Equal("Hello, {0", _service.GetString("MalformedGreeting", "General"));
    }

    /// <summary>
    /// Verifies that the indexer delegates to resource lookup.
    /// </summary>
    [Fact]
    public void Indexer_KnownKey_ReturnsLocalizedValue()
    {
        Assert.Equal("Hello", _service["Greeting"]);
    }

    /// <summary>
    /// Verifies that invalid lookup and culture arguments fail at the contract boundary.
    /// </summary>
    [Fact]
    public void PublicMethods_InvalidArguments_ThrowArgumentExceptions()
    {
        Assert.Throws<ArgumentException>(() => _service.GetString(" "));
        Assert.Throws<ArgumentNullException>(() => _service.GetString("Greeting", (object?[])null!));
        Assert.Throws<ArgumentNullException>(() => _service.SetCulture(null!));
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
    /// Verifies that repeated binding evaluations do not flood logs with the same missing key.
    /// </summary>
    [Fact]
    public void GetString_RepeatedMissingKey_LogsOncePerCulture()
    {
        var logger = new CountingLogger<LocalizationService>();
        var service = CreateService(AppContext.BaseDirectory, logger);

        service.GetString("Missing.Resource.Key");
        service.GetString("Missing.Resource.Key");

        Assert.Equal(1, logger.WarningCount);

        var result = service.SetCulture(CultureInfo.GetCultureInfo("fr"));
        service.GetString("Missing.Resource.Key");

        Assert.True(result.Success);
        Assert.Equal(2, logger.WarningCount);
    }

    /// <summary>
    /// Verifies that changing culture refreshes both the culture and all indexer bindings.
    /// </summary>
    [Fact]
    public void SetCulture_AvailableCulture_RaisesLiveBindingNotifications()
    {
        var propertyNames = new List<string?>();
        var formattingCulture = CultureInfo.CurrentCulture;
        _service.PropertyChanged += (_, eventArgs) => propertyNames.Add(eventArgs.PropertyName);

        var result = _service.SetCulture(CultureInfo.GetCultureInfo("fr"));

        Assert.True(result.Success);
        Assert.Equal("fr", _service.CurrentCulture.Name);
        Assert.Equal("fr", CultureInfo.CurrentUICulture.Name);
        Assert.Equal("fr", CultureInfo.DefaultThreadCurrentUICulture?.Name);
        Assert.Equal(formattingCulture, CultureInfo.CurrentCulture);
        Assert.Equal(
            [nameof(_service.CurrentCulture), LocalizationConstants.IndexerPropertyName],
            propertyNames);
    }

    /// <summary>
    /// Verifies that a dotted resource key resolves and refreshes through the Avalonia binding path.
    /// </summary>
    [Fact]
    public void LocalizeExtension_DottedKeyBinding_RefreshesWhenCultureChanges()
    {
        var target = new BindingTarget();
        var extension = new LocalizeExtension("Settings.Appearance.Title");
        using (target.Bind(BindingTarget.ValueProperty, extension.CreateBinding(_service)))
        {
            Assert.Equal("Appearance", target.Value);

            var result = _service.SetCulture(CultureInfo.GetCultureInfo("fr"));

            Assert.True(result.Success);
            Assert.Equal("Apparence", target.Value);
        }
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

    /// <summary>
    /// Verifies that one invalid satellite cannot abort discovery of later valid cultures.
    /// </summary>
    [Fact]
    public void AvailableCultures_InvalidSatellite_IgnoresItAndContinuesDiscovery()
    {
        var resourceAssemblyName = typeof(LocalizationServiceTests).Assembly.GetName().Name
            ?? throw new InvalidOperationException("The test assembly name could not be resolved.");
        var corruptCultureDirectory = Path.Combine(AppContext.BaseDirectory, "es-MX");
        var corruptSatellitePath = Path.Combine(
            corruptCultureDirectory,
            $"{resourceAssemblyName}{LocalizationConstants.SatelliteAssemblySuffix}");

        Directory.CreateDirectory(corruptCultureDirectory);
        File.WriteAllBytes(corruptSatellitePath, [0x00, 0x01, 0x02, 0x03]);

        try
        {
            var service = CreateService(AppContext.BaseDirectory, NullLogger<LocalizationService>.Instance);
            var cultureNames = service.AvailableCultures.Select(culture => culture.Name).ToList();

            Assert.DoesNotContain("es-MX", cultureNames);
            Assert.Contains("fr", cultureNames);
        }
        finally
        {
            File.Delete(corruptSatellitePath);
            if (!Directory.EnumerateFileSystemEntries(corruptCultureDirectory).Any())
            {
                Directory.Delete(corruptCultureDirectory);
            }
        }
    }

    private LocalizationService CreateService(string baseDirectory, ILogger<LocalizationService> logger)
    {
        var resourceAssembly = typeof(LocalizationServiceTests).Assembly;
        var assemblyName = resourceAssembly.GetName().Name
            ?? throw new InvalidOperationException("The test assembly name could not be resolved.");
        var resources = new LocalizationResources(
            new ResourceManager(TestResourceBaseName, resourceAssembly),
            $"{assemblyName}{LocalizationConstants.SatelliteAssemblySuffix}",
            baseDirectory,
            CultureInfo.GetCultureInfo(LocalizationConstants.DefaultCultureName));

        return new LocalizationService(resources, logger);
    }
}
