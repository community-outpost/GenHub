using Avalonia.Data.Converters;
using GenHub.Core.Constants;
using GenHub.Core.Models.Tools.ReplayManager;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a <see cref="ReplaySlotInfo"/> model into a localized slot display label.
/// </summary>
public class LocalizedReplaySlotConverter : IValueConverter
{
    /// <summary>
    /// Static instance for XAML usage.
    /// </summary>
    public static readonly LocalizedReplaySlotConverter Instance = new();

    /// <inheritdoc/>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ReplaySlotInfo slot)
        {
            return value?.ToString() ?? string.Empty;
        }

        try
        {
            var loc = LocalizationConverterHelper.ResolveLocalizationService();
            if (loc == null)
            {
                return slot.DisplayLabel;
            }

            var slotNumber = slot.SlotIndex + 1;
            if (slot.IsHuman || ReplaySlotInfo.HasAiIndicator(slot.PlayerName))
            {
                var format = loc.GetString("Tools.ReplayManager.Checkpoint.SlotLabelFormat") ?? "Slot {0}: {1}";
                return string.Format(culture ?? CultureInfo.CurrentCulture, format, slotNumber, slot.PlayerName);
            }

            var aiLabel = loc.GetString("Tools.ReplayManager.Checkpoint.AiLabel") ?? ReplayManagerConstants.AiLabel;
            var aiFormat = loc.GetString("Tools.ReplayManager.Checkpoint.SlotLabelAiFormat") ?? "Slot {0}: {1} ({2})";
            return string.Format(culture ?? CultureInfo.CurrentCulture, aiFormat, slotNumber, slot.PlayerName, aiLabel);
        }
        catch
        {
            return slot.DisplayLabel;
        }
    }

    /// <inheritdoc/>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("LocalizedReplaySlotConverter does not support two-way binding.");
    }
}
