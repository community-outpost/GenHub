using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using GenHub.Core.Models.Tools.GenHotkeys;
using System;
using System.Globalization;
using System.IO;

namespace GenHub.Features.Tools.GenHotkeys.Converters;

/// <summary>
/// Converts a faction or faction group string to its corresponding emblem <see cref="Bitmap"/>.
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
            _ => null,
        };
        group ??= parameter as string ?? HotkeyFaction.UsaGroup;

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

    private static Bitmap? LoadAssetBitmap(string fileName)
    {
        try
        {
            var uri = new Uri($"avares://GenHub/Assets/Icons/Factions/{fileName}");
            if (AssetLoader.Exists(uri))
            {
                using var stream = AssetLoader.Open(uri);
                return new Bitmap(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        {
            // Fall through
        }

        return null;
    }
}
