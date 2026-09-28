using Avalonia.Media;
using GenHub.Core.Constants;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Converts a ContentType enum value to a SolidColorBrush for UI visual distinction.
/// </summary>
public class ContentTypeToBrushConverter : ContentTypeToBrushConverterBase
{
    /// <summary>
    /// Gets the singleton instance of the converter.
    /// </summary>
    public static readonly ContentTypeToBrushConverter Instance = new();

    private static readonly SolidColorBrush GameClientBrushValue = new(Color.Parse(UiConstants.ContentTypeGameClientColor));
    private static readonly SolidColorBrush ModBrushValue = new(Color.Parse(UiConstants.ContentTypeModColor));
    private static readonly SolidColorBrush PatchBrushValue = new(Color.Parse(UiConstants.ContentTypePatchColor));
    private static readonly SolidColorBrush MapBrushValue = new(Color.Parse(UiConstants.ContentTypeMapColor));
    private static readonly SolidColorBrush AddonBrushValue = new(Color.Parse(UiConstants.ContentTypeAddonColor));
    private static readonly SolidColorBrush ToolBrushValue = new(Color.Parse(UiConstants.ContentTypeToolColor));
    private static readonly SolidColorBrush BundleBrushValue = new(Color.Parse(UiConstants.ContentTypeBundleColor));
    private static readonly SolidColorBrush MissionBrushValue = new(Color.Parse(UiConstants.ContentTypeMissionColor));
    private static readonly SolidColorBrush SkinBrushValue = new(Color.Parse(UiConstants.ContentTypeSkinColor));

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
}
