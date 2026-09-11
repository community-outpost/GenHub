using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a ContentType enum value to a SolidColorBrush for UI visual distinction.
/// </summary>
public class ContentTypeToBrushConverter : IValueConverter
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly ContentTypeToBrushConverter Instance = new();

    private static readonly SolidColorBrush GameClientBrush = new(Color.Parse(UiConstants.ContentTypeGameClientColor));
    private static readonly SolidColorBrush ModBrush = new(Color.Parse(UiConstants.ContentTypeModColor));
    private static readonly SolidColorBrush PatchBrush = new(Color.Parse(UiConstants.ContentTypePatchColor));
    private static readonly SolidColorBrush MapBrush = new(Color.Parse(UiConstants.ContentTypeMapColor));
    private static readonly SolidColorBrush AddonBrush = new(Color.Parse(UiConstants.ContentTypeAddonColor));
    private static readonly SolidColorBrush ToolBrush = new(Color.Parse(UiConstants.ContentTypeToolColor));
    private static readonly SolidColorBrush BundleBrush = new(Color.Parse(UiConstants.ContentTypeBundleColor));
    private static readonly SolidColorBrush MissionBrush = new(Color.Parse(UiConstants.ContentTypeMissionColor));
    private static readonly SolidColorBrush SkinBrush = new(Color.Parse(UiConstants.ContentTypeSkinColor));

    /// <summary>
    /// Converts a ContentType to a SolidColorBrush.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type (ignored).</param>
    /// <param name="parameter">Optional parameter (ignored).</param>
    /// <param name="culture">The culture (ignored).</param>
    /// <returns>A SolidColorBrush representing the content type color.</returns>
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
}
