using Avalonia.Media.Imaging;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// A sub-gadget art overlay positioned inside a canvas item (scrollbar pieces, combo button, slider thumb).
/// </summary>
/// <param name="Image">The overlay image.</param>
/// <param name="X">The X position in device-independent pixels relative to the item.</param>
/// <param name="Y">The Y position in device-independent pixels relative to the item.</param>
/// <param name="Width">The width in device-independent pixels.</param>
/// <param name="Height">The height in device-independent pixels.</param>
public sealed record WndCanvasOverlayViewModel(Bitmap Image, double X, double Y, double Width, double Height);
