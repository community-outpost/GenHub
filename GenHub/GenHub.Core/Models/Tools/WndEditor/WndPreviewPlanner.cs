using GenHub.Core.Constants;
using System;
using System.Collections.Generic;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// Computes preview display properties (which images to load, slices, text, visibility)
/// for a parsed WND window based on its control type and draw data.
/// </summary>
public static class WndPreviewPlanner
{
    private sealed record WndTextStyle(string? Text, WndRgbaColor? TextColor, int FontSize, bool FontBold, string? FontName = null);

    /// <summary>
    /// Plans the preview presentation for a window.
    /// </summary>
    /// <param name="window">The WND window model to plan.</param>
    /// <param name="windowImageOverrides">Optional mapped image overrides keyed by control or scheme name.</param>
    /// <returns>The computed preview plan.</returns>
    public static WndPreviewPlan Plan(WndWindow window, IReadOnlyDictionary<string, string>? windowImageOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        WndDrawDataSet.TryParse(window.GetProperty(WndConstants.PropertyKeys.EnabledDrawData), out var drawData);
        var status = WndStatusValue.ParseStatus(window.GetProperty(WndConstants.PropertyKeys.Status));
        var isHidden = status.Flags.Contains(WndConstants.StatusFlags.Hidden, StringComparer.OrdinalIgnoreCase);
        var isSeeThru = status.Flags.Contains(WndConstants.StatusFlags.SeeThru, StringComparer.OrdinalIgnoreCase);
        var isImageWindow = status.Flags.Contains(WndConstants.StatusFlags.Image, StringComparer.OrdinalIgnoreCase);
        var text = PlanText(window);
        var (textColor, fontSize, fontBold, fontName) = PlanTextStyle(window);
        var textCentered = IsCenteredText(window);
        var style = new WndTextStyle(text, textColor, fontSize, fontBold, fontName);

        var plan = window.ControlType switch
        {
            WndControlType.PushButton or WndControlType.CommandButton => PlanButton(drawData, style, isHidden, isSeeThru, isImageWindow),
            WndControlType.EntryField => PlanTextEntry(drawData, style, isHidden, isSeeThru, isImageWindow),
            WndControlType.ScrollListBox => PlanListbox(window, drawData, style, isHidden, isSeeThru, isImageWindow),
            WndControlType.ComboBox => PlanComboBox(window, drawData, style, isHidden, isSeeThru, isImageWindow),
            WndControlType.HorzSlider or WndControlType.VertSlider => PlanSlider(window, drawData, fontSize, isHidden, isSeeThru, isImageWindow),
            _ => PlanGeneric(drawData, style, textCentered, isHidden, isSeeThru, isImageWindow, window.ControlType),
        };

        return ApplySchemeAndBackdropContext(window, plan, windowImageOverrides);
    }

    private static WndPreviewPlan PlanButton(
        WndDrawDataSet? drawData,
        WndTextStyle style,
        bool isHidden,
        bool isSeeThru,
        bool isImageWindow)
    {
        var left = ImageAt(drawData, WndConstants.Preview.ButtonImageIndex);
        var middle = ImageAt(drawData, WndConstants.Preview.ButtonMiddleImageIndex);
        var right = ImageAt(drawData, WndConstants.Preview.ButtonRightImageIndex);
        if (left != null && middle != null && right != null)
        {
            var fallback = EntryAt(drawData, WndConstants.Preview.ButtonImageIndex);
            return new WndPreviewPlan(null, left, middle, right, null, null, false, ResolveFillColor(fallback, isSeeThru, isImageWindow), ResolveBorderColor(fallback, isSeeThru), style.Text, style.TextColor, style.FontSize, style.FontBold, true, isHidden, FontName: style.FontName);
        }

        var single = EntryAt(drawData, WndConstants.Preview.ButtonImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, null, false, ResolveFillColor(single, isSeeThru, isImageWindow), ResolveBorderColor(single, isSeeThru), style.Text, style.TextColor, style.FontSize, style.FontBold, true, isHidden, FontName: style.FontName);
    }

    private static WndPreviewPlan PlanTextEntry(
        WndDrawDataSet? drawData,
        WndTextStyle style,
        bool isHidden,
        bool isSeeThru,
        bool isImageWindow)
    {
        var left = ImageAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
        var right = ImageAt(drawData, WndConstants.Preview.TextEntryRightImageIndex);
        var center = ImageAt(drawData, WndConstants.Preview.TextEntryCenterImageIndex);
        if (left != null && center != null && right != null)
        {
            var fallback = EntryAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
            return new WndPreviewPlan(null, left, center, right, null, null, false, ResolveFillColor(fallback, isSeeThru, isImageWindow), ResolveBorderColor(fallback, isSeeThru), style.Text, style.TextColor, style.FontSize, style.FontBold, false, isHidden, FontName: style.FontName);
        }

        var single = EntryAt(drawData, WndConstants.Preview.TextEntryLeftImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, null, false, ResolveFillColor(single, isSeeThru, isImageWindow), ResolveBorderColor(single, isSeeThru), style.Text, style.TextColor, style.FontSize, style.FontBold, false, isHidden, FontName: style.FontName);
    }

