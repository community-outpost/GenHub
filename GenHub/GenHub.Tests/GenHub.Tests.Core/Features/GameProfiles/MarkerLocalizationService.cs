using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Results;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;

namespace GenHub.Tests.Core.Features.GameProfiles;

/// <summary>
/// Localization fake that resolves every key to a marker built from the key and its arguments,
/// so tests can prove text was resolved through a resource key rather than a hardcoded literal.
/// </summary>
internal sealed class MarkerLocalizationService : ILocalizationService
{
    /// <inheritdoc/>
    public IReadOnlyList<CultureInfo> AvailableCultures { get; } = [CultureInfo.InvariantCulture];

    /// <inheritdoc/>
    public CultureInfo CurrentCulture => CultureInfo.InvariantCulture;

    /// <inheritdoc/>
    public string this[string key] => Marker(key);

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged
    {
        add { }
        remove { }
    }

    /// <summary>
    /// Builds the marker a key and its arguments resolve to.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">The format arguments.</param>
    /// <returns>The marker string.</returns>
    public static string Marker(string key, params object?[] arguments) =>
        arguments.Length == 0
            ? $"[{key}]"
            : $"[{key}|{string.Join("|", arguments.Select(a => a?.ToString()))}]";

    /// <inheritdoc/>
    public string GetString(string key, params object?[] arguments) => Marker(key, arguments);

    /// <inheritdoc/>
    public bool TryGetString(string key, [NotNullWhen(true)] out string? result, params object?[] arguments)
    {
        result = Marker(key, arguments);
        return true;
    }

    /// <inheritdoc/>
    public OperationResult SetCulture(CultureInfo culture) => OperationResult.CreateSuccess();
}
