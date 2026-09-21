using GenHub.Core.Constants;
using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Computes preview display properties (which images to load, slices, text, visibility)
/// for a parsed WND window based on its control type and draw data.
/// </summary>
public static class WndPreviewPlanner
{
    /// <summary>
    /// Plans the preview presentation for a window.
    /// </summary>
    /// <param name="window">The WND window model to plan.</param>
    /// <returns>The computed preview plan.</returns>
    public static WndPreviewPlan Plan(WndWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        WndDrawDataSet.TryParse(window.GetProperty(WndConstants.PropertyKeys.EnabledDrawData), out var drawData);
        var status = WndStatusValue.ParseStatus(window.GetProperty(WndConstants.PropertyKeys.Status));
        var hasImageFlag = status.Flags.Contains(WndConstants.StatusFlags.Image, StringComparer.OrdinalIgnoreCase);
        var isHidden = status.Flags.Contains(WndConstants.StatusFlags.Hidden, StringComparer.OrdinalIgnoreCase);
        var isSeeThru = status.Flags.Contains(WndConstants.StatusFlags.SeeThru, StringComparer.OrdinalIgnoreCase);
        var text = PlanText(window);
        var (textColor, fontSize, fontBold) = PlanTextStyle(window);
        var textCentered = IsCenteredText(window);

        return window.ControlType switch
        {
            WndControlType.PushButton or WndControlType.CommandButton => PlanButton(drawData, text, textColor, fontSize, fontBold, isHidden, isSeeThru),
            WndControlType.EntryField => PlanTextEntry(drawData, text, textColor, fontSize, fontBold, isHidden, isSeeThru),
            WndControlType.StaticText => new WndPreviewPlan(null, null, null, null, null, null, false, null, null, text, textColor, fontSize, fontBold, textCentered, isHidden),
            WndControlType.ScrollListBox => PlanListbox(window, drawData, text, textColor, fontSize, fontBold, isHidden, isSeeThru),
            WndControlType.ComboBox => PlanComboBox(window, drawData, text, textColor, fontSize, fontBold, isHidden, isSeeThru),
            WndControlType.HorzSlider or WndControlType.VertSlider => PlanSlider(window, drawData, fontSize, isHidden, isSeeThru),
            _ => PlanGeneric(drawData, hasImageFlag, text, textColor, fontSize, fontBold, textCentered, isHidden, isSeeThru, window.ControlType),
        };
    }

    private static WndPreviewPlan PlanButton(
        WndDrawDataSet? drawData,
        string? text,
        WndRgbaColor? textColor,
        int fontSize,
        bool fontBold,
        bool isHidden,
        bool isSeeThru)
    {
        var left = ImageAt(drawData, WndConstants.Preview.ButtonImageIndex);
        var middle = ImageAt(drawData, WndConstants.Preview.ButtonMiddleImageIndex);
        var right = ImageAt(drawData, WndConstants.Preview.ButtonRightImageIndex);
        if (left != null && middle != null && right != null)
        {
            var fallback = EntryAt(drawData, WndConstants.Preview.ButtonImageIndex);
            return new WndPreviewPlan(null, left, middle, right, null, null, false, ResolveFillColor(fallback, true, isSeeThru), ResolveBorderColor(fallback, isSeeThru), text, textColor, fontSize, fontBold, true, isHidden);
        }

        var single = EntryAt(drawData, WndConstants.Preview.ButtonImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, null, false, ResolveFillColor(single, left != null, isSeeThru), ResolveBorderColor(single, isSeeThru), text, textColor, fontSize, fontBold, true, isHidden);
    }