    private static WndPreviewPlan PlanGeneric(
        WndDrawDataSet? drawData,
        WndTextStyle style,
        bool textCentered,
        bool isHidden,
        bool isSeeThru,
        bool isImageWindow,
        WndControlType controlType)
    {
        var entry = EntryAt(drawData, WndConstants.Preview.DefaultImageIndex);
        var single = ImageAt(drawData, WndConstants.Preview.DefaultImageIndex);
        var drawsText = controlType is WndControlType.CheckBox or WndControlType.RadioButton or WndControlType.StaticText;
        var glyph = controlType is WndControlType.CheckBox or WndControlType.RadioButton
            ? ImageAt(drawData, WndConstants.Preview.BoxGlyphImageIndex)
            : null;
        return new WndPreviewPlan(
            single,
            null,
            null,
            null,
            glyph,
            null,
            false,
            ResolveFillColor(entry, isSeeThru, isImageWindow),
            ResolveBorderColor(entry, isSeeThru),
            drawsText ? style.Text : null,
            style.TextColor,
            style.FontSize,
            style.FontBold,
            textCentered,
            isHidden,
            FontName: style.FontName);
    }

    private static WndPreviewPlan PlanListbox(
        WndWindow window,
        WndDrawDataSet? drawData,
        WndTextStyle style,
        bool isHidden,
        bool isSeeThru,
        bool isImageWindow)
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
            ResolveFillColor(entry, isSeeThru, isImageWindow),
            ResolveBorderColor(entry, isSeeThru),
            style.Text,
            style.TextColor,
            style.FontSize,
            style.FontBold,
            false,
            isHidden,
            FontName: style.FontName);
    }

    private static WndPreviewPlan PlanComboBox(
        WndWindow window,
        WndDrawDataSet? drawData,
        WndTextStyle style,
        bool isHidden,
        bool isSeeThru,
        bool isImageWindow)
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
            ResolveFillColor(entry, isSeeThru, isImageWindow),
            ResolveBorderColor(entry, isSeeThru),
            style.Text,
            style.TextColor,
            style.FontSize,
            style.FontBold,
            false,
            isHidden,
            FontName: style.FontName);
    }

    private static WndPreviewPlan PlanSlider(
        WndWindow window,
        WndDrawDataSet? drawData,
        int fontSize,
        bool isHidden,
        bool isSeeThru,
        bool isImageWindow)
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
            return new WndPreviewPlan(null, left, center, right, null, sub, vertical, ResolveFillColor(fallback, isSeeThru, isImageWindow), ResolveBorderColor(fallback, isSeeThru), null, null, fontSize, false, false, isHidden);
        }

        var single = EntryAt(drawData, WndConstants.Preview.SliderLeftImageIndex);
        return new WndPreviewPlan(left, null, null, null, null, sub, vertical, ResolveFillColor(single, isSeeThru, isImageWindow), ResolveBorderColor(single, isSeeThru), null, null, fontSize, false, false, isHidden);
    }

    private static WndPreviewPlan ApplySchemeAndBackdropContext(
        WndWindow window,
        WndPreviewPlan plan,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var single = plan.SingleImage;
        var underlay = plan.UnderlayImage;

        var name = window.Name ?? string.Empty;
        var drawCallback = window.GetProperty(WndConstants.PropertyKeys.DrawCallback) ?? string.Empty;

        var isTinyMarker = window.TryGetScreenRect(out var srect) && srect != null &&
            (srect.BottomRightX - srect.UpperLeftX <= 30 || srect.BottomRightY - srect.UpperLeftY <= 30);

        if ((name.EndsWith(":" + WndConstants.ControlBarScheme.BackgroundMarkerKey, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(name, WndConstants.ControlBarScheme.BackgroundMarkerKey, StringComparison.OrdinalIgnoreCase)) && isTinyMarker)
        {
            single = null;
        }
        else if (name.EndsWith(":Munkee", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "Munkee", StringComparison.OrdinalIgnoreCase) ||
                 name.EndsWith(":ControlBarParent", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(name, "ControlBarParent", StringComparison.OrdinalIgnoreCase))
        {
            if (overrides != null && overrides.TryGetValue(WndConstants.ControlBarScheme.BackgroundMarkerKey, out var schemeBg) && !string.IsNullOrWhiteSpace(schemeBg))
            {
                single = schemeBg;
            }
            else if (string.IsNullOrWhiteSpace(single))
            {
                single = WndConstants.ControlBarScheme.DefaultAmericaBaseGenerals;
            }
        }

        if (string.IsNullOrWhiteSpace(single) && !isTinyMarker)
        {
            single = ResolveSingleImageFallback(name, drawCallback, overrides);
        }

        if (string.Equals(single, "MainMenuRuler", StringComparison.OrdinalIgnoreCase))
        {
            underlay = ResolveOverrideOrFallback(overrides, "ShellMenuBackdrop", "MainMenuBackdrop");
        }

        if (single != plan.SingleImage || underlay != plan.UnderlayImage)
        {
            return plan with { SingleImage = single, UnderlayImage = underlay };
        }

        return plan;
    }

    private static string? ResolveSingleImageFallback(
        string name,
        string drawCallback,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (IsCommandBarBackground(name, drawCallback))
        {
            return ResolveOverrideOrFallback(overrides, "BackgroundMarker", "InGameUIAmericaBase");
        }

        if (name.EndsWith(":RightHUD", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "RightHUD", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "RightHUD", "SALogo");
        }

        return ResolveButtonOrMarkerFallback(name, overrides);
    }

    private static bool IsCommandBarBackground(string name, string drawCallback)
    {
        return drawCallback.Contains("W3DCommandBarBackgroundDraw", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(":BackgroundMarker", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "BackgroundMarker", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveButtonOrMarkerFallback(string name, IReadOnlyDictionary<string, string>? overrides)
    {
        if (name.EndsWith(":ButtonOptions", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "ButtonOptions", "SAOptions");
        }

        if (name.EndsWith(":ButtonIdleWorker", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "ButtonIdleWorker", "SAWorker");
        }

        if (name.EndsWith(":ButtonChat", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "ButtonChat", "SAChat");
        }

        if (name.EndsWith(":ButtonPlaceBeacon", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "ButtonPlaceBeacon", "SABeacon");
        }

        if (name.EndsWith(":ButtonGeneral", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "ButtonGeneral", "SAGeneral");
        }

        if (name.EndsWith(":ButtonUAttack", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "ButtonUAttack", "SAUAttackI");
        }

        if (name.EndsWith(":ExpBarForeground", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "ExpBarForeground", "SAExpBar");
        }

        if (name.Contains("ButtonCommand", StringComparison.OrdinalIgnoreCase) || name.Contains("CommandMarker", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveOverrideOrFallback(overrides, "QueueButtonImage", "SCBigButton");
        }

        return null;
    }

    private static string ResolveOverrideOrFallback(IReadOnlyDictionary<string, string>? overrides, string key, string fallback)
    {
        if (overrides != null && overrides.TryGetValue(key, out var val) && !string.IsNullOrWhiteSpace(val))
        {
            return val;
        }

        return fallback;
    }

    private static WndRgbaColor? ResolveFillColor(WndDrawDataEntry? entry, bool isSeeThru, bool isImageWindow)
    {
        // In SAGE engine (W3DGameWindow.cpp), windows with WIN_STATUS_IMAGE never call winFillRect.
        // Solid background fills only apply to windows without image status.
        if (isSeeThru || isImageWindow || entry == null || entry.IsEmpty)
        {
            return null;
        }

        // GUIEdit initializes newly created windows with an unconfigured sentinel red tint (255, 0, 0, 255).
        // In SAGE layouts, unconfigured window templates frequently retain this default without an image.
        // If the entry has no mapped image name, we treat pure opaque red (255, 0, 0, 255) as GUIEdit's dummy
        // placeholder and suppress the fill so the window canvas remains transparent rather than drawing an opaque red box.
        if (string.IsNullOrWhiteSpace(entry.Image) && entry.Color is { Red: 255, Green: 0, Blue: 0, Alpha: 255 })
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

    private static (WndRgbaColor? TextColor, int FontSize, bool FontBold, string? FontName) PlanTextStyle(WndWindow window)
    {
        WndRgbaColor? textColor = null;
        if (WndTextColorValue.TryParse(window.GetProperty(WndConstants.PropertyKeys.TextColor), out var colors) && colors != null)
        {
            textColor = colors.Enabled;
        }

        var fontSize = WndConstants.Editor.DefaultFontSize;
        var fontBold = false;
        string? fontName = null;
        if (WndFontValue.TryParse(window.GetProperty(WndConstants.PropertyKeys.Font), out var font) && font != null)
        {
            fontSize = Math.Max(1, font.Size);
            fontBold = font.Bold;
            fontName = string.IsNullOrWhiteSpace(font.Name) ? null : font.Name.Trim();
        }
        else
        {
            var rawFont = window.GetProperty(WndConstants.PropertyKeys.Font)?.Trim(' ', '"', '\x27', ';');
            if (!string.IsNullOrWhiteSpace(rawFont) && !rawFont.Contains(':') && !rawFont.Contains(','))
            {
                fontName = rawFont;
            }
        }

        return (textColor, fontSize, fontBold, fontName);
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
