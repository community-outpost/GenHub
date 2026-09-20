using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Tools.WndEditor;

/// <summary>
/// A single window node of a window definition (.wnd) document.
/// </summary>
public sealed class WndWindow
{
    /// <summary>
    /// Gets the stable identity of this window for editor tracking.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets or sets the control type name exactly as declared in the file.
    /// </summary>
    public string ControlTypeName { get; set; } = WndConstants.ControlTypes.User;

    /// <summary>
    /// Gets the recognized control type, or <see cref="WndControlType.Unknown"/> when the name is not recognized.
    /// </summary>
    public WndControlType ControlType => ParseControlType(ControlTypeName);

    /// <summary>
    /// Gets or sets the source file name this window was loaded from.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets the ordered properties of this window.
    /// </summary>
    public List<WndProperty> Properties { get; } = [];

    /// <summary>
    /// Gets the ordered child windows of this window.
    /// </summary>
    public List<WndWindow> Children { get; } = [];

    /// <summary>
    /// Gets the display name from the NAME property when present.
    /// </summary>
    public string? Name => GetProperty(WndConstants.PropertyKeys.Name);

    /// <summary>
    /// Maps a declared control type name to the recognized enum value.
    /// </summary>
    /// <param name="controlTypeName">The declared control type name.</param>
    /// <returns>The recognized control type or <see cref="WndControlType.Unknown"/>.</returns>
    public static WndControlType ParseControlType(string? controlTypeName)
    {
        return controlTypeName switch
        {
            WndConstants.ControlTypes.User => WndControlType.User,
            WndConstants.ControlTypes.PushButton => WndControlType.PushButton,
            WndConstants.ControlTypes.StaticText => WndControlType.StaticText,
            WndConstants.ControlTypes.EntryField => WndControlType.EntryField,
            WndConstants.ControlTypes.CheckBox => WndControlType.CheckBox,
            WndConstants.ControlTypes.RadioButton => WndControlType.RadioButton,
            WndConstants.ControlTypes.ProgressBar => WndControlType.ProgressBar,
            WndConstants.ControlTypes.HorzSlider => WndControlType.HorzSlider,
            WndConstants.ControlTypes.VertSlider => WndControlType.VertSlider,
            WndConstants.ControlTypes.ScrollListBox => WndControlType.ScrollListBox,
            WndConstants.ControlTypes.ComboBox => WndControlType.ComboBox,
            WndConstants.ControlTypes.CommandButton => WndControlType.CommandButton,
            WndConstants.ControlTypes.TabControl => WndControlType.TabControl,
            WndConstants.ControlTypes.TabPane => WndControlType.TabPane,
            _ => WndControlType.Unknown,
        };
    }

    /// <summary>
    /// Gets the value of a property by key, or null when absent.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>The property value or null.</returns>
    public string? GetProperty(string key)
    {
        return Properties.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.Ordinal))?.Value;
    }

    /// <summary>
    /// Sets a property value, replacing the first existing entry or appending a new one.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <param name="value">The property value.</param>
    public void SetProperty(string key, string value)
    {
        var index = Properties.FindIndex(p => string.Equals(p.Key, key, StringComparison.Ordinal));
        if (index >= 0)
        {
            Properties[index] = new WndProperty(key, value);
        }
        else
        {
            Properties.Add(new WndProperty(key, value));
        }
    }

    /// <summary>
    /// Removes all properties with the given key.
    /// </summary>
    /// <param name="key">The property key.</param>
    /// <returns>True when at least one property was removed.</returns>
    public bool RemoveProperty(string key)
    {
        return Properties.RemoveAll(p => string.Equals(p.Key, key, StringComparison.Ordinal)) > 0;
    }

    /// <summary>
    /// Tries to read the geometry from the SCREENRECT property.
    /// </summary>
    /// <param name="screenRect">The parsed rectangle when successful.</param>
    /// <returns>True when a SCREENRECT property exists and parses successfully.</returns>
    public bool TryGetScreenRect(out WndScreenRect? screenRect)
    {
        return WndScreenRect.TryParse(GetProperty(WndConstants.PropertyKeys.ScreenRect), out screenRect);
    }
}
