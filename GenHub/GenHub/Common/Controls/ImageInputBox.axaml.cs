using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Common.Controls;

/// <summary>
/// A reusable image input control supporting direct URL/path text entry,
/// file and text drag-and-drop, clipboard paste of raw bitmaps or image URLs,
/// and clickable thumbnail browse.
/// </summary>
public partial class ImageInputBox : UserControl
{
    /// <summary>
    /// Defines the <see cref="Text"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<ImageInputBox, string?>(
            nameof(Text),
            defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    /// <summary>
    /// Defines the <see cref="Watermark"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> WatermarkProperty =
        AvaloniaProperty.Register<ImageInputBox, string?>(
            nameof(Watermark),
            defaultValue: "Image URL, file path, paste or drop image...");

    /// <summary>
    /// Defines the <see cref="PreviewCornerRadius"/> property.
    /// </summary>
    public static readonly StyledProperty<CornerRadius> PreviewCornerRadiusProperty =
        AvaloniaProperty.Register<ImageInputBox, CornerRadius>(
            nameof(PreviewCornerRadius),
            defaultValue: new CornerRadius(6));

    /// <summary>
    /// Defines the <see cref="FallbackIconData"/> property.
    /// </summary>
    public static readonly StyledProperty<Geometry?> FallbackIconDataProperty =
        AvaloniaProperty.Register<ImageInputBox, Geometry?>(nameof(FallbackIconData));

    /// <summary>
    /// Defines the <see cref="PreviewToolTip"/> property.
    /// </summary>
    public static readonly StyledProperty<string?> PreviewToolTipProperty =
        AvaloniaProperty.Register<ImageInputBox, string?>(
            nameof(PreviewToolTip),
            defaultValue: "Click to browse or drag and drop image here");

    /// <summary>
    /// Defines the <see cref="DropHandler"/> property.
    /// </summary>
    public static readonly StyledProperty<Func<string, Task>?> DropHandlerProperty =
        AvaloniaProperty.Register<ImageInputBox, Func<string, Task>?>(nameof(DropHandler));

    /// <summary>
    /// Gets or sets the image URL or file path.
    /// </summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the watermark text.
    /// </summary>
    public string? Watermark
    {
        get => GetValue(WatermarkProperty);
        set => SetValue(WatermarkProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius of the preview border.
    /// </summary>
    public CornerRadius PreviewCornerRadius
    {
        get => GetValue(PreviewCornerRadiusProperty);
        set => SetValue(PreviewCornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the fallback icon geometry.
    /// </summary>
    public Geometry? FallbackIconData
    {
        get => GetValue(FallbackIconDataProperty);
        set => SetValue(FallbackIconDataProperty, value);
    }

    /// <summary>
    /// Gets or sets the tooltip for the preview thumbnail.
    /// </summary>
    public string? PreviewToolTip
    {
        get => GetValue(PreviewToolTipProperty);
        set => SetValue(PreviewToolTipProperty, value);
    }

    /// <summary>
    /// Gets or sets the custom handler invoked when an image file or URL is dropped or pasted.
    /// </summary>
    public Func<string, Task>? DropHandler
    {
        get => GetValue(DropHandlerProperty);
        set => SetValue(DropHandlerProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageInputBox"/> class.
    /// </summary>
    public ImageInputBox()
    {
        InitializeComponent();

        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);

        var textBox = this.FindControl<TextBox>("InputTextBox");
        if (textBox != null)
        {
            textBox.AddHandler(KeyDownEvent, OnTextBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        var previewBorder = this.FindControl<Border>("PreviewBorder");
        if (previewBorder != null)
        {
            previewBorder.PointerPressed += OnPreviewBorderPointerPressed;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data.Contains(DataFormats.Files) || e.Data.Contains(DataFormats.Text))
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }
        else
        {
            e.DragEffects = DragDropEffects.None;
        }
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        e.Handled = true;
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            var first = files?.FirstOrDefault();
            if (first != null && !string.IsNullOrWhiteSpace(first.Path.LocalPath))
            {
                await ProcessIncomingInputAsync(first.Path.LocalPath);
                return;
            }
        }

        if (e.Data.Contains(DataFormats.Text))
        {
            var text = e.Data.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                await ProcessIncomingInputAsync(text.Trim());
            }
        }
    }

    private async void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            try
            {
                var formats = await topLevel.Clipboard.GetFormatsAsync();

                // 1. Raw bitmap or image format in clipboard
                var imageFormat = formats.FirstOrDefault(f =>
                    f.Contains("png", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("jpeg", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("bitmap", StringComparison.OrdinalIgnoreCase) ||
                    f.Contains("image", StringComparison.OrdinalIgnoreCase));

                if (imageFormat != null)
                {
                    var data = await topLevel.Clipboard.GetDataAsync(imageFormat);
                    byte[]? bytes = null;
                    if (data is byte[] b)
                    {
                        bytes = b;
                    }
                    else if (data is MemoryStream ms)
                    {
                        bytes = ms.ToArray();
                    }
                    else if (data is Stream s)
                    {
                        using var ms2 = new MemoryStream();
                        await s.CopyToAsync(ms2);
                        bytes = ms2.ToArray();
                    }

                    if (bytes != null && bytes.Length > 0)
                    {
                        e.Handled = true;
                        var tempFile = Path.Combine(Path.GetTempPath(), $"genhub_pasted_{Guid.NewGuid():N}.png");
                        await File.WriteAllBytesAsync(tempFile, bytes);
                        await ProcessIncomingInputAsync(tempFile);
                        return;
                    }
                }

                // 2. Files in clipboard
                var files = await topLevel.Clipboard.GetDataAsync(DataFormats.Files);
                if (files is IEnumerable<IStorageItem> storageItems)
                {
                    var first = storageItems.FirstOrDefault();
                    if (first != null && !string.IsNullOrWhiteSpace(first.Path.LocalPath))
                    {
                        e.Handled = true;
                        await ProcessIncomingInputAsync(first.Path.LocalPath);
                        return;
                    }
                }
                else if (files is IEnumerable<string> filePaths)
                {
                    var first = filePaths.FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(first))
                    {
                        e.Handled = true;
                        await ProcessIncomingInputAsync(first);
                        return;
                    }
                }

                // 3. Text/URL in clipboard
                var text = await topLevel.Clipboard.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var trimmed = text.Trim();
                    if (Uri.TryCreate(trimmed, UriKind.Absolute, out _) || File.Exists(trimmed))
                    {
                        e.Handled = true;
                        await ProcessIncomingInputAsync(trimmed);
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to paste clipboard content: {ex.Message}");
            }
        }
    }

    private async void OnPreviewBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null)
        {
            return;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select Image",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Images")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif", "*.svg", "*.ico"],
                },
            ],
        });

        if (files.Count > 0 && !string.IsNullOrWhiteSpace(files[0].Path.LocalPath))
        {
            await ProcessIncomingInputAsync(files[0].Path.LocalPath);
        }
    }

    private async Task ProcessIncomingInputAsync(string fileOrUrl)
    {
        if (DropHandler != null)
        {
            await DropHandler(fileOrUrl);
        }
        else
        {
            Text = fileOrUrl;
        }
    }
}
