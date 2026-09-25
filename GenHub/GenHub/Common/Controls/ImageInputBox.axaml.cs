using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GenHub.Common.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Common.Controls;

/// <summary>
/// A reusable image input control supporting direct URL/path text entry,
/// file and text drag-and-drop, clipboard paste of raw bitmaps or image URLs,
/// explicit paste button, and clickable thumbnail browse.
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

    private bool _isProcessingInput;

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

        AddHandler(KeyDownEvent, OnControlKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var previewBorder = this.FindControl<Border>("PreviewBorder");
        if (previewBorder != null)
        {
            previewBorder.PointerPressed += OnPreviewBorderPointerPressed;
        }

        var clearButton = this.FindControl<Button>("ClearButton");
        if (clearButton != null)
        {
            clearButton.Click += (s, e) =>
            {
                if (_isProcessingInput)
                {
                    return;
                }

                Text = string.Empty;
            };
        }

        var pasteButton = this.FindControl<Button>("PasteButton");
        if (pasteButton != null)
        {
            pasteButton.Click += OnPasteButtonClick;
        }

        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (inputTextBox != null)
        {
            inputTextBox.LostFocus += async (s, e) =>
            {
                if (!_isProcessingInput && DropHandler != null)
                {
                    var newText = inputTextBox.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(newText) && File.Exists(newText))
                    {
                        await ProcessIncomingInputAsync(newText);
                    }
                }
            };

            inputTextBox.KeyDown += async (s, e) =>
            {
                if (e.Key == Key.Enter && !_isProcessingInput && DropHandler != null)
                {
                    var newText = inputTextBox.Text?.Trim();
                    if (!string.IsNullOrWhiteSpace(newText) && File.Exists(newText))
                    {
                        e.Handled = true;
                        await ProcessIncomingInputAsync(newText);
                    }
                }
            };
        }
    }

    private static void OnDragOver(object? sender, DragEventArgs e)
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

    private static async void OnDrop(object? sender, DragEventArgs e)
    {
        if (sender is not ImageInputBox box)
        {
            return;
        }

        e.Handled = true;
        if (e.Data.Contains(DataFormats.Files))
        {
            var files = e.Data.GetFiles();
            var first = files?.FirstOrDefault();
            if (first != null && !string.IsNullOrWhiteSpace(first.Path.LocalPath))
            {
                await box.ProcessIncomingInputAsync(first.Path.LocalPath);
                return;
            }
        }

        if (e.Data.Contains(DataFormats.Text))
        {
            var text = e.Data.GetText();
            if (!string.IsNullOrWhiteSpace(text))
            {
                await box.ProcessIncomingInputAsync(text.Trim());
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnControlKeyDown(object? sender, KeyEventArgs e)
    {
        var isPaste = (e.Key == Key.V && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
                      || (e.Key == Key.Insert && e.KeyModifiers.HasFlag(KeyModifiers.Shift));

        if (!isPaste)
        {
            return;
        }

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard == null)
        {
            return;
        }

        e.Handled = true;
        await PasteFromClipboardAsync(topLevel.Clipboard);
    }

    private async void OnPasteButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard != null)
        {
            await PasteFromClipboardAsync(topLevel.Clipboard);
        }
    }

    private async Task PasteFromClipboardAsync(Avalonia.Input.Platform.IClipboard clipboard)
    {
        try
        {
            var result = await ClipboardInputHelper.ExtractPastedFileOrImageAsync(clipboard);
            if (!string.IsNullOrWhiteSpace(result))
            {
                await ProcessIncomingInputAsync(result);
            }
            else
            {
                var text = await clipboard.GetTextAsync();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    var inputTextBox = this.FindControl<TextBox>("InputTextBox");
                    if (inputTextBox != null)
                    {
                        var start = inputTextBox.SelectionStart;
                        var end = inputTextBox.SelectionEnd;
                        var current = inputTextBox.Text ?? string.Empty;
                        if (start >= 0 && end >= start && end <= current.Length)
                        {
                            inputTextBox.Text = current.Remove(start, end - start).Insert(start, text);
                            inputTextBox.CaretIndex = start + text.Length;
                        }
                        else
                        {
                            inputTextBox.Text = text;
                            inputTextBox.CaretIndex = text.Length;
                        }
                    }
                    else
                    {
                        Text = text;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to paste clipboard content: {ex.Message}");
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
        if (_isProcessingInput)
        {
            return;
        }

        _isProcessingInput = true;
        try
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
        finally
        {
            _isProcessingInput = false;
        }
    }
}
