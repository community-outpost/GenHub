using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GenHub.Core.Models.Enums;
using System;
using System.Globalization;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Base class for converters that map a ContentType to a SolidColorBrush.
/// Subclasses provide the concrete brush values (e.g., solid badges vs subtle backgrounds).
/// </summary>
public abstract class ContentTypeToBrushConverterBase : IValueConverter
{
    /// <summary>Gets the brush for game clients.</summary>
    protected abstract SolidColorBrush GameClientBrush { get; }

    /// <summary>Gets the brush for mods.</summary>
    protected abstract SolidColorBrush ModBrush { get; }

    /// <summary>Gets the brush for patches.</summary>
    protected abstract SolidColorBrush PatchBrush { get; }

    /// <summary>Gets the brush for maps and map packs.</summary>
    protected abstract SolidColorBrush MapBrush { get; }

    /// <summary>Gets the brush for addons.</summary>
    protected abstract SolidColorBrush AddonBrush { get; }

    /// <summary>Gets the brush for tools, standalone executables, and GenHub builds.</summary>
    protected abstract SolidColorBrush ToolBrush { get; }

    /// <summary>Gets the brush for content bundles.</summary>
    protected abstract SolidColorBrush BundleBrush { get; }

    /// <summary>Gets the brush for missions.</summary>
    protected abstract SolidColorBrush MissionBrush { get; }

    /// <summary>Gets the brush for skins and language packs.</summary>
    protected abstract SolidColorBrush SkinBrush { get; }

    /// <summary>
    /// Converts a ContentType to its brush.
    /// </summary>
    /// <param name="value">The ContentType value to convert.</param>
    /// <param name="targetType">The target type (ignored).</param>
    /// <param name="parameter">Optional parameter (ignored).</param>
    /// <param name="culture">The culture (ignored).</param>
    /// <returns>A SolidColorBrush representing the content type.</returns>
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
                ContentType.ModdingTool or ContentType.Executable or ContentType.GenHubBuild => ToolBrush,
                ContentType.ContentBundle => BundleBrush,
                ContentType.Mission => MissionBrush,
                ContentType.Skin or ContentType.LanguagePack => SkinBrush,
                _ => ModBrush,
            };
        }

        return ModBrush;
    }

    /// <summary>
    /// Converts back from a brush to a ContentType (not supported).</summary>
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
