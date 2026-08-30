using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using GenHub.Infrastructure.Services;

namespace GenHub.Infrastructure.Controls;

/// <summary>
/// Attached property for asynchronously loading and caching image URLs onto Avalonia Image controls.
/// </summary>
public static class ImageLoader
{
    /// <summary>
    /// Identifies the Source attached property.
    /// </summary>
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("Source", typeof(ImageLoader));

    static ImageLoader()
    {
        SourceProperty.Changed.AddClassHandler<Image>(OnSourceChanged);
    }

    /// <summary>
    /// Gets the Source property value.
    /// </summary>
    /// <param name="element">The Image control.</param>
    /// <returns>The string image URL or path.</returns>
    public static string? GetSource(Image element) => element.GetValue(SourceProperty);

    /// <summary>
    /// Sets the Source property value.
    /// </summary>
    /// <param name="element">The Image control.</param>
    /// <param name="value">The string image URL or path.</param>
    public static void SetSource(Image element, string? value) => element.SetValue(SourceProperty, value);

    private static void OnSourceChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        image.AttachedToVisualTree -= OnAttachedToVisualTree;

        var url = e.NewValue as string;
        if (string.IsNullOrWhiteSpace(url))
        {
            image.Source = null;
            return;
        }

        image.AttachedToVisualTree += OnAttachedToVisualTree;
        _ = ApplySourceAsync(image, url);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is not Image image)
        {
            return;
        }

        var url = GetSource(image);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (image.Source != null)
        {
            InvalidateImage(image);
            return;
        }

        _ = ApplySourceAsync(image, url);
    }

    private static async Task ApplySourceAsync(Image image, string url)
    {
        var bitmap = ImageCacheService.Instance.GetBitmapFromMemory(url)
            ?? await ImageCacheService.Instance.GetBitmapAsync(url);

        if (bitmap == null || GetSource(image) != url)
        {
            return;
        }

        void SetBitmap()
        {
            if (GetSource(image) == url)
            {
                image.Source = bitmap;
                InvalidateImage(image);
            }
        }

        if (Dispatcher.UIThread.CheckAccess())
        {
            SetBitmap();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(SetBitmap);
        }
    }

    private static void InvalidateImage(Image image)
    {
        image.InvalidateMeasure();
        image.InvalidateArrange();
        image.InvalidateVisual();
    }
}
