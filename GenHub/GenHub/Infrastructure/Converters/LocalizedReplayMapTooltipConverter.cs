using Avalonia.Data.Converters;
using Avalonia.Logging;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools.ReplayManager;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a replay into a localized map tooltip that names the map and states whether it is available.
/// As a multi-value converter the first value is the replay and later values only trigger a refresh,
/// so binding <see cref="ReplayFile.MapAvailability"/> as a second value updates the tooltip when the scan finishes.
/// </summary>
public class LocalizedReplayMapTooltipConverter : IValueConverter, IMultiValueConverter
{
    /// <summary>
    /// Static instance for XAML usage.
    /// </summary>
    public static readonly LocalizedReplayMapTooltipConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ReplayFile replay)
        {
            return null;
        }

        try
        {
            return BuildTooltip(replay, LocalizationConverterHelper.ResolveLocalizationService());
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            Logger.TryGet(LogEventLevel.Warning, LogArea.Binding)?.Log(this, "Could not build the replay map tooltip: {Exception}", ex);
            return replay.MapName;
        }
    }

    /// <inheritdoc/>
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Count > 0 ? Convert(values[0], targetType, parameter, culture) : null;

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static string? BuildTooltip(ReplayFile replay, ILocalizationService? loc)
    {
        var mapCrc = replay.Metadata?.FormattedMapCrc;
        var mapSize = replay.Metadata?.MapSize;
        if (mapCrc == null || mapSize == null)
        {
            return replay.MapName;
        }

        var size = mapSize.Value.ToString(CultureInfo.CurrentCulture);
        var status = replay.MapAvailability switch
        {
            ReplayMapAvailability.Available =>
                loc?.GetString("Tools.ReplayManager.Tooltip.Map.Available", mapCrc, size)
                ?? $"Map found. Matched by CRC {mapCrc} and size {size} bytes.",
            ReplayMapAvailability.NotFound =>
                loc?.GetString("Tools.ReplayManager.Tooltip.Map.NotFound", mapCrc, size)
                ?? $"Map not found. No user map or installed stock map matches CRC {mapCrc} and size {size} bytes.",
            _ =>
                loc?.GetString("Tools.ReplayManager.Tooltip.Map.Unknown", mapCrc, size)
                ?? $"Map availability could not be checked. Map CRC {mapCrc}, size {size} bytes.",
        };

        return string.IsNullOrWhiteSpace(replay.MapName) ? status : $"{replay.MapName}\n{status}";
    }
}
