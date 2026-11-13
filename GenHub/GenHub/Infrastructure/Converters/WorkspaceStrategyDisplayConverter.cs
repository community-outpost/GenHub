using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="WorkspaceStrategy"/> value to its localized display name or description.
/// </summary>
/// <remarks>
/// As a multi-value converter the first value is the strategy. Any further value, such as the
/// localization service's current culture, only makes the binding refresh when the language changes.
/// </remarks>
public sealed class WorkspaceStrategyDisplayConverter : IValueConverter, IMultiValueConverter
{
    /// <summary>
    /// Gets a converter that returns the localized display name of a strategy.
    /// </summary>
    public static readonly WorkspaceStrategyDisplayConverter Name = new(describe: false);

    /// <summary>
    /// Gets a converter that returns the localized one-line description of a strategy.
    /// </summary>
    public static readonly WorkspaceStrategyDisplayConverter Description = new(describe: true);

    private const string NameKeyFormat = "WorkspaceStrategy.{0}.Name";
    private const string DescriptionKeyFormat = "WorkspaceStrategy.{0}.Description";
    private const string WindowsNoteKeyFormat = "WorkspaceStrategy.{0}.WindowsNote";

    private readonly bool _describe;

    private WorkspaceStrategyDisplayConverter(bool describe)
    {
        _describe = describe;
    }

    /// <summary>
    /// Gets the application localization service, for bindings that refresh on culture changes.
    /// </summary>
    public static ILocalizationService? LocalizationSource => LocalizationConverterHelper.ResolveLocalizationService();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not WorkspaceStrategy strategy)
        {
            return value?.ToString() ?? string.Empty;
        }

        var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
        return _describe
            ? GetDescription(strategy, localizationService, OperatingSystem.IsWindows())
            : GetName(strategy, localizationService);
    }

    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        Convert(values.Count > 0 ? values[0] : null, targetType, parameter, culture);

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    /// <summary>
    /// Gets the localized display name of a strategy.
    /// </summary>
    /// <param name="strategy">The strategy to name.</param>
    /// <param name="localizationService">The localization service, or null when unavailable.</param>
    /// <returns>The localized name, or the enum name when no resource exists.</returns>
    internal static string GetName(WorkspaceStrategy strategy, ILocalizationService? localizationService) =>
        LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, FormatKey(NameKeyFormat, strategy), strategy.ToString());

    /// <summary>
    /// Gets the localized description of a strategy, with the Windows permission note when it applies.
    /// </summary>
    /// <param name="strategy">The strategy to describe.</param>
    /// <param name="localizationService">The localization service, or null when unavailable.</param>
    /// <param name="isWindows">Whether the host is Windows.</param>
    /// <returns>The localized description, or an empty string when no resource exists.</returns>
    internal static string GetDescription(WorkspaceStrategy strategy, ILocalizationService? localizationService, bool isWindows)
    {
        var description = LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, FormatKey(DescriptionKeyFormat, strategy), string.Empty);
        if (!isWindows)
        {
            return description;
        }

        var windowsNote = LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, FormatKey(WindowsNoteKeyFormat, strategy), string.Empty);
        if (string.IsNullOrEmpty(windowsNote))
        {
            return description;
        }

        return string.IsNullOrEmpty(description) ? windowsNote : $"{description} {windowsNote}";
    }

    private static string FormatKey(string keyFormat, WorkspaceStrategy strategy) =>
        string.Format(CultureInfo.InvariantCulture, keyFormat, strategy);
}