    private static WndPreviewPlan PlanTextEntry(
        WndDrawDataSet? drawData,
        string? text,
        WndRgbaColor? textColor,
        int fontSize,
        bool fontBold,
        bool isHidden,
        bool isSeeThru)
    {
        var left = ImageAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
        var right = ImageAt(drawData, WndConstants.Preview.TextEntryRightImageIndex);
        var center = ImageAt(drawData, WndConstants.Preview.TextEntryCenterImageIndex);
        if (left != null && center != null && right != null)
        {
            var fallback = EntryAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
            return new WndPreviewPlan(null, left, center, right, null, null, false, ResolveFillColor(fallback, true, isSeeThru), ResolveBorderColor(fallback, isSeeThru), text, textColor, fontSize, fontBold, false, isHidden);
        }

        var single = EntryAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, null, false, ResolveFillColor(single, left != null, isSeeThru), ResolveBorderColor(single, isSeeThru), text, textColor, fontSize, fontBold, false, isHidden);
    }

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Internal helper packaging full preview plan context")]
    private static WndPreviewPlan PlanGeneric(
        WndDrawDataSet? drawData,
        bool hasImageFlag,
        string? text,
        WndRgbaColor? textColor,
        int fontSize,
        bool fontBold,
        bool textCentered,
        bool isHidden,
        bool isSeeThru,
        WndControlType controlType)
    {
        var entry = EntryAt(drawData, WndConstants.Preview.DefaultImageIndex);
        var needsImageFlag = controlType is WndControlType.User or WndControlType.Unknown or WndControlType.TabPane;
        var single = hasImageFlag || !needsImageFlag ? ImageAt(drawData, WndConstants.Preview.DefaultImageIndex) : null;
        var drawsText = controlType is WndControlType.CheckBox or WndControlType.RadioButton;
        var glyph = drawsText ? ImageAt(drawData, WndConstants.Preview.BoxGlyphImageIndex) : null;
        return new WndPreviewPlan(
            single,
            null,
            null,
            null,
            glyph,
            null,
            false,
            ResolveFillColor(entry, !string.IsNullOrWhiteSpace(entry?.Image), isSeeThru),
            ResolveBorderColor(entry, isSeeThru),
            drawsText ? text : null,
            textColor,
            fontSize,
            fontBold,
            textCentered,
            isHidden);
    }

    private static WndPreviewPlan PlanListbox(
        WndWindow window,
        WndDrawDataSet? drawData,
        string? text,
        WndRgbaColor? textColor,
        int fontSize,
        bool fontBold,
        bool isHidden,
        bool isSeeThru)
    {
        var entry = EntryAt(drawData, WndConstants.Preview.DefaultImageIndex);
        var sub = new WndPreviewSubImages(
            SubImageAt(window, WndConstants.SubDrawDataKeys.ListboxEnabledUpButton),
            SubImageAt(window, WndConstants.SubDrawDataKeys.ListboxEnabledDownButton),
            SubImageAt(window, WndConstants.SubDrawDataKeys.ListboxEnabledSlider),
            null,
            null);
        if (!ShowsScrollBar(window))
        {
            sub = sub with { ScrollUp = null, ScrollDown = null, ScrollThumb = null };
        }

        var image = ImageAt(drawData, WndConstants.Preview.DefaultImageIndex);
        return new WndPreviewPlan(
            image,
            null,
            null,
            null,
            null,
            sub,
            false,
            ResolveFillColor(entry, image != null, isSeeThru),
            ResolveBorderColor(entry, isSeeThru),
            text,
            textColor,
            fontSize,
            fontBold,
            false,
            isHidden);
    }

    private static WndPreviewPlan PlanComboBox(
        WndWindow window,
        WndDrawDataSet? drawData,
        string? text,
        WndRgbaColor? textColor,
        int fontSize,
        bool fontBold,
        bool isHidden,
        bool isSeeThru)
    {
        var entry = EntryAt(drawData, WndConstants.Preview.DefaultImageIndex);
        var sub = new WndPreviewSubImages(
            null,
            null,
            null,
            SubImageAt(window, WndConstants.SubDrawDataKeys.ComboBoxDropDownButtonEnabled),
            null);
        var image = ImageAt(drawData, WndConstants.Preview.DefaultImageIndex);
        return new WndPreviewPlan(
            image,
            null,
            null,
            null,
            null,
            sub,
            false,
            ResolveFillColor(entry, image != null, isSeeThru),
            ResolveBorderColor(entry, isSeeThru),
            text,
            textColor,
            fontSize,
            fontBold,
            false,
            isHidden);
    }

    private static WndPreviewPlan PlanSlider(
        WndWindow window,
        WndDrawDataSet? drawData,
        int fontSize,
        bool isHidden,
        bool isSeeThru)
    {
        var left = ImageAt(drawData, WndConstants.Preview.SliderLeftImageIndex);
        var right = ImageAt(drawData, WndConstants.Preview.SliderRightImageIndex);
        var center = ImageAt(drawData, WndConstants.Preview.SliderCenterImageIndex);
        var vertical = window.ControlType == WndControlType.VertSlider;
        var sub = new WndPreviewSubImages(
            null,
            null,
            null,
            null,
            SubImageAt(window, WndConstants.SubDrawDataKeys.SliderThumbEnabled));
        if (left != null && center != null && right != null)
        {
            var fallback = EntryAt(drawData, WndConstants.Preview.SliderLeftImageIndex);
            return new WndPreviewPlan(null, left, center, right, null, sub, vertical, ResolveFillColor(fallback, true, isSeeThru), ResolveBorderColor(fallback, isSeeThru), null, null, fontSize, false, false, isHidden);
        }

        var single = EntryAt(drawData, WndConstants.Preview.SliderLeftImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, sub, vertical, ResolveFillColor(single, left != null, isSeeThru), ResolveBorderColor(single, isSeeThru), null, null, fontSize, false, false, isHidden);
    }

    private static WndRgbaColor? ResolveFillColor(WndDrawDataEntry? entry, bool hasImage, bool isSeeThru)
    {
        if (isSeeThru || !hasImage || entry == null || entry.IsEmpty || string.IsNullOrWhiteSpace(entry.Image))
        {
            return null;
        }

        // GUIEdit default dummy red tint (255, 0, 0)
        if (entry.Color != null && entry.Color.Red == 255 && entry.Color.Green == 0 && entry.Color.Blue == 0)
        {
            return null;
        }

        return entry.Color;
    }

    private static WndRgbaColor? ResolveBorderColor(WndDrawDataEntry? entry, bool isSeeThru)
    {
        if (isSeeThru || entry == null || entry.IsEmpty)
        {
            return null;
        }

        if (entry.BorderColor != null && entry.BorderColor.Alpha == 0)
        {
            return null;
        }

        return entry.BorderColor;
    }

    private static bool ShowsScrollBar(WndWindow window)
    {
        return !WndListboxData.TryParse(window.GetProperty(WndConstants.PropertyKeys.ListboxData), out var data)
            || data == null
            || data.ScrollBar;
    }

    private static string? SubImageAt(WndWindow window, string key)
    {
        if (!WndDrawDataSet.TryParse(window.GetProperty(key), out var set) || set == null)
        {
            return null;
        }

        return ImageAt(set, WndConstants.Preview.DefaultImageIndex);
    }

    private static string? PlanText(WndWindow window)
    {
        var raw = window.GetProperty(WndConstants.PropertyKeys.Text);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var text = WndValueTokenizer.Unquote(raw).Trim();
        return text.Length == 0 ? null : text;
    }

    private static (WndRgbaColor? TextColor, int FontSize, bool FontBold) PlanTextStyle(WndWindow window)
    {
        WndRgbaColor? textColor = null;
        if (WndTextColorValue.TryParse(window.GetProperty(WndConstants.PropertyKeys.TextColor), out var colors) && colors != null)
        {
            textColor = colors.Enabled;
        }

        var fontSize = WndConstants.Editor.DefaultFontSize;
        var fontBold = false;
        if (WndFontValue.TryParse(window.GetProperty(WndConstants.PropertyKeys.Font), out var font) && font != null)
        {
            fontSize = Math.Max(1, font.Size);
            fontBold = font.Bold;
        }

        return (textColor, fontSize, fontBold);
    }

    private static bool IsCenteredText(WndWindow window)
    {
        if (window.ControlType is WndControlType.StaticText)
        {
            var raw = window.GetProperty(WndConstants.PropertyKeys.StaticTextData);
            return WndStaticTextData.TryParse(raw, out var data) && data?.Centered == true;
        }

        return window.ControlType is WndControlType.PushButton or WndControlType.CommandButton;
    }

    private static WndDrawDataEntry? EntryAt(WndDrawDataSet? drawData, int index)
    {
        if (drawData == null || index < 0 || index >= drawData.Entries.Count)
        {
            return null;
        }

        return drawData.Entries[index];
    }

    private static string? ImageAt(WndDrawDataSet? drawData, int index)
    {
        var entry = EntryAt(drawData, index);
        if (entry == null || entry.IsEmpty || string.IsNullOrWhiteSpace(entry.Image))
        {
            return null;
        }

        return entry.Image.Trim();
    }
}
