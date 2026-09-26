using Avalonia.Data.Converters;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="TelemetryLevel"/> enum value to its localized display string.
/// </summary>
public class LocalizedTelemetryLevelConverter : IValueConverter
{
    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not TelemetryLevel level)
        {
            if (value is string str && Enum.TryParse<TelemetryLevel>(str, out var parsedLevel))
            {
                level = parsedLevel;
            }
            else
            {
                return value?.ToString() ?? string.Empty;
            }
        }

        try
        {
            var localizationService = LocalizationConverterHelper.ResolveLocalizationService();
            if (localizationService == null)
            {
                return level switch
                {
                    TelemetryLevel.Disabled => "Disabled",
                    TelemetryLevel.CrashReportsOnly => "Crash Reports Only",
                    TelemetryLevel.AnonymousMetrics => "Anonymous Usage Metrics & Crash Reports",
                    _ => level.ToString(),
                };
            }

            return level switch
            {
                TelemetryLevel.Disabled => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Settings.DiagnosticsPrivacy.TelemetryLevel.Disabled", "Disabled"),
                TelemetryLevel.CrashReportsOnly => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Settings.DiagnosticsPrivacy.TelemetryLevel.CrashReportsOnly", "Crash Reports Only"),
                TelemetryLevel.AnonymousMetrics => LocalizationConverterHelper.GetLocalizedOrDefault(localizationService, "Settings.DiagnosticsPrivacy.TelemetryLevel.AnonymousMetrics", "Anonymous Usage Metrics & Crash Reports"),
                _ => level.ToString(),
            };
        }
        catch (InvalidOperationException)
        {
            return level.ToString();
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
