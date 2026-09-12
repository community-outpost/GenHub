using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using GenHub.Core.Models.Tools.GenHotkeys;
using GenHub.Features.Tools.GenHotkeys.Services;

namespace GenHub.Features.Tools.GenHotkeys.Converters;

/// <summary>
/// Converts a faction or faction group string to the corresponding authentic transparent faction logo <see cref="Bitmap"/>.
/// </summary>
public class FactionToImageConverter : IValueConverter
{
    private static readonly Lazy<Bitmap?> UsaBitmap = new(() => LoadAssetBitmap("usa.png"));
    private static readonly Lazy<Bitmap?> ChinaBitmap = new(() => LoadAssetBitmap("china.png"));
    private static readonly Lazy<Bitmap?> GlaBitmap = new(() => LoadAssetBitmap("gla.png"));

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var group = value switch
        {
            HotkeyFaction faction => faction.FactionGroup,
            string s => s,
            _ => HotkeyFaction.UsaGroup,
        };

        if (string.Equals(group, HotkeyFaction.ChinaGroup, StringComparison.OrdinalIgnoreCase))
        {
            return ChinaBitmap.Value;
        }

        if (string.Equals(group, HotkeyFaction.GlaGroup, StringComparison.OrdinalIgnoreCase))
        {
            return GlaBitmap.Value;
        }

        return UsaBitmap.Value;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static Bitmap? LoadAssetBitmap(string filename)
    {
        try
        {
            using var stream = GenHotkeysAssetLoader.TryOpenFactionIconStream(filename);
            if (stream != null)
            {
                return new Bitmap(stream);
            }
        }
        catch
        {
            // Fall back to null if resource cannot be loaded
        }

        return null;
    }
}
