using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a ContentType enum value to a translucent SolidColorBrush for badge background tinting.
/// </summary>
public class ContentTypeToBadgeBackgroundConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly ContentTypeToBadgeBackgroundConverter Instance = new();

    private const byte BadgeAlpha = 0x25;

    private static readonly SolidColorBrush GameClientBrush = CreateTintBrush(UiConstants.ContentTypeGameClientColor);
    private static readonly SolidColorBrush ModBrush = CreateTintBrush(UiConstants.ContentTypeModColor);
    private static readonly SolidColorBrush PatchBrush = CreateTintBrush(UiConstants.ContentTypePatchColor);
    private static readonly SolidColorBrush MapBrush = CreateTintBrush(UiConstants.ContentTypeMapColor);
    private static readonly SolidColorBrush AddonBrush = CreateTintBrush(UiConstants.ContentTypeAddonColor);
    private static readonly SolidColorBrush ToolBrush = CreateTintBrush(UiConstants.ContentTypeToolColor);
    private static readonly SolidColorBrush BundleBrush = CreateTintBrush(UiConstants.ContentTypeBundleColor);
    private static readonly SolidColorBrush MissionBrush = CreateTintBrush(UiConstants.ContentTypeMissionColor);
    private static readonly SolidColorBrush SkinBrush = CreateTintBrush(UiConstants.ContentTypeSkinColor);

    /// <summary>
    /// Converts a ContentType to a translucent SolidColorBrush.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type (ignored).</param>
    /// <param name="parameter">Optional parameter (ignored).</param>
    /// <param name="culture">The culture (ignored).</param>
    /// <returns>A SolidColorBrush representing the content type badge background tint.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ContentType contentType)
        {
            return contentType switch
            {
                ContentType.GameClient => GameClientBrush,
                ContentType.Mod => ModBrush,
                ContentType.Patch => PatchBrush,
                ContentType.Map or ContentType.MapPack => MapBrush,
                ContentType.Addon => AddonBrush,
                ContentType.ModdingTool or ContentType.Executable => ToolBrush,
                ContentType.ContentBundle => BundleBrush,
                ContentType.Mission => MissionBrush,
                ContentType.Skin or ContentType.LanguagePack => SkinBrush,
                _ => ModBrush,
            };
        }

        return ModBrush;
    }

    /// <summary>
    /// Converts back from a brush to a ContentType (not supported).
    /// </summary>
    /// <param name="value">The value to convert back.</param>
    /// <param name="targetType">The target type.</param>
    /// <param name="parameter">Optional parameter.</param>
    /// <param name="culture">The culture.</param>
    /// <returns><see cref="AvaloniaProperty.UnsetValue"/> as two-way conversion is not supported.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }

    private static SolidColorBrush CreateTintBrush(string hex)
    {
        var baseColor = Color.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(BadgeAlpha, baseColor.R, baseColor.G, baseColor.B));
    }
}
