using Avalonia.Media;
using GenHub.Core.Constants;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a ContentType enum value to a translucent SolidColorBrush for badge background tinting.
/// </summary>
public class ContentTypeToBadgeBackgroundConverter : ContentTypeToBrushConverterBase
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly ContentTypeToBadgeBackgroundConverter Instance = new();

    private const byte BadgeAlpha = 0x25;

    private static readonly SolidColorBrush GameClientBrushValue = CreateTintBrush(UiConstants.ContentTypeGameClientColor);
    private static readonly SolidColorBrush ModBrushValue = CreateTintBrush(UiConstants.ContentTypeModColor);
    private static readonly SolidColorBrush PatchBrushValue = CreateTintBrush(UiConstants.ContentTypePatchColor);
    private static readonly SolidColorBrush MapBrushValue = CreateTintBrush(UiConstants.ContentTypeMapColor);
    private static readonly SolidColorBrush AddonBrushValue = CreateTintBrush(UiConstants.ContentTypeAddonColor);
    private static readonly SolidColorBrush ToolBrushValue = CreateTintBrush(UiConstants.ContentTypeToolColor);
    private static readonly SolidColorBrush BundleBrushValue = CreateTintBrush(UiConstants.ContentTypeBundleColor);
    private static readonly SolidColorBrush MissionBrushValue = CreateTintBrush(UiConstants.ContentTypeMissionColor);
    private static readonly SolidColorBrush SkinBrushValue = CreateTintBrush(UiConstants.ContentTypeSkinColor);

    /// <inheritdoc />
    protected override SolidColorBrush GameClientBrush => GameClientBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush ModBrush => ModBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush PatchBrush => PatchBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush MapBrush => MapBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush AddonBrush => AddonBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush ToolBrush => ToolBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush BundleBrush => BundleBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush MissionBrush => MissionBrushValue;

    /// <inheritdoc />
    protected override SolidColorBrush SkinBrush => SkinBrushValue;

    private static SolidColorBrush CreateTintBrush(string hex)
    {
        var baseColor = Color.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(BadgeAlpha, baseColor.R, baseColor.G, baseColor.B));
    }
}
