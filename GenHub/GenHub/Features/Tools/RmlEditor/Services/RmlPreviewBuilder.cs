using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Tools.RmlEditor;
using GenHub.Core.Models.Tools.RmlEditor;
using GenHub.Core.Services.Tools.RmlEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using Span = Avalonia.Controls.Documents.Span;

namespace GenHub.Features.Tools.RmlEditor.Services;

/// <summary>
/// Builds an Avalonia approximation of an interface document for the editor canvas.
/// The preview favors editability over pixel fidelity: flow layout, colors, text,
/// and images render closely while absolute positioning, decorators, animations,
/// and data bindings are approximated or skipped.
/// </summary>
public sealed class RmlPreviewBuilder(IRcssDocumentService rcssService, ILocalizationService localization)
{
    private sealed class BuildContext
    {
        public BuildContext(RmlPreviewOptions options)
        {
            Options = options;
        }

        public RmlPreviewOptions Options { get; }

        public Dictionary<Guid, Control> Controls { get; } = [];

        public Dictionary<Guid, RmlComputedStyle> Styles { get; } = [];

        public List<string> MissingImages { get; } = [];
    }

    private const double DefaultSurfaceHeight = 400.0;
    private const double MissingImageWidth = 160.0;
    private const double MissingImageHeight = 120.0;
    private const double HiddenPlaceholderHeight = 28.0;
    private const double BreakSpacing = 8.0;

