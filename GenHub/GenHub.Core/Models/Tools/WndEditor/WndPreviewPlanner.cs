using GenHub.Core.Constants;
using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Plans canvas previews the way the engine draws windows (see W3DGameWinDefaultDraw,
/// W3DGadgetPushButtonImageDraw, and W3DGadgetTextEntryImageDraw in GeneralsGameCode).
/// </summary>
public static class WndPreviewPlanner
{
    /// <summary>
    /// Builds the preview plan for a window from its enabled draw data, status, text, and font.
    /// </summary>
    /// <param name="window">The window to plan.</param>
    /// <returns>The preview plan, never null.</returns>
    public static WndPreviewPlan Plan(WndWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        WndDrawDataSet.TryParse(window.GetProperty(WndConstants.PropertyKeys.EnabledDrawData), out var drawData);
        var status = WndStatusValue.ParseStatus(window.GetProperty(WndConstants.PropertyKeys.Status));
        var hasImageFlag = status.Flags.Contains(WndConstants.StatusFlags.Image, StringComparer.OrdinalIgnoreCase);
        var isHidden = status.Flags.Contains(WndConstants.StatusFlags.Hidden, StringComparer.OrdinalIgnoreCase);
        var text = PlanText(window);
        var (textColor, fontSize, fontBold) = PlanTextStyle(window);
        var textCentered = IsCenteredText(window);

        return window.ControlType switch
        {
            WndControlType.PushButton or WndControlType.CommandButton => PlanButton(drawData, text, textColor, fontSize, fontBold, isHidden),
            WndControlType.EntryField => PlanTextEntry(drawData, text, textColor, fontSize, fontBold, isHidden),
            WndControlType.StaticText => new WndPreviewPlan(null, null, null, null, null, null, false, null, null, text, textColor, fontSize, fontBold, textCentered, isHidden),
            WndControlType.ScrollListBox => PlanListbox(window, drawData, text, textColor, fontSize, fontBold, isHidden),
            WndControlType.ComboBox => PlanComboBox(window, drawData, text, textColor, fontSize, fontBold, isHidden),
            WndControlType.HorzSlider or WndControlType.VertSlider => PlanSlider(window, drawData, fontSize, isHidden),
            _ => PlanGeneric(drawData, hasImageFlag, text, textColor, fontSize, fontBold, textCentered, isHidden, window.ControlType),
        };
    }

    private static WndPreviewPlan PlanButton(
        WndDrawDataSet? drawData,
        string? text,
        WndRgbaColor? textColor,
        int fontSize,
        bool fontBold,
        bool isHidden)
    {
        var left = ImageAt(drawData, WndConstants.Preview.ButtonImageIndex);
        var middle = ImageAt(drawData, WndConstants.Preview.ButtonMiddleImageIndex);
        var right = ImageAt(drawData, WndConstants.Preview.ButtonRightImageIndex);
        if (left != null && middle != null && right != null)
        {
            var fallback = EntryAt(drawData, WndConstants.Preview.ButtonImageIndex);
            return new WndPreviewPlan(null, left, middle, right, null, null, false, fallback?.Color, fallback?.BorderColor, text, textColor, fontSize, fontBold, true, isHidden);
        }

        var single = EntryAt(drawData, WndConstants.Preview.ButtonImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, null, false, single?.Color, single?.BorderColor, text, textColor, fontSize, fontBold, true, isHidden);
    }

    private static WndPreviewPlan PlanTextEntry(
        WndDrawDataSet? drawData,
        string? text,
        WndRgbaColor? textColor,
        int fontSize,
        bool fontBold,
        bool isHidden)
    {
        var left = ImageAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
        var right = ImageAt(drawData, WndConstants.Preview.TextEntryRightImageIndex);
        var center = ImageAt(drawData, WndConstants.Preview.TextEntryCenterImageIndex);
        if (left != null && center != null && right != null)
        {
            var fallback = EntryAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
            return new WndPreviewPlan(null, left, center, right, null, null, false, fallback?.Color, fallback?.BorderColor, text, textColor, fontSize, fontBold, false, isHidden);
        }

        var single = EntryAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, null, false, single?.Color, single?.BorderColor, text, textColor, fontSize, fontBold, false, isHidden);
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
            entry?.Color,
            entry?.BorderColor,
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
        bool isHidden)
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

        return new WndPreviewPlan(
            ImageAt(drawData, WndConstants.Preview.DefaultImageIndex),
            null,
            null,
            null,
            null,
            sub,
            false,
            entry?.Color,
            entry?.BorderColor,
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
        bool isHidden)
    {
        var entry = EntryAt(drawData, WndConstants.Preview.DefaultImageIndex);
        var sub = new WndPreviewSubImages(
            null,
            null,
            null,
            SubImageAt(window, WndConstants.SubDrawDataKeys.ComboBoxDropDownButtonEnabled),
            null);
        return new WndPreviewPlan(
            ImageAt(drawData, WndConstants.Preview.DefaultImageIndex),
            null,
            null,
            null,
            null,
            sub,
            false,
            entry?.Color,
            entry?.BorderColor,
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
        bool isHidden)
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
            return new WndPreviewPlan(null, left, center, right, null, sub, vertical, fallback?.Color, fallback?.BorderColor, null, null, fontSize, false, false, isHidden);
        }

        var single = EntryAt(drawData, WndConstants.Preview.SliderLeftImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, sub, vertical, single?.Color, single?.BorderColor, null, null, fontSize, false, false, isHidden);
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
        if (WndStaticTextData.TryParse(window.GetProperty(WndConstants.PropertyKeys.StaticTextData), out var data) && data != null)
        {
            return data.Centered;
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
