using GenHub.Core.Constants;
using GenHub.Core.Extensions;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Xunit;

namespace GenHub.Tests.Core.Extensions;

/// <summary>
/// Unit tests for <see cref="LocalizationServiceExtensions"/>.
/// </summary>
public class LocalizationServiceExtensionsTests
{
    /// <summary>
    /// Verifies that a null service returns the English fallback.
    /// </summary>
    [Fact]
    public void GetLocalizedString_NullService_ReturnsFallback()
    {
        ILocalizationService? service = null;

        Assert.Equal("Fallback", service.GetLocalizedString("Any.Key", "Fallback"));
    }

    /// <summary>
    /// Verifies that a null service formats the English fallback with the provided arguments.
    /// </summary>
    [Fact]
    public void GetLocalizedString_NullService_FormatsFallback()
    {
        ILocalizationService? service = null;

        Assert.Equal("Hello World", service.GetLocalizedString("Any.Key", "Hello {0}", "World"));
    }

    /// <summary>
    /// Verifies that a known key returns the localized value from the service.
    /// </summary>
    [Fact]
    public void GetLocalizedString_KnownKey_ReturnsLocalizedValue()
    {
        var service = new StubLocalizationService(new Dictionary<string, string>
        {
            ["Known.Key"] = "Localized!",
        });

        Assert.Equal("Localized!", service.GetLocalizedString("Known.Key", "Fallback"));
    }

    /// <summary>
    /// Verifies that a missing key returns the English fallback without throwing.
    /// </summary>
    [Fact]
    public void GetLocalizedString_MissingKey_ReturnsFallback()
    {
        var service = new StubLocalizationService(new Dictionary<string, string>());

        Assert.Equal("Fallback", service.GetLocalizedString("Missing.Key", "Fallback"));
    }

    /// <summary>
    /// Verifies that format arguments are applied to the localized template.
    /// </summary>
    [Fact]
    public void GetLocalizedString_WithArguments_FormatsLocalizedTemplate()
    {
        var service = new StubLocalizationService(new Dictionary<string, string>
        {
            ["Greet.Key"] = "Hello {0}",
        });

        Assert.Equal("Hello World", service.GetLocalizedString("Greet.Key", "Hello {0}", "World"));
    }

    /// <summary>
    /// Verifies that a malformed fallback format string returns the raw fallback instead of throwing.
    /// </summary>
    [Fact]
    public void GetLocalizedString_MalformedFallback_ReturnsRawFallback()
    {
        ILocalizationService? service = null;

        Assert.Equal("Bad { format", service.GetLocalizedString("Any.Key", "Bad { format", "World"));
    }

    private sealed class StubLocalizationService(Dictionary<string, string> resources) : ILocalizationService
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public IReadOnlyList<CultureInfo> AvailableCultures { get; } = [CultureInfo.InvariantCulture];

        public CultureInfo CurrentCulture { get; } = CultureInfo.InvariantCulture;

        public string this[string key] => resources.TryGetValue(key, out var value) ? value : key;

        public string GetString(string key, params object?[] arguments)
        {
            return TryGetString(key, out var result, arguments) ? result : key;
        }

        public bool TryGetString(string key, [NotNullWhen(true)] out string? result, params object?[] arguments)
        {
            if (resources.TryGetValue(key, out var value))
            {
                result = arguments.Length == 0 ? value : string.Format(CultureInfo.InvariantCulture, value, arguments);
                return true;
            }

            result = null;
            return false;
        }

        public OperationResult SetCulture(CultureInfo culture)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
            return OperationResult.CreateSuccess();
        }
    }
}