    /// <summary>
    /// Builds the preview control tree for a document.
    /// </summary>
    /// <param name="document">The interface document.</param>
    /// <param name="options">The preview options.</param>
    /// <returns>The preview result.</returns>
    public RmlPreviewResult Build(RmlDocument document, RmlPreviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        var context = new BuildContext(options);
        var ancestors = new List<RmlElement>();
        var bodyStyle = ComputeElementStyle(document.Body, ancestors, options);
        context.Styles[document.Body.Id] = bodyStyle;

        var host = BuildChildHost(document.Body, ancestors, options.BaseFontSize, context);
        var body = new Border
        {
            Child = host,
            Background = ResolveBackground(bodyStyle, context),
            Padding = ResolvePadding(bodyStyle, options.BaseFontSize),
        };
        ApplySize(body, bodyStyle, options.BaseFontSize);
        var bodyFrame = WrapSelectable(body, document.Body.Id, context);
        context.Controls[document.Body.Id] = bodyFrame;

        var root = new Border
        {
            Width = options.SurfaceWidth,
            MinHeight = DefaultSurfaceHeight,
            Background = body.Background ?? ThemeBrush(ThemeResourceKeys.SurfaceBackgroundBrush, new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x14))),
            Child = bodyFrame,
        };

        if (host is Panel panel && panel.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localization.GetString("Tools.RmlEditor.Preview.EmptyBody"),
                Foreground = ThemeBrush(ThemeResourceKeys.TextSecondary, new SolidColorBrush(Colors.Gray)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 32),
            });
        }

        return new RmlPreviewResult
        {
            Root = root,
            ElementControls = context.Controls,
            ComputedStyles = context.Styles,
            MissingImages = context.MissingImages.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
        };
    }

    private static bool IsHidden(RmlComputedStyle style)
    {
        var display = style.GetProperty(RmlConstants.StyleProperties.Display);
        if (string.Equals(display, RmlConstants.Values.None, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var visibility = style.GetProperty(RmlConstants.StyleProperties.Visibility);
        return string.Equals(visibility, RmlConstants.Values.Hidden, StringComparison.OrdinalIgnoreCase)
            || string.Equals(visibility, RmlConstants.Values.Collapse, StringComparison.OrdinalIgnoreCase);
    }

    private static Control WrapSelectable(Control control, Guid id, BuildContext context)
    {
        var options = context.Options;
        var selected = options.SelectedId == id;
        var frame = new Border
        {
            Background = Brushes.Transparent,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1),
            BorderBrush = selected ? ThemeBrush(ThemeResourceKeys.AccentBrush, new SolidColorBrush(Color.FromRgb(0xE8, 0x6C, 0x1A))) : Brushes.Transparent,
            Child = control,
            Cursor = new Cursor(StandardCursorType.Hand),
            Focusable = false,
        };
        frame.AddHandler(InputElement.PointerPressedEvent, (_, e) => OnPreviewPressed(e, id, options), RoutingStrategies.Tunnel);
        return frame;
    }

    private static void OnPreviewPressed(PointerPressedEventArgs e, Guid id, RmlPreviewOptions options)
    {
        e.Handled = true;
        options.ElementPressed?.Invoke(id);
    }

    private static Panel CreateFlowPanel(RmlComputedStyle style)
    {
        var direction = style.GetProperty(RmlConstants.StyleProperties.FlexDirection);
        var horizontal = string.Equals(direction, RmlConstants.Values.Row, StringComparison.OrdinalIgnoreCase)
            || string.Equals(direction, RmlConstants.Values.RowReverse, StringComparison.OrdinalIgnoreCase);
        var wrap = style.GetProperty(RmlConstants.StyleProperties.FlexWrap);
        var shouldWrap = !string.Equals(wrap, RmlConstants.Values.NoWrap, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(wrap);

        Panel panel = shouldWrap
            ? new WrapPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical }
            : new StackPanel { Orientation = horizontal ? Orientation.Horizontal : Orientation.Vertical };

        var gap = style.GetProperty(RmlConstants.StyleProperties.Gap);
        if (panel is StackPanel stack && RmlLength.TryParse(gap, 16.0, out var length) && length.Unit == RmlLengthUnit.Px)
        {
            stack.Spacing = ClampPreviewDimension(length.Value);
        }

        return panel;
    }

    private static Control WrapOverflow(Panel panel, RmlComputedStyle style)
    {
        var overflow = style.GetProperty(RmlConstants.StyleProperties.Overflow);
        var overflowX = style.GetProperty(RmlConstants.StyleProperties.OverflowX);
        var overflowY = style.GetProperty(RmlConstants.StyleProperties.OverflowY);
        if (string.IsNullOrWhiteSpace(overflow) && string.IsNullOrWhiteSpace(overflowX) && string.IsNullOrWhiteSpace(overflowY))
        {
            return panel;
        }

        return new ScrollViewer
        {
            Content = panel,
            HorizontalScrollBarVisibility = ToScrollVisibility(overflowX ?? overflow),
            VerticalScrollBarVisibility = ToScrollVisibility(overflowY ?? overflow),
        };
    }

    private static ScrollBarVisibility ToScrollVisibility(string? overflow)
    {
        return overflow?.ToLowerInvariant() switch
        {
            RmlConstants.Values.Hidden => ScrollBarVisibility.Hidden,
            RmlConstants.Values.Scroll => ScrollBarVisibility.Visible,
            RmlConstants.Values.Auto => ScrollBarVisibility.Auto,
            _ => ScrollBarVisibility.Disabled,
        };
    }

    private static TextBlock CreateTextBlock(string text, RmlElement element, RmlComputedStyle style, double fontSize)
    {
        var block = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
        };
        ApplyTextStyle(block, element, style, fontSize);
        if (text.Length > 0)
        {
            block.Text = ApplyTextTransform(CollapseWhitespace(text), style);
        }

        return block;
    }

    private static bool IsInlineNode(RmlNode node)
    {
        return node switch
        {
            RmlText text => !string.IsNullOrWhiteSpace(text.Text),
            RmlElement element => IsInlineTag(element.Tag),
            _ => false,
        };
    }

    private static bool IsInlineTag(string tag)
    {
        return string.Equals(tag, RmlConstants.Elements.Span, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.Anchor, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.Emphasis, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.Strong, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.Code, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.Label, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.Break, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tag, RmlConstants.Elements.Image, StringComparison.OrdinalIgnoreCase);
    }

    private static string CollapseWhitespace(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

    private static double ResolveImageDimension(string? attribute, string? styleValue, double fontSize, double fallback)
    {
        if (RmlLength.TryParse(styleValue, fontSize, out var styleLength) && styleLength.Unit == RmlLengthUnit.Px && styleLength.Value > 0)
        {
            return ClampPreviewDimension(styleLength.Value);
        }

        if (RmlLength.TryParse(attribute, fontSize, out var attributeLength) && attributeLength.Unit == RmlLengthUnit.Px && attributeLength.Value > 0)
        {
            return ClampPreviewDimension(attributeLength.Value);
        }

        return fallback;
    }

    private static void ApplyImageSize(Image image, RmlElement element, RmlComputedStyle style, double fontSize)
    {
        var width = style.GetProperty(RmlConstants.StyleProperties.Width) ?? element.GetAttribute(RmlConstants.Attributes.Width);
        if (RmlLength.TryParse(width, fontSize, out var parsedWidth) && parsedWidth.Unit == RmlLengthUnit.Px && parsedWidth.Value > 0)
        {
            image.Width = ClampPreviewDimension(parsedWidth.Value);
        }

        var height = style.GetProperty(RmlConstants.StyleProperties.Height) ?? element.GetAttribute(RmlConstants.Attributes.Height);
        if (RmlLength.TryParse(height, fontSize, out var parsedHeight) && parsedHeight.Unit == RmlLengthUnit.Px && parsedHeight.Value > 0)
        {
            image.Height = ClampPreviewDimension(parsedHeight.Value);
        }
    }

    private static Stretch ResolveStretch(RmlComputedStyle style)
    {
        return style.GetProperty(RmlConstants.StyleProperties.ObjectFit)?.ToLowerInvariant() switch
        {
            RmlConstants.Values.Contain or RmlConstants.Values.ScaleDown => Stretch.Uniform,
            RmlConstants.Values.Cover => Stretch.UniformToFill,
            RmlConstants.Values.None => Stretch.None,
            _ => Stretch.Fill,
        };
    }

    private static Control BuildTextInput(RmlElement element)
    {
        var type = (element.GetAttribute(RmlConstants.Attributes.Type) ?? RmlConstants.InputTypes.Text).ToLowerInvariant();
        var box = new TextBox
        {
            Text = element.GetAttribute(RmlConstants.Attributes.Value) ?? string.Empty,
            Watermark = element.GetAttribute(RmlConstants.Attributes.Placeholder) ?? string.Empty,
            IsReadOnly = element.HasAttribute(RmlConstants.Attributes.ReadOnly),
        };
        if (string.Equals(type, RmlConstants.InputTypes.Password, StringComparison.OrdinalIgnoreCase))
        {
            box.PasswordChar = '*';
        }

        if (int.TryParse(element.GetAttribute(RmlConstants.Attributes.MaxLength), out var maxLength) && maxLength > 0)
        {
            box.MaxLength = maxLength;
        }

        return box;
    }

    private static Control BuildSlider(RmlElement element)
    {
        var slider = new Slider { Minimum = 0, Maximum = 100 };
        if (double.TryParse(element.GetAttribute(RmlConstants.Attributes.Min), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var min))
        {
            slider.Minimum = min;
        }

        if (double.TryParse(element.GetAttribute(RmlConstants.Attributes.Max), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max) && max > slider.Minimum)
        {
            slider.Maximum = max;
        }

        if (double.TryParse(element.GetAttribute(RmlConstants.Attributes.Value), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            slider.Value = Math.Clamp(value, slider.Minimum, slider.Maximum);
        }

        return slider;
    }

    private static Control BuildSelect(RmlElement element, RmlComputedStyle style, double fontSize)
    {
        var items = new List<string>();
        var selected = 0;
        CollectOptions(element, items, ref selected);

        Control control;
        if (int.TryParse(element.GetAttribute(RmlConstants.Attributes.Size), out var size) && size > 1)
        {
            control = new ListBox { ItemsSource = items, SelectedIndex = selected < items.Count ? selected : -1, MaxHeight = ClampPreviewDimension(size * (fontSize + 8)) };
        }
        else
        {
            control = new ComboBox { ItemsSource = items, SelectedIndex = selected < items.Count ? selected : -1 };
        }

        control.SetValue(TextElement.FontSizeProperty, fontSize);
        var border = new Border { Child = control };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private static void CollectOptions(RmlElement element, List<string> items, ref int selected)
    {
        foreach (var child in element.Elements)
        {
            if (string.Equals(child.Tag, RmlConstants.Elements.Option, StringComparison.OrdinalIgnoreCase))
            {
                var label = CollapseWhitespace(child.InnerText);
                if (label.Length == 0)
                {
                    label = child.GetAttribute(RmlConstants.Attributes.Value) ?? string.Empty;
                }

                if (child.HasAttribute(RmlConstants.Attributes.Selected))
                {
                    selected = items.Count;
                }

                items.Add(label);
            }
            else if (string.Equals(child.Tag, RmlConstants.Elements.OptionGroup, StringComparison.OrdinalIgnoreCase))
            {
                CollectOptions(child, items, ref selected);
            }
        }
    }

    private static Control BuildTextArea(RmlElement element, RmlComputedStyle style, double fontSize)
    {
        var box = new TextBox
        {
            Text = element.InnerText.Trim(),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontSize = fontSize,
            IsReadOnly = element.HasAttribute(RmlConstants.Attributes.ReadOnly),
        };
        if (int.TryParse(element.GetAttribute(RmlConstants.Attributes.Rows), out var rows) && rows > 0)
        {
            box.MinHeight = rows * (fontSize + 6);
        }

        var border = new Border { Child = box };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private static List<List<RmlElement>> CollectTableRows(RmlElement table)
    {
        var rows = new List<List<RmlElement>>();
        foreach (var section in table.Elements)
        {
            if (string.Equals(section.Tag, RmlConstants.Elements.TableRow, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(CollectRowCells(section));
            }
            else if (string.Equals(section.Tag, RmlConstants.Elements.TableHead, StringComparison.OrdinalIgnoreCase)
                || string.Equals(section.Tag, RmlConstants.Elements.TableBody, StringComparison.OrdinalIgnoreCase)
                || string.Equals(section.Tag, RmlConstants.Elements.TableFoot, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var row in section.Elements.Where(e => string.Equals(e.Tag, RmlConstants.Elements.TableRow, StringComparison.OrdinalIgnoreCase)))
                {
                    rows.Add(CollectRowCells(row));
                }
            }
        }

        return rows;
    }

    private static List<RmlElement> CollectRowCells(RmlElement row)
    {
        return row.Elements
            .Where(e => string.Equals(e.Tag, RmlConstants.Elements.TableCell, StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Tag, RmlConstants.Elements.TableHeaderCell, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static Control BuildProgress(RmlElement element, RmlComputedStyle style, double fontSize)
    {
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, FontSize = fontSize };
        if (double.TryParse(element.GetAttribute(RmlConstants.Attributes.Max), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var max) && max > 0)
        {
            progress.Maximum = max;
        }

        if (double.TryParse(element.GetAttribute(RmlConstants.Attributes.Value), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            progress.Value = Math.Clamp(value, progress.Minimum, progress.Maximum);
        }

        var border = new Border { Child = progress };
        ApplySize(border, style, fontSize);
        return border;
    }

    private static void ApplyBox(Border border, RmlComputedStyle style, double fontSize)
    {
        ApplySize(border, style, fontSize);
        border.Margin = ResolveMargin(style, fontSize);
        border.Opacity = ResolveOpacity(style);
        ApplyBorder(border, style, fontSize);
        ApplyCursor(border, style);

        if (RmlLength.TryParse(style.GetProperty(RmlConstants.StyleProperties.BorderRadius), fontSize, out var radius) && radius.Unit == RmlLengthUnit.Px)
        {
            border.CornerRadius = new CornerRadius(Math.Max(0, radius.Value));
        }
    }

    private static void ApplySize(Border border, RmlComputedStyle style, double fontSize)
    {
        if (RmlLength.TryParse(style.GetProperty(RmlConstants.StyleProperties.Width), fontSize, out var width))
        {
            if (width.Unit == RmlLengthUnit.Px)
            {
                border.Width = ClampPreviewDimension(width.Value);
            }
            else if (width.Unit == RmlLengthUnit.Percent)
            {
                border.HorizontalAlignment = HorizontalAlignment.Stretch;
            }
        }

        if (RmlLength.TryParse(style.GetProperty(RmlConstants.StyleProperties.Height), fontSize, out var height))
        {
            if (height.Unit == RmlLengthUnit.Px)
            {
                border.Height = ClampPreviewDimension(height.Value);
            }
            else if (height.Unit == RmlLengthUnit.Percent)
            {
                border.VerticalAlignment = VerticalAlignment.Stretch;
            }
        }

        if (RmlLength.TryParse(style.GetProperty(RmlConstants.StyleProperties.MinWidth), fontSize, out var minWidth) && minWidth.Unit == RmlLengthUnit.Px)
        {
            border.MinWidth = ClampPreviewDimension(minWidth.Value);
        }

        if (RmlLength.TryParse(style.GetProperty(RmlConstants.StyleProperties.MinHeight), fontSize, out var minHeight) && minHeight.Unit == RmlLengthUnit.Px)
        {
            border.MinHeight = ClampPreviewDimension(minHeight.Value);
        }

        if (RmlLength.TryParse(style.GetProperty(RmlConstants.StyleProperties.MaxWidth), fontSize, out var maxWidth) && maxWidth.Unit == RmlLengthUnit.Px && maxWidth.Value > 0)
        {
            border.MaxWidth = ClampPreviewDimension(maxWidth.Value);
        }

        if (RmlLength.TryParse(style.GetProperty(RmlConstants.StyleProperties.MaxHeight), fontSize, out var maxHeight) && maxHeight.Unit == RmlLengthUnit.Px && maxHeight.Value > 0)
        {
            border.MaxHeight = ClampPreviewDimension(maxHeight.Value);
        }
    }

    private static double ResolveBorderWidth(RmlComputedStyle style, double fontSize)
    {
        var width = style.GetProperty(RmlConstants.StyleProperties.BorderWidth);
        if (!string.IsNullOrWhiteSpace(width))
        {
            var first = width.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (RmlLength.TryParse(first, fontSize, out var parsed) && parsed.Unit == RmlLengthUnit.Px)
            {
                return Math.Max(0, parsed.Value);
            }
        }

        var shorthand = style.GetProperty(RmlConstants.StyleProperties.Border);
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            foreach (var token in shorthand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (RmlLength.TryParse(token, fontSize, out var parsed) && parsed.Unit == RmlLengthUnit.Px)
                {
                    return Math.Max(0, parsed.Value);
                }
            }
        }

        return 0;
    }

    private static SolidColorBrush? ResolveBorderColor(RmlComputedStyle style)
    {
        var color = style.GetProperty(RmlConstants.StyleProperties.BorderColor);
        if (RmlColors.TryParse(color, out var parsed))
        {
            return new SolidColorBrush(parsed);
        }

        var shorthand = style.GetProperty(RmlConstants.StyleProperties.Border);
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            foreach (var token in shorthand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            {
                if (RmlColors.TryParse(token, out var shorthandColor))
                {
                    return new SolidColorBrush(shorthandColor);
                }
            }
        }

        return null;
    }

    private static void ApplyCursor(Control control, RmlComputedStyle style)
    {
        var cursor = style.GetProperty(RmlConstants.StyleProperties.Cursor)?.ToLowerInvariant();
        if (string.Equals(cursor, RmlConstants.Values.Pointer, StringComparison.OrdinalIgnoreCase))
        {
            control.Cursor = new Cursor(StandardCursorType.Hand);
        }
        else if (string.Equals(cursor, RmlConstants.Values.Text, StringComparison.OrdinalIgnoreCase))
        {
            control.Cursor = new Cursor(StandardCursorType.Ibeam);
        }
    }

    private static double ResolveOpacity(RmlComputedStyle style)
    {
        var opacity = style.GetProperty(RmlConstants.StyleProperties.Opacity);
        if (double.TryParse(opacity, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            return Math.Clamp(value, 0, 1);
        }

        return 1;
    }

    private static void ApplyTextStyle(TextBlock block, RmlElement element, RmlComputedStyle style, double fontSize)
    {
        ApplyFont(block, element, style, fontSize);
        block.TextAlignment = ResolveTextAlignment(style);
        block.LineHeight = ResolveLineHeight(style, fontSize);
        if (string.Equals(style.GetProperty(RmlConstants.StyleProperties.WhiteSpace), RmlConstants.Values.NoWrap, StringComparison.OrdinalIgnoreCase))
        {
            block.TextWrapping = TextWrapping.NoWrap;
        }
    }

    private static void ApplyFont(TextBlock block, RmlElement element, RmlComputedStyle style, double fontSize)
    {
        if (RmlColors.TryParse(style.GetProperty(RmlConstants.StyleProperties.Color), out var color))
        {
            block.Foreground = new SolidColorBrush(color);
        }

        block.FontWeight = ResolveFontWeight(element, style);
        block.FontStyle = ResolveFontStyle(style);
        var family = ResolveFontFamily(style);
        if (family != null)
        {
            block.FontFamily = family;
        }

        block.TextDecorations = ResolveTextDecorations(style);
    }

    private static FontWeight ResolveFontWeight(RmlElement element, RmlComputedStyle style)
    {
        var weight = style.GetProperty(RmlConstants.StyleProperties.FontWeight)?.ToLowerInvariant();
        if (string.Equals(weight, RmlConstants.Values.Bold, StringComparison.OrdinalIgnoreCase)
            || string.Equals(weight, RmlConstants.Values.Bolder, StringComparison.OrdinalIgnoreCase))
        {
            return FontWeight.Bold;
        }

        if (int.TryParse(weight, out var numeric))
        {
            return numeric >= 600 ? FontWeight.Bold : FontWeight.Normal;
        }

        var tag = element.Tag.ToLowerInvariant();
        return tag is RmlConstants.Elements.Heading1 or RmlConstants.Elements.Heading2 or RmlConstants.Elements.Heading3 or RmlConstants.Elements.Heading4 or RmlConstants.Elements.Strong or RmlConstants.Elements.TableHeaderCell ? FontWeight.Bold : FontWeight.Normal;
    }

    private static FontStyle ResolveFontStyle(RmlComputedStyle style)
    {
        var fontStyle = style.GetProperty(RmlConstants.StyleProperties.FontStyle)?.ToLowerInvariant();
        return string.Equals(fontStyle, RmlConstants.Values.Italic, StringComparison.OrdinalIgnoreCase)
            || string.Equals(fontStyle, RmlConstants.Values.Oblique, StringComparison.OrdinalIgnoreCase)
            ? FontStyle.Italic
            : FontStyle.Normal;
    }

    private static FontFamily? ResolveFontFamily(RmlComputedStyle style)
    {
        var family = style.GetProperty(RmlConstants.StyleProperties.FontFamily);
        if (string.IsNullOrWhiteSpace(family))
        {
            return null;
        }

        var first = family.Split(',').Select(part => part.Trim().Trim('"', '\'')).FirstOrDefault(part => part.Length > 0);
        return string.IsNullOrEmpty(first) ? null : new FontFamily(first);
    }

    private static TextDecorationCollection? ResolveTextDecorations(RmlComputedStyle style)
    {
        var decoration = style.GetProperty(RmlConstants.StyleProperties.TextDecoration)?.ToLowerInvariant() ?? string.Empty;
        if (decoration.Contains(RmlConstants.Values.Underline, StringComparison.OrdinalIgnoreCase))
        {
            return TextDecorations.Underline;
        }

        if (decoration.Contains(RmlConstants.Values.LineThrough, StringComparison.OrdinalIgnoreCase))
        {
            return TextDecorations.Strikethrough;
        }

        return null;
    }

    private static TextAlignment ResolveTextAlignment(RmlComputedStyle style)
    {
        return style.GetProperty(RmlConstants.StyleProperties.TextAlign)?.ToLowerInvariant() switch
        {
            RmlConstants.Values.Center => TextAlignment.Center,
            RmlConstants.Values.Right or RmlConstants.Values.End => TextAlignment.Right,
            RmlConstants.Values.Justify => TextAlignment.Justify,
            _ => TextAlignment.Left,
        };
    }

    private static double ResolveLineHeight(RmlComputedStyle style, double fontSize)
    {
        var lineHeight = style.GetProperty(RmlConstants.StyleProperties.LineHeight);
        if (string.IsNullOrWhiteSpace(lineHeight))
        {
            return double.NaN;
        }

        if (RmlLength.TryParse(lineHeight, fontSize, out var length) && length.Unit == RmlLengthUnit.Px)
        {
            return Math.Max(1, length.Value);
        }

        if (double.TryParse(lineHeight.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var multiplier))
        {
            return Math.Max(1, multiplier * fontSize);
        }

        return double.NaN;
    }

    private static void ApplyButtonStyle(Button button, RmlComputedStyle style, double fontSize)
    {
        if (RmlColors.TryParse(style.GetProperty(RmlConstants.StyleProperties.Color), out var color))
        {
            button.Foreground = new SolidColorBrush(color);
        }

        var background = ResolveBackground(style, null);
        if (background != null)
        {
            button.Background = background;
        }

        var padding = ResolvePadding(style, fontSize);
        if (padding != new Thickness(0))
        {
            button.Padding = padding;
        }
    }

    private static double ResolveFontSize(RmlComputedStyle style, double inheritedFontSize, double baseFontSize)
    {
        var size = style.GetProperty(RmlConstants.StyleProperties.FontSize);
        if (string.IsNullOrWhiteSpace(size))
        {
            return inheritedFontSize;
        }

        size = size.Trim().ToLowerInvariant();
        if (string.Equals(size, RmlConstants.Values.Larger, StringComparison.OrdinalIgnoreCase))
        {
            return inheritedFontSize * 1.2;
        }

        if (string.Equals(size, RmlConstants.Values.Smaller, StringComparison.OrdinalIgnoreCase))
        {
            return inheritedFontSize * 0.85;
        }

        var named = size switch
        {
            RmlConstants.Values.XxSmall => baseFontSize * 0.6,
            RmlConstants.Values.XSmall => baseFontSize * 0.75,
            RmlConstants.Values.Small => baseFontSize * 0.85,
            RmlConstants.Values.Medium => baseFontSize,
            RmlConstants.Values.Large => baseFontSize * 1.15,
            RmlConstants.Values.XLarge => baseFontSize * 1.3,
            RmlConstants.Values.XxLarge => baseFontSize * 1.6,
            _ => (double?)null,
        };
        if (named.HasValue)
        {
            return named.Value;
        }

        return RmlLength.TryParse(size, inheritedFontSize, out var length) && length.Unit == RmlLengthUnit.Px
            ? Math.Clamp(length.Value, 1, RmlConstants.Editor.MaxPreviewFontSize)
            : inheritedFontSize;
    }

    private static IBrush? ResolveBackground(RmlComputedStyle style, BuildContext? context)
    {
        if (RmlColors.TryParse(style.GetProperty(RmlConstants.StyleProperties.BackgroundColor), out var solid))
        {
            return solid.A == 0 ? null : new SolidColorBrush(solid);
        }

        var shorthand = style.GetProperty(RmlConstants.StyleProperties.Background);
        if (string.IsNullOrWhiteSpace(shorthand))
        {
            return null;
        }

        var image = ExtractBackgroundImage(shorthand, style, context);
        if (image != null)
        {
            return image;
        }

        foreach (var token in shorthand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (RmlColors.TryParse(token, out var color) && color.A != 0)
            {
                return new SolidColorBrush(color);
            }
        }

        return null;
    }

    private static double ClampPreviewDimension(double value)
    {
        return Math.Clamp(value, 0, RmlConstants.Editor.MaxPreviewDimension);
    }

    private static IBrush ThemeBrush(string key, IBrush fallback)
    {
        if (Avalonia.Application.Current?.TryGetResource(key, theme: null, out var resource) == true && resource is IBrush brush)
        {
            return brush;
        }

        return fallback;
    }

    private static ImageBrush? ExtractBackgroundImage(string shorthand, RmlComputedStyle style, BuildContext? context)
    {
        var start = shorthand.IndexOf(RmlConstants.CssFunctions.UrlPrefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0 || context == null)
        {
            return null;
        }

        var contentStart = start + RmlConstants.CssFunctions.UrlPrefix.Length;
        var end = FindUrlEnd(shorthand, contentStart);
        if (end < 0)
        {
            return null;
        }

        var reference = shorthand.Substring(contentStart, end - contentStart).Trim().Trim('"', '\'');
        if (!context.Options.Images.TryGetValue(reference, out var bitmap) || bitmap == null)
        {
            if (!string.IsNullOrWhiteSpace(reference))
            {
                context.MissingImages.Add(reference);
            }

            return null;
        }

        return new ImageBrush(bitmap)
        {
            Stretch = style.GetProperty(RmlConstants.StyleProperties.BackgroundSize)?.ToLowerInvariant() switch
            {
                RmlConstants.Values.Contain => Stretch.Uniform,
                RmlConstants.Values.Cover => Stretch.UniformToFill,
                _ => Stretch.UniformToFill,
            },
        };
    }

    private static int FindUrlEnd(string shorthand, int contentStart)
    {
        var quote = '\0';
        var depth = 0;
        for (var i = contentStart; i < shorthand.Length; i++)
        {
            var c = shorthand[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c == '"' || c == '\'')
            {
                quote = c;
            }
            else if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }
            else if (c == ')')
            {
                return i;
            }
        }

        return -1;
    }

    private static Thickness ResolvePadding(RmlComputedStyle style, double fontSize)
    {
        return ResolveSides(
            style.GetProperty(RmlConstants.StyleProperties.Padding),
            style.GetProperty(RmlConstants.StyleProperties.PaddingTop),
            style.GetProperty(RmlConstants.StyleProperties.PaddingRight),
            style.GetProperty(RmlConstants.StyleProperties.PaddingBottom),
            style.GetProperty(RmlConstants.StyleProperties.PaddingLeft),
            fontSize);
    }

    private static Thickness ResolveMargin(RmlComputedStyle style, double fontSize)
    {
        var margin = ResolveSides(
            style.GetProperty(RmlConstants.StyleProperties.Margin),
            style.GetProperty(RmlConstants.StyleProperties.MarginTop),
            style.GetProperty(RmlConstants.StyleProperties.MarginRight),
            style.GetProperty(RmlConstants.StyleProperties.MarginBottom),
            style.GetProperty(RmlConstants.StyleProperties.MarginLeft),
            fontSize);

        var position = style.GetProperty(RmlConstants.StyleProperties.Position);
        if (string.Equals(position, RmlConstants.Values.Absolute, StringComparison.OrdinalIgnoreCase))
        {
            var left = ResolveOffset(style.GetProperty(RmlConstants.StyleProperties.Left), fontSize);
            var top = ResolveOffset(style.GetProperty(RmlConstants.StyleProperties.Top), fontSize);
            margin = new Thickness(margin.Left + left, margin.Top + top, margin.Right, margin.Bottom);
        }

        return margin;
    }

    private static double ResolveOffset(string? value, double fontSize)
    {
        return RmlLength.TryParse(value, fontSize, out var length) && length.Unit == RmlLengthUnit.Px ? ClampPreviewDimension(length.Value) : 0;
    }

    private static Thickness ResolveSides(string? shorthand, string? top, string? right, string? bottom, string? left, double fontSize)
    {
        var sides = new double[4];
        if (!string.IsNullOrWhiteSpace(shorthand))
        {
            var parts = shorthand.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var values = parts.Select(part => ResolveSideValue(part, fontSize)).ToList();
            if (values.Count > 0)
            {
                sides[0] = values[0];
                sides[1] = values.Count > 1 ? values[1] : values[0];
                sides[2] = values.Count > 2 ? values[2] : values[0];
                sides[3] = values.Count > 3 ? values[3] : sides[1];
            }
        }

        sides[0] = ResolveSideOverride(top, fontSize, sides[0]);
        sides[1] = ResolveSideOverride(right, fontSize, sides[1]);
        sides[2] = ResolveSideOverride(bottom, fontSize, sides[2]);
        sides[3] = ResolveSideOverride(left, fontSize, sides[3]);
        return new Thickness(sides[3], sides[0], sides[1], sides[2]);
    }

    private static double ResolveSideOverride(string? value, double fontSize, double current)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return current;
        }

        return ResolveSideValue(value, fontSize);
    }

    private static double ResolveSideValue(string value, double fontSize)
    {
        return RmlLength.TryParse(value, fontSize, out var length) && length.Unit == RmlLengthUnit.Px ? ClampPreviewDimension(length.Value) : 0;
    }

    private static string ApplyTextTransform(string text, RmlComputedStyle style)
    {
        var transform = style.GetProperty(RmlConstants.StyleProperties.TextTransform)?.ToLowerInvariant();
        return transform switch
        {
            RmlConstants.Values.Uppercase => text.ToUpperInvariant(),
            RmlConstants.Values.Lowercase => text.ToLowerInvariant(),
            RmlConstants.Values.Capitalize => System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(text.ToLowerInvariant()),
            _ => text,
        };
    }

    private static string? FirstToken(string? value)
    {
        return value?.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
    }

    private static void ApplyBorder(Border border, RmlComputedStyle style, double fontSize)
    {
        var borderStyle = style.GetProperty(RmlConstants.StyleProperties.BorderStyle)
            ?? FirstToken(style.GetProperty(RmlConstants.StyleProperties.Border));
        if (string.Equals(borderStyle, RmlConstants.Values.None, StringComparison.OrdinalIgnoreCase)
            || string.Equals(borderStyle, RmlConstants.Values.Hidden, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var width = ResolveBorderWidth(style, fontSize);
        if (width <= 0)
        {
            return;
        }

        border.BorderThickness = new Thickness(width);
        border.BorderBrush = ResolveBorderColor(style) ?? new SolidColorBrush(Colors.Black);
    }

    private RmlComputedStyle ComputeElementStyle(RmlElement element, IReadOnlyList<RmlElement> ancestors, RmlPreviewOptions options)
    {
        var inline = ParseInline(element);
        return RmlStyleComputer.ComputeStyle(element, ancestors, options.StyleSheets, inline);
    }

    private IReadOnlyList<RcssDeclaration> ParseInline(RmlElement element)
    {
        var style = element.GetAttribute(RmlConstants.Attributes.Style);
        if (string.IsNullOrWhiteSpace(style))
        {
            return [];
        }

        var parsed = rcssService.ParseInlineStyle(style);
        return parsed.Success && parsed.Data != null ? parsed.Data : [];
    }

    private Control? BuildElement(RmlElement element, List<RmlElement> ancestors, double inheritedFontSize, BuildContext context)
    {
        var options = context.Options;
        var style = ComputeElementStyle(element, ancestors, options);
        context.Styles[element.Id] = style;

        if (IsHidden(style))
        {
            if (!options.ShowHidden)
            {
                return null;
            }

            var placeholder = WrapSelectable(BuildHiddenPlaceholder(element), element.Id, context);
            context.Controls[element.Id] = placeholder;
            return placeholder;
        }

        var fontSize = ResolveFontSize(style, inheritedFontSize, context.Options.BaseFontSize);
        ancestors.Add(element);
        Control? control;
        try
        {
            control = BuildByTag(element, ancestors, style, fontSize, context);
        }
        finally
        {
            ancestors.RemoveAt(ancestors.Count - 1);
        }

        if (control == null)
        {
            return null;
        }

        control.Focusable = false;
        var framed = WrapSelectable(control, element.Id, context);
        context.Controls[element.Id] = framed;
        return framed;
    }

    private Control? BuildByTag(RmlElement element, List<RmlElement> ancestors, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var tag = element.Tag.ToLowerInvariant();
        return tag switch
        {
            RmlConstants.Elements.Paragraph or RmlConstants.Elements.Heading1 or RmlConstants.Elements.Heading2 or RmlConstants.Elements.Heading3 or RmlConstants.Elements.Heading4 or RmlConstants.Elements.Span or RmlConstants.Elements.Emphasis or RmlConstants.Elements.Strong or RmlConstants.Elements.Code or RmlConstants.Elements.Preformatted or RmlConstants.Elements.Label or RmlConstants.Elements.Anchor => BuildTextContainer(element, ancestors, style, fontSize, context),
            RmlConstants.Elements.Image => BuildImage(element, style, fontSize, context),
            RmlConstants.Elements.Input => BuildInput(element, style, fontSize, context),
            RmlConstants.Elements.Button => BuildButton(element, style, fontSize, context),
            RmlConstants.Elements.Select => BuildSelect(element, style, fontSize),
            RmlConstants.Elements.TextArea => BuildTextArea(element, style, fontSize),
            RmlConstants.Elements.Table => BuildTable(element, ancestors, style, fontSize, context),
            RmlConstants.Elements.UnorderedList or RmlConstants.Elements.OrderedList => BuildList(element, ancestors, style, fontSize, context),
            RmlConstants.Elements.Break => new Border { Height = BreakSpacing },
            RmlConstants.Elements.HorizontalRule => new Separator { Margin = new Thickness(0, 8) },
            RmlConstants.Elements.Progress => BuildProgress(element, style, fontSize),
            RmlConstants.Elements.Option or RmlConstants.Elements.OptionGroup or RmlConstants.Elements.TableRow or RmlConstants.Elements.TableCell or RmlConstants.Elements.TableHeaderCell or RmlConstants.Elements.TableHead or RmlConstants.Elements.TableBody or RmlConstants.Elements.TableFoot or RmlConstants.Elements.ListItem or RmlConstants.Document.Title or RmlConstants.Document.Link or RmlConstants.Document.Meta or RmlConstants.Document.Style or RmlConstants.Document.Script or RmlConstants.Document.Head => null,
            RmlConstants.Elements.MapPreview => BuildLabeledPlaceholder(element, localization.GetString("Tools.RmlEditor.Preview.MapPreview"), style, fontSize, context),
            _ => BuildContainer(element, ancestors, style, fontSize, context),
        };
    }

    private Control BuildContainer(RmlElement element, List<RmlElement> ancestors, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var host = BuildChildHost(element, ancestors, fontSize, context);
        var border = new Border
        {
            Child = host,
            Background = ResolveBackground(style, context),
            Padding = ResolvePadding(style, fontSize),
        };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private Control BuildChildHost(RmlElement element, List<RmlElement> ancestors, double fontSize, BuildContext context)
    {
        var style = context.Styles[element.Id];
        var panel = CreateFlowPanel(style);
        var inlineRun = new List<RmlNode>();

        foreach (var child in element.Children)
        {
            if (child is RmlComment)
            {
                continue;
            }

            if (IsInlineNode(child))
            {
                inlineRun.Add(child);
                continue;
            }

            FlushInlineRun(panel, inlineRun, element, ancestors, fontSize, context);
            if (child is RmlElement nested)
            {
                var built = BuildElement(nested, ancestors, fontSize, context);
                if (built != null)
                {
                    panel.Children.Add(built);
                }
            }
        }

        FlushInlineRun(panel, inlineRun, element, ancestors, fontSize, context);
        return WrapOverflow(panel, style);
    }

    private void FlushInlineRun(Panel panel, List<RmlNode> run, RmlElement parent, List<RmlElement> ancestors, double fontSize, BuildContext context)
    {
        if (run.Count == 0)
        {
            return;
        }

        var nodes = run.ToList();
        run.Clear();
        var block = BuildInlineBlock(nodes, parent, ancestors, fontSize, context);
        if (block != null)
        {
            panel.Children.Add(block);
        }
    }

    private Control BuildTextContainer(RmlElement element, List<RmlElement> ancestors, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var border = new Border { Background = ResolveBackground(style, context) };
        var panel = new StackPanel { Orientation = Orientation.Vertical };
        border.Child = panel;

        var inlineRun = new List<RmlNode>();
        foreach (var child in element.Children)
        {
            if (child is RmlComment)
            {
                continue;
            }

            if (IsInlineNode(child))
            {
                inlineRun.Add(child);
                continue;
            }

            FlushInlineRun(panel, inlineRun, element, ancestors, fontSize, context);
            if (child is RmlElement nested)
            {
                var built = BuildElement(nested, ancestors, fontSize, context);
                if (built != null)
                {
                    panel.Children.Add(built);
                }
            }
        }

        FlushInlineRun(panel, inlineRun, element, ancestors, fontSize, context);
        if (panel.Children.Count == 0)
        {
            panel.Children.Add(CreateTextBlock(string.Empty, element, style, fontSize));
        }

        ApplyBox(border, style, fontSize);
        return border;
    }

    private Control? BuildInlineBlock(IReadOnlyList<RmlNode> nodes, RmlElement parent, List<RmlElement> ancestors, double fontSize, BuildContext context)
    {
        var parentStyle = context.Styles[parent.Id];
        var block = CreateTextBlock(string.Empty, parent, parentStyle, fontSize);
        var inlines = new InlineCollection();
        var ancestorsWithParent = new List<RmlElement>(ancestors) { parent };
        foreach (var node in nodes)
        {
            AppendInline(inlines, node, ancestorsWithParent, fontSize, context);
        }

        if (inlines.Count == 0)
        {
            return null;
        }

        block.Inlines = inlines;
        return block;
    }

    private void AppendInline(InlineCollection inlines, RmlNode node, List<RmlElement> ancestors, double fontSize, BuildContext context)
    {
        if (node is RmlText text)
        {
            var value = CollapseWhitespace(text.Text);
            if (value.Length > 0)
            {
                inlines.Add(new Run(value));
            }

            return;
        }

        if (node is not RmlElement element)
        {
            return;
        }

        if (string.Equals(element.Tag, RmlConstants.Elements.Break, StringComparison.OrdinalIgnoreCase))
        {
            inlines.Add(new LineBreak());
            return;
        }

        if (string.Equals(element.Tag, RmlConstants.Elements.Image, StringComparison.OrdinalIgnoreCase))
        {
            var imageStyle = ComputeElementStyle(element, ancestors, context.Options);
            context.Styles[element.Id] = imageStyle;
            var image = BuildImageContent(element, imageStyle, fontSize, context);
            if (image != null)
            {
                var framed = WrapSelectable(image, element.Id, context);
                context.Controls[element.Id] = framed;
                inlines.Add(new InlineUIContainer(framed));
            }

            return;
        }

        var style = ComputeElementStyle(element, ancestors, context.Options);
        context.Styles[element.Id] = style;
        if (IsHidden(style) && !context.Options.ShowHidden)
        {
            return;
        }

        var elementFontSize = ResolveFontSize(style, fontSize, context.Options.BaseFontSize);
        if (string.Equals(element.Tag, RmlConstants.Elements.Anchor, StringComparison.OrdinalIgnoreCase))
        {
            AppendAnchorInline(inlines, element, ancestors, style, elementFontSize, context);
            return;
        }

        var span = new Span();
        ApplyInlineStyle(span, element, style, elementFontSize, context);
        var nested = new List<RmlElement>(ancestors) { element };
        foreach (var child in element.Children)
        {
            if (IsInlineNode(child))
            {
                AppendInline(span.Inlines, child, nested, elementFontSize, context);
            }
        }

        if (span.Inlines.Count > 0)
        {
            inlines.Add(span);
        }
        else
        {
            var fallback = CollapseWhitespace(element.InnerText);
            if (fallback.Length > 0)
            {
                span.Inlines.Add(new Run(ApplyTextTransform(fallback, style)));
                inlines.Add(span);
            }
        }
    }

    private void AppendAnchorInline(InlineCollection inlines, RmlElement element, List<RmlElement> ancestors, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var span = new Span();
        ApplyInlineStyle(span, element, style, fontSize, context);
        span.TextDecorations = TextDecorations.Underline;
        if (style.GetProperty(RmlConstants.StyleProperties.Color) == null)
        {
            span.Foreground = ThemeBrush(ThemeResourceKeys.AccentBrush, new SolidColorBrush(Color.FromRgb(0x4D, 0xA3, 0xFF)));
        }

        var nested = new List<RmlElement>(ancestors) { element };
        foreach (var child in element.Children)
        {
            if (IsInlineNode(child))
            {
                AppendInline(span.Inlines, child, nested, fontSize, context);
            }
        }

        if (span.Inlines.Count == 0)
        {
            var fallback = CollapseWhitespace(element.InnerText);
            if (fallback.Length == 0)
            {
                return;
            }

            span.Inlines.Add(new Run(ApplyTextTransform(fallback, style)));
        }

        inlines.Add(span);
    }

    private Control BuildList(RmlElement element, List<RmlElement> ancestors, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var ordered = string.Equals(element.Tag, RmlConstants.Elements.OrderedList, StringComparison.OrdinalIgnoreCase);
        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 2 };
        var index = 0;
        foreach (var item in element.Elements.Where(e => string.Equals(e.Tag, RmlConstants.Elements.ListItem, StringComparison.OrdinalIgnoreCase)))
        {
            index++;
            var marker = ordered ? $"{index}." : "-";
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.Add(new TextBlock { Text = marker, FontSize = fontSize, VerticalAlignment = VerticalAlignment.Top });
            var content = new StackPanel { Orientation = Orientation.Vertical };
            var itemAncestors = new List<RmlElement>(ancestors) { element };
            var itemStyle = ComputeElementStyle(item, itemAncestors, context.Options);
            context.Styles[item.Id] = itemStyle;
            var inlineRun = new List<RmlNode>();
            var nestedAncestors = new List<RmlElement>(itemAncestors) { item };
            foreach (var child in item.Children)
            {
                if (child is RmlComment)
                {
                    continue;
                }

                if (IsInlineNode(child))
                {
                    inlineRun.Add(child);
                    continue;
                }

                FlushInlineRun(content, inlineRun, item, nestedAncestors, fontSize, context);
                if (child is RmlElement nested)
                {
                    var built = BuildElement(nested, nestedAncestors, fontSize, context);
                    if (built != null)
                    {
                        content.Children.Add(built);
                    }
                }
            }

            FlushInlineRun(content, inlineRun, item, nestedAncestors, fontSize, context);
            row.Children.Add(content);
            panel.Children.Add(WrapSelectable(row, item.Id, context));
            context.Controls[item.Id] = row;
        }

        var border = new Border
        {
            Child = panel,
            Background = ResolveBackground(style, context),
            Padding = ResolvePadding(style, fontSize),
        };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private Control? BuildImage(RmlElement element, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var content = BuildImageContent(element, style, fontSize, context);
        if (content == null)
        {
            return null;
        }

        var border = new Border { Child = content };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private Control? BuildImageContent(RmlElement element, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var reference = element.GetAttribute(RmlConstants.Attributes.Src) ?? string.Empty;
        context.Options.Images.TryGetValue(reference, out var bitmap);
        if (bitmap == null)
        {
            if (!string.IsNullOrWhiteSpace(reference))
            {
                context.MissingImages.Add(reference);
            }

            return BuildMissingImagePlaceholder(element, style, fontSize, reference);
        }

        var image = new Image
        {
            Source = bitmap,
            Stretch = ResolveStretch(style),
        };
        ApplyImageSize(image, element, style, fontSize);
        return image;
    }

    private Control BuildMissingImagePlaceholder(RmlElement element, RmlComputedStyle style, double fontSize, string reference)
    {
        var label = string.IsNullOrWhiteSpace(reference)
            ? localization.GetString("Tools.RmlEditor.Preview.MissingImage")
            : localization.GetString("Tools.RmlEditor.Preview.MissingImageSource", reference);
        var text = new TextBlock
        {
            Text = label,
            FontSize = Math.Max(10, fontSize * 0.75),
            Foreground = ThemeBrush(ThemeResourceKeys.TextSecondary, new SolidColorBrush(Colors.Gray)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8),
        };
        var border = new Border
        {
            Child = text,
            Background = ThemeBrush(ThemeResourceKeys.SurfaceElevatedBrush, new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A))),
            BorderBrush = ThemeBrush(ThemeResourceKeys.BorderBrush, new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))),
            BorderThickness = new Thickness(1),
            Width = ResolveImageDimension(element.GetAttribute(RmlConstants.Attributes.Width), style.GetProperty(RmlConstants.StyleProperties.Width), fontSize, MissingImageWidth),
            Height = ResolveImageDimension(element.GetAttribute(RmlConstants.Attributes.Height), style.GetProperty(RmlConstants.StyleProperties.Height), fontSize, MissingImageHeight),
        };
        return border;
    }

    private Control? BuildInput(RmlElement element, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var type = (element.GetAttribute(RmlConstants.Attributes.Type) ?? RmlConstants.InputTypes.Text).ToLowerInvariant();
        Control? control = type switch
        {
            RmlConstants.InputTypes.Checkbox => new CheckBox { IsChecked = element.HasAttribute(RmlConstants.Attributes.Checked), Content = element.GetAttribute(RmlConstants.Attributes.Value) },
            RmlConstants.InputTypes.Radio => new RadioButton { IsChecked = element.HasAttribute(RmlConstants.Attributes.Checked), GroupName = element.GetAttribute(RmlConstants.Attributes.Name) ?? string.Empty, Content = element.GetAttribute(RmlConstants.Attributes.Value) },
            RmlConstants.InputTypes.Button or RmlConstants.InputTypes.Submit or RmlConstants.InputTypes.Reset => new Button { Content = element.GetAttribute(RmlConstants.Attributes.Value) ?? DefaultButtonLabel(type) },
            RmlConstants.InputTypes.Range => BuildSlider(element),
            RmlConstants.InputTypes.Hidden => null,
            RmlConstants.InputTypes.File => new Button { Content = localization.GetString("Tools.RmlEditor.Preview.BrowseButton") },
            _ => BuildTextInput(element),
        };

        if (control == null)
        {
            return null;
        }

        control.SetValue(TextElement.FontSizeProperty, fontSize);
        var border = new Border { Child = control };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private string DefaultButtonLabel(string type)
    {
        return type switch
        {
            RmlConstants.InputTypes.Submit => localization.GetString("Tools.RmlEditor.Preview.SubmitButton"),
            RmlConstants.InputTypes.Reset => localization.GetString("Tools.RmlEditor.Preview.ResetButton"),
            _ => localization.GetString("Tools.RmlEditor.Preview.DefaultButton"),
        };
    }

    private Control BuildButton(RmlElement element, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var label = CollapseWhitespace(element.InnerText);
        if (label.Length == 0)
        {
            label = element.GetAttribute(RmlConstants.Attributes.Value) ?? localization.GetString("Tools.RmlEditor.Preview.DefaultButton");
        }

        var button = new Button
        {
            Content = ApplyTextTransform(label, style),
            FontSize = fontSize,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        ApplyButtonStyle(button, style, fontSize);
        var border = new Border { Child = button };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private Control BuildTable(RmlElement element, List<RmlElement> ancestors, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var rows = CollectTableRows(element);
        var columnCount = 1;
        foreach (var row in rows)
        {
            columnCount = Math.Max(columnCount, row.Count);
        }

        var grid = new Grid();
        for (var c = 0; c < columnCount; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var cells = rows[r];
            for (var c = 0; c < cells.Count; c++)
            {
                var cell = BuildTableCell(cells[c], ancestors, fontSize, context);
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                if (int.TryParse(cells[c].GetAttribute(RmlConstants.Attributes.ColSpan), out var colspan) && colspan > 1)
                {
                    Grid.SetColumnSpan(cell, Math.Min(colspan, columnCount - c));
                }

                if (int.TryParse(cells[c].GetAttribute(RmlConstants.Attributes.RowSpan), out var rowspan) && rowspan > 1)
                {
                    Grid.SetRowSpan(cell, Math.Min(rowspan, rows.Count - r));
                }

                grid.Children.Add(cell);
            }
        }

        var border = new Border
        {
            Child = grid,
            Background = ResolveBackground(style, context),
            Padding = ResolvePadding(style, fontSize),
        };
        ApplyBox(border, style, fontSize);
        return border;
    }

    private Control BuildTableCell(RmlElement cell, List<RmlElement> ancestors, double fontSize, BuildContext context)
    {
        var header = string.Equals(cell.Tag, RmlConstants.Elements.TableHeaderCell, StringComparison.OrdinalIgnoreCase);
        var cellAncestors = new List<RmlElement>(ancestors);
        var cellStyle = ComputeElementStyle(cell, cellAncestors, context.Options);
        context.Styles[cell.Id] = cellStyle;
        var cellFontSize = ResolveFontSize(cellStyle, fontSize, context.Options.BaseFontSize);

        var panel = new StackPanel { Orientation = Orientation.Vertical };
        var inlineRun = new List<RmlNode>();
        var nested = new List<RmlElement>(cellAncestors) { cell };
        foreach (var child in cell.Children)
        {
            if (child is RmlComment)
            {
                continue;
            }

            if (IsInlineNode(child))
            {
                inlineRun.Add(child);
                continue;
            }

            FlushInlineRun(panel, inlineRun, cell, nested, cellFontSize, context);
            if (child is RmlElement element)
            {
                var built = BuildElement(element, nested, cellFontSize, context);
                if (built != null)
                {
                    panel.Children.Add(built);
                }
            }
        }

        FlushInlineRun(panel, inlineRun, cell, nested, cellFontSize, context);
        if (header)
        {
            foreach (var text in panel.Children.OfType<TextBlock>())
            {
                text.FontWeight = FontWeight.Bold;
            }
        }

        var border = new Border
        {
            Child = panel,
            Background = ResolveBackground(cellStyle, context),
            Padding = ResolvePadding(cellStyle, cellFontSize),
            BorderBrush = ThemeBrush(ThemeResourceKeys.BorderBrush, new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))),
            BorderThickness = new Thickness(0.5),
        };
        if (panel.Children.Count == 0)
        {
            panel.Children.Add(new TextBlock { Text = string.Empty });
        }

        return WrapSelectable(border, cell.Id, context);
    }

    private Control BuildHiddenPlaceholder(RmlElement element)
    {
        var label = $"<{element.Tag}>";
        if (!string.IsNullOrEmpty(element.ElementId))
        {
            label += $" #{element.ElementId}";
        }

        return new Border
        {
            Height = HiddenPlaceholderHeight,
            Background = ThemeBrush(ThemeResourceKeys.SurfaceElevatedBrush, new SolidColorBrush(Color.FromArgb(0x33, 0x88, 0x88, 0x88))),
            BorderBrush = ThemeBrush(ThemeResourceKeys.BorderBrush, new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88))),
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = localization.GetString("Tools.RmlEditor.Preview.HiddenElement", label),
                FontSize = 11,
                Foreground = ThemeBrush(ThemeResourceKeys.TextSecondary, new SolidColorBrush(Colors.Gray)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    private void ApplyInlineStyle(Span span, RmlElement element, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        if (RmlColors.TryParse(style.GetProperty(RmlConstants.StyleProperties.Color), out var color))
        {
            span.Foreground = new SolidColorBrush(color);
        }

        var background = ResolveBackground(style, context);
        if (background != null)
        {
            span.Background = background;
        }

        span.FontSize = fontSize;
        span.FontWeight = ResolveFontWeight(element, style);
        span.FontStyle = ResolveFontStyle(style);
        span.FontFamily = ResolveFontFamily(style) ?? span.FontFamily;
        span.TextDecorations = ResolveTextDecorations(style);
    }

    private Control BuildLabeledPlaceholder(RmlElement element, string caption, RmlComputedStyle style, double fontSize, BuildContext context)
    {
        var border = new Border
        {
            Background = ResolveBackground(style, context) ?? ThemeBrush(ThemeResourceKeys.SurfaceElevatedBrush, new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22))),
            BorderBrush = ThemeBrush(ThemeResourceKeys.BorderBrush, new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55))),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            MinHeight = 64,
            Child = new TextBlock
            {
                Text = string.IsNullOrEmpty(element.ElementId) ? caption : $"{caption} #{element.ElementId}",
                FontSize = Math.Max(11, fontSize * 0.85),
                Foreground = ThemeBrush(ThemeResourceKeys.TextSecondary, new SolidColorBrush(Colors.Gray)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        ApplyBox(border, style, fontSize);
        return border;
    }
}
