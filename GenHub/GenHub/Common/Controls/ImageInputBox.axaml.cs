using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using GenHub.Common.Helpers;
using System;
using System.Collections.Generic;
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
    /// Represents a selectable built-in asset.
    /// </summary>
    public record BuiltInAssetItem(string Name, string Url);

    /// <summary>
    /// Gets the list of built-in covers and banners available in GenHub.
    /// </summary>
    public static IReadOnlyList<BuiltInAssetItem> BuiltInCovers { get; } =
    [
        new("Zero Hour Cover", "avares://GenHub/Assets/Covers/zerohour-cover.png"),
        new("Generals Cover", "avares://GenHub/Assets/Covers/generals-cover.png"),
        new("Generals Cover 2", "avares://GenHub/Assets/Covers/generals-cover-2.png"),
        new("USA Cover", "avares://GenHub/Assets/Covers/usa-cover.jpg"),
        new("China Cover", "avares://GenHub/Assets/Covers/china-cover.jpg"),
        new("GLA Cover", "avares://GenHub/Assets/Covers/gla-cover.jpg"),
    ];

    /// <summary>
    /// Gets the list of built-in publisher and community logos available in GenHub.
    /// </summary>
    public static IReadOnlyList<BuiltInAssetItem> BuiltInLogos { get; } =
    [
        new("Dominator Logo", "avares://GenHub/Assets/Logos/dominator-logo.png"),
        new("Community Outpost", "avares://GenHub/Assets/Logos/communityoutpost-logo.png"),
        new("Generals Online", "avares://GenHub/Assets/Logos/generalsonline-logo.png"),
        new("TheSuperHackers", "avares://GenHub/Assets/Logos/thesuperhackers-logo.png"),
        new("GenLauncher", "avares://GenHub/Assets/Logos/genlauncher-logo.png"),
        new("GenPatcher", "avares://GenHub/Assets/Logos/genpatcher-logo.png"),
        new("Generals Hub", "avares://GenHub/Assets/Logos/generalshub-logo.png"),
        new("CnC Labs", "avares://GenHub/Assets/Logos/cnclabs-logo.png"),
        new("ModDB", "avares://GenHub/Assets/Logos/moddb-logo.png"),
        new("GitHub", "avares://GenHub/Assets/Logos/github-logo.png"),
        new("AOD Maps", "avares://GenHub/Assets/Logos/aodmaps-logo.png"),
    ];

    private async void OnBuiltInAssetSelected(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string url)
        {
            var assetBtn = this.FindControl<Button>("AssetPickerButton");
            assetBtn?.Flyout?.Hide();
            await ProcessIncomingInputAsync(url);
        }
    }

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

        WirePreviewBorder();
        WireClearButton();
        WirePasteButton();
        WireInputTextBox();
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

    private void WirePreviewBorder()
    {
        var previewBorder = this.FindControl<Border>("PreviewBorder");
        if (previewBorder != null)
        {
            previewBorder.PointerPressed += OnPreviewBorderPointerPressed;
        }
    }

    private void WireClearButton()
    {
        var clearButton = this.FindControl<Button>("ClearButton");
        if (clearButton != null)
        {
            clearButton.Click += OnClearButtonClick;
        }
    }

    private void WirePasteButton()
    {
        var pasteButton = this.FindControl<Button>("PasteButton");
        if (pasteButton != null)
        {
            pasteButton.Click += OnPasteButtonClick;
        }
    }

    private void WireInputTextBox()
    {
        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (inputTextBox == null)
        {
            return;
        }

        inputTextBox.LostFocus += OnInputTextBoxLostFocus;
        inputTextBox.KeyDown += OnInputTextBoxKeyDown;
    }

    private void OnClearButtonClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!_isProcessingInput)
        {
            Text = string.Empty;
        }
    }

    private async void OnInputTextBoxLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_isProcessingInput || DropHandler == null || sender is not TextBox inputTextBox)
        {
            return;
        }

        var newText = inputTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(newText) && File.Exists(newText))
        {
            await ProcessIncomingInputAsync(newText);
        }
    }

    private async void OnInputTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _isProcessingInput || DropHandler == null || sender is not TextBox inputTextBox)
        {
            return;
        }

        var newText = inputTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(newText) && File.Exists(newText))
        {
            e.Handled = true;
            await ProcessIncomingInputAsync(newText);
        }
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
