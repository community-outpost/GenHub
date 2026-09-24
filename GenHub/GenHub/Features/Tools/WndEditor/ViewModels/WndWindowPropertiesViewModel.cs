using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.WndEditor;
using GenHub.Core.Models.Tools.WndEditor;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// Typed property editors for the selected window, organized like the reference editor:
/// general properties, control specific data, other raw values, and raw text.
/// </summary>
[SuppressMessage("Major Code Smell", "S4144:Methods should not have identical implementations", Justification = "Generated property change partial methods dispatch UI edits.")]
public sealed partial class WndWindowPropertiesViewModel : ObservableObject
{
    private static readonly Lazy<IReadOnlyList<string>> SystemFontNames = new(CollectSystemFontNames);
    private readonly IWndDocumentService _documentService;
    private readonly INotificationService _notificationService;
    private readonly ILocalizationService _localizationService;
    private readonly Action<string, string> _commitEdit;
    private readonly Action<string> _removeProperty;
    private readonly Action<IReadOnlyList<WndProperty>> _replaceProperties;
    private readonly HashSet<string> _knownKeys;
    private bool _suppressCommit;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndWindowPropertiesViewModel"/> class.
    /// </summary>
    /// <param name="window">The edited window.</param>
    /// <param name="documentService">The document service.</param>
    /// <param name="notificationService">The notification service.</param>
    /// <param name="localizationService">The localization service.</param>
    /// <param name="commitEdit">Callback committing a property change with undo support.</param>
    /// <param name="removeProperty">Callback removing a property with undo support.</param>
    /// <param name="replaceProperties">Callback replacing all properties with undo support.</param>
    public WndWindowPropertiesViewModel(
        WndWindow window,
        IWndDocumentService documentService,
        INotificationService notificationService,
        ILocalizationService localizationService,
        Action<string, string> commitEdit,
        Action<string> removeProperty,
        Action<IReadOnlyList<WndProperty>> replaceProperties)
    {
        Window = window;
        _documentService = documentService;
        _notificationService = notificationService;
        _localizationService = localizationService;
        _commitEdit = commitEdit;
        _removeProperty = removeProperty;
        _replaceProperties = replaceProperties;
        _knownKeys = new HashSet<string>(KnownTypedKeys(), StringComparer.OrdinalIgnoreCase);
        _windowTypeOptions = BuildWindowTypeOptions();
        RefreshFromWindow();
    }

    /// <summary>
    /// Gets the edited window.
    /// </summary>
    public WndWindow Window { get; }

    /// <summary>
    /// Gets the style flag editors.
    /// </summary>
    public ObservableCollection<WndFlagViewModel> StyleFlags { get; } = [];

    /// <summary>
    /// Gets the basic status flag editors.
    /// </summary>
    public ObservableCollection<WndFlagViewModel> BasicStatusFlags { get; } = [];

    /// <summary>
    /// Gets the interaction status flag editors.
    /// </summary>
    public ObservableCollection<WndFlagViewModel> InteractionStatusFlags { get; } = [];

    /// <summary>
    /// Gets the miscellaneous status flag editors.
    /// </summary>
    public ObservableCollection<WndFlagViewModel> MiscStatusFlags { get; } = [];

    /// <summary>
    /// Gets the enabled draw data entry editors.
    /// </summary>
    public ObservableCollection<WndDrawDataEntryViewModel> EnabledDrawData { get; } = [];

    /// <summary>
    /// Gets the disabled draw data entry editors.
    /// </summary>
    public ObservableCollection<WndDrawDataEntryViewModel> DisabledDrawData { get; } = [];

    /// <summary>
    /// Gets the hilite draw data entry editors.
    /// </summary>
    public ObservableCollection<WndDrawDataEntryViewModel> HiliteDrawData { get; } = [];

    /// <summary>
    /// Gets the raw rows for properties without typed editors.
    /// </summary>
    public ObservableCollection<WndPropertyRowViewModel> OtherRows { get; } = [];

    /// <summary>
    /// Gets the available window type names.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _windowTypeOptions;

    /// <summary>
    /// Gets or sets the selected window type name.
    /// </summary>
    [ObservableProperty]
    private string _selectedWindowType = string.Empty;

    /// <summary>
    /// Gets or sets the short window name.
    /// </summary>
    [ObservableProperty]
    private string _shortName = string.Empty;

    /// <summary>
    /// Gets or sets the full decorated name preview.
    /// </summary>
    [ObservableProperty]
    private string _decoratedPreview = string.Empty;

    /// <summary>
    /// Gets or sets the upper-left X coordinate.
    /// </summary>
    [ObservableProperty]
    private int _upperLeftX;

    /// <summary>
    /// Gets or sets the upper-left Y coordinate.
    /// </summary>
    [ObservableProperty]
    private int _upperLeftY;

    /// <summary>
    /// Gets or sets the bottom-right X coordinate.
    /// </summary>
    [ObservableProperty]
    private int _bottomRightX;

    /// <summary>
    /// Gets or sets the bottom-right Y coordinate.
    /// </summary>
    [ObservableProperty]
    private int _bottomRightY;

    /// <summary>
    /// Gets or sets the calculated window width (BottomRightX - UpperLeftX).
    /// Changing this updates BottomRightX.
    /// </summary>
    public int WindowWidth
    {
        get => Math.Max(0, BottomRightX - UpperLeftX);
        set
        {
            var clamped = Math.Max(0, value);
            if (WindowWidth != clamped)
            {
                BottomRightX = UpperLeftX + clamped;
            }
        }
    }

    /// <summary>
    /// Gets or sets the calculated window height (BottomRightY - UpperLeftY).
    /// Changing this updates BottomRightY.
    /// </summary>
    public int WindowHeight
    {
        get => Math.Max(0, BottomRightY - UpperLeftY);
        set
        {
            var clamped = Math.Max(0, value);
            if (WindowHeight != clamped)
            {
                BottomRightY = UpperLeftY + clamped;
            }
        }
    }

    /// <summary>
    /// Gets or sets the creation resolution width.
    /// </summary>
    [ObservableProperty]
    private int _creationWidth;

    /// <summary>
    /// Gets or sets the creation resolution height.
    /// </summary>
    [ObservableProperty]
    private int _creationHeight;

    /// <summary>
    /// Gets or sets a value indicating whether the window declares a screen rect.
    /// </summary>
    [ObservableProperty]
    private bool _hasScreenRect;

    /// <summary>
    /// Gets or sets the unrecognized status flags display text.
    /// </summary>
    [ObservableProperty]
    private string _unknownStatusFlags = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether unrecognized status flags exist.
    /// </summary>
    [ObservableProperty]
    private bool _hasUnknownStatusFlags;

    /// <summary>
    /// Gets or sets the text label.
    /// </summary>
    [ObservableProperty]
    private string _text = string.Empty;

    /// <summary>
    /// Gets or sets the tooltip text.
    /// </summary>
    [ObservableProperty]
    private string _tooltipText = string.Empty;

    /// <summary>
    /// Gets or sets the tooltip delay.
    /// </summary>
    [ObservableProperty]
    private int _tooltipDelay;

    /// <summary>
    /// Gets or sets the font name.
    /// </summary>
    [ObservableProperty]
    private string _fontName = string.Empty;

    /// <summary>
    /// Gets the selectable font names (installed system fonts plus the current value).
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _fontNameOptions = [];

    /// <summary>
    /// Gets or sets the known mapped image names offered by the art picker.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<string> _imageNameOptions = [];

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    [ObservableProperty]
    private int _fontSize;

    /// <summary>
    /// Gets or sets a value indicating whether the font is bold.
    /// </summary>
    [ObservableProperty]
    private bool _fontBold;

    /// <summary>
    /// Gets or sets the enabled text color editor.
    /// </summary>
    [ObservableProperty]
    private WndRgbaViewModel? _enabledTextColor;

    /// <summary>
    /// Gets or sets the enabled text border color editor.
    /// </summary>
    [ObservableProperty]
    private WndRgbaViewModel? _enabledTextBorderColor;

    /// <summary>
    /// Gets or sets the disabled text color editor.
    /// </summary>
    [ObservableProperty]
    private WndRgbaViewModel? _disabledTextColor;

    /// <summary>
    /// Gets or sets the disabled text border color editor.
    /// </summary>
    [ObservableProperty]
    private WndRgbaViewModel? _disabledTextBorderColor;

    /// <summary>
    /// Gets or sets the hilite text color editor.
    /// </summary>
    [ObservableProperty]
    private WndRgbaViewModel? _hiliteTextColor;

    /// <summary>
    /// Gets or sets the hilite text border color editor.
    /// </summary>
    [ObservableProperty]
    private WndRgbaViewModel? _hiliteTextBorderColor;

    /// <summary>
    /// Gets or sets the system callback name.
    /// </summary>
    [ObservableProperty]
    private string _systemCallback = string.Empty;

    /// <summary>
    /// Gets or sets the input callback name.
    /// </summary>
    [ObservableProperty]
    private string _inputCallback = string.Empty;

    /// <summary>
    /// Gets or sets the tooltip callback name.
    /// </summary>
    [ObservableProperty]
    private string _tooltipCallback = string.Empty;

    /// <summary>
    /// Gets or sets the draw callback name.
    /// </summary>
    [ObservableProperty]
    private string _drawCallback = string.Empty;

    /// <summary>
    /// Gets or sets the header template name.
    /// </summary>
    [ObservableProperty]
    private string _headerTemplate = string.Empty;

    /// <summary>
    /// Gets or sets the image offset X.
    /// </summary>
    [ObservableProperty]
    private int _imageOffsetX;

    /// <summary>
    /// Gets or sets the image offset Y.
    /// </summary>
    [ObservableProperty]
    private int _imageOffsetY;

    /// <summary>
    /// Gets or sets the recognized control kind.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStaticText))]
    [NotifyPropertyChangedFor(nameof(IsEntryField))]
    [NotifyPropertyChangedFor(nameof(IsSlider))]
    [NotifyPropertyChangedFor(nameof(IsListbox))]
    [NotifyPropertyChangedFor(nameof(IsComboBox))]
    [NotifyPropertyChangedFor(nameof(IsRadioButton))]
    [NotifyPropertyChangedFor(nameof(IsTabControl))]
    [NotifyPropertyChangedFor(nameof(IsControlDataSupported))]
    [NotifyPropertyChangedFor(nameof(ShowMissingControlDataHint))]
    private WndControlType _controlKind;

    /// <summary>
    /// Gets or sets a value indicating whether the control declares typed gadget data.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMissingControlDataHint))]
    private bool _hasControlData;

    /// <summary>
    /// Gets a value indicating whether the control is static text.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsStaticText => ControlKind == WndControlType.StaticText;

    /// <summary>
    /// Gets a value indicating whether the control is a text entry field.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsEntryField => ControlKind == WndControlType.EntryField;

    /// <summary>
    /// Gets a value indicating whether the control is a slider.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsSlider => ControlKind == WndControlType.HorzSlider || ControlKind == WndControlType.VertSlider;

    /// <summary>
    /// Gets a value indicating whether the control is a list box.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsListbox => ControlKind == WndControlType.ScrollListBox;

    /// <summary>
    /// Gets a value indicating whether the control is a combo box.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsComboBox => ControlKind == WndControlType.ComboBox;

    /// <summary>
    /// Gets a value indicating whether the control is a radio button.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsRadioButton => ControlKind == WndControlType.RadioButton;

    /// <summary>
    /// Gets a value indicating whether the control is a tab control.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsTabControl => ControlKind == WndControlType.TabControl;

    /// <summary>
    /// Gets a value indicating whether the control kind supports a typed data block.
    /// </summary>
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to current control type for MVVM view binding.")]
    public bool IsControlDataSupported => ControlDataKey(ControlKind) != null;

    /// <summary>
    /// Gets a value indicating whether the missing data hint applies.
    /// </summary>
    public bool ShowMissingControlDataHint => IsControlDataSupported && !HasControlData;

    /// <summary>
    /// Gets or sets a value indicating whether static text is centered.
    /// </summary>
    [ObservableProperty]
    private bool _staticCentered;

    /// <summary>
    /// Gets or sets the text entry maximum length.
    /// </summary>
    [ObservableProperty]
    private int _entryMaxLen;

    /// <summary>
    /// Gets or sets a value indicating whether text entry masks input.
    /// </summary>
    [ObservableProperty]
    private bool _entrySecretText;

    /// <summary>
    /// Gets or sets a value indicating whether text entry accepts numbers only.
    /// </summary>
    [ObservableProperty]
    private bool _entryNumericalOnly;

    /// <summary>
    /// Gets or sets a value indicating whether text entry accepts letters and numbers only.
    /// </summary>
    [ObservableProperty]
    private bool _entryAlphaNumericalOnly;

    /// <summary>
    /// Gets or sets a value indicating whether text entry accepts ASCII only.
    /// </summary>
    [ObservableProperty]
    private bool _entryAsciiOnly;

    /// <summary>
    /// Gets or sets the slider minimum value.
    /// </summary>
    [ObservableProperty]
    private int _sliderMinValue;

    /// <summary>
    /// Gets or sets the slider maximum value.
    /// </summary>
    [ObservableProperty]
    private int _sliderMaxValue;

    /// <summary>
    /// Gets or sets the list box maximum entries.
    /// </summary>
    [ObservableProperty]
    private int _listLength;

    /// <summary>
    /// Gets or sets a value indicating whether the list box scrolls to new entries.
    /// </summary>
    [ObservableProperty]
    private bool _listAutoScroll;

    /// <summary>
    /// Gets or sets a value indicating whether the list box declares scroll-if-at-end.
    /// </summary>
    [ObservableProperty]
    private bool _listHasScrollIfAtEnd;

    /// <summary>
    /// Gets or sets a value indicating whether the list box scrolls when at the end.
    /// </summary>
    [ObservableProperty]
    private bool _listScrollIfAtEnd;

    /// <summary>
    /// Gets or sets a value indicating whether the list box purges old entries.
    /// </summary>
    [ObservableProperty]
    private bool _listAutoPurge;

    /// <summary>
    /// Gets or sets a value indicating whether the list box shows a scroll bar.
    /// </summary>
    [ObservableProperty]
    private bool _listScrollBar;

    /// <summary>
    /// Gets or sets a value indicating whether the list box allows multiple selection.
    /// </summary>
    [ObservableProperty]
    private bool _listMultiSelect;

    /// <summary>
    /// Gets or sets the list box column count.
    /// </summary>
    [ObservableProperty]
    private int _listColumns;

    /// <summary>
    /// Gets or sets the list box column widths as comma separated percentages.
    /// </summary>
    [ObservableProperty]
    private string _listColumnWidths = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the list box always selects an entry.
    /// </summary>
    [ObservableProperty]
    private bool _listForceSelect;

    /// <summary>
    /// Gets or sets a value indicating whether the combo box is editable.
    /// </summary>
    [ObservableProperty]
    private bool _comboIsEditable;

    /// <summary>
    /// Gets or sets the combo box maximum characters.
    /// </summary>
    [ObservableProperty]
    private int _comboMaxChars;

    /// <summary>
    /// Gets or sets the combo box maximum displayed entries.
    /// </summary>
    [ObservableProperty]
    private int _comboMaxDisplay;

    /// <summary>
    /// Gets or sets a value indicating whether the combo box accepts ASCII only.
    /// </summary>
    [ObservableProperty]
    private bool _comboAsciiOnly;

    /// <summary>
    /// Gets or sets a value indicating whether the combo box accepts letters and numbers only.
    /// </summary>
    [ObservableProperty]
    private bool _comboLettersAndNumbersOnly;

    /// <summary>
    /// Gets or sets the radio button group.
    /// </summary>
    [ObservableProperty]
    private int _radioGroup;

    /// <summary>
    /// Gets or sets the tab control orientation.
    /// </summary>
    [ObservableProperty]
    private int _tabOrientation;

    /// <summary>
    /// Gets or sets the tab control edge.
    /// </summary>
    [ObservableProperty]
    private int _tabEdge;

    /// <summary>
    /// Gets or sets the tab control width.
    /// </summary>
    [ObservableProperty]
    private int _tabWidth;

    /// <summary>
    /// Gets or sets the tab control height.
    /// </summary>
    [ObservableProperty]
    private int _tabHeight;

    /// <summary>
    /// Gets or sets the tab control count.
    /// </summary>
    [ObservableProperty]
    private int _tabCount;

    /// <summary>
    /// Gets or sets the tab control pane border.
    /// </summary>
    [ObservableProperty]
    private int _tabPaneBorder;

    /// <summary>
    /// Gets or sets the tab control per-pane disabled flags as comma separated values.
    /// </summary>
    [ObservableProperty]
    private string _tabPaneDisabled = string.Empty;

    /// <summary>
    /// Gets or sets the key input for adding a raw property.
    /// </summary>
    [ObservableProperty]
    private string _newPropertyKey = string.Empty;

    /// <summary>
    /// Gets or sets the value input for adding a raw property.
    /// </summary>
    [ObservableProperty]
    private string _newPropertyValue = string.Empty;

    /// <summary>
    /// Gets or sets the raw statement draft.
    /// </summary>
    [ObservableProperty]
    private string _rawText = string.Empty;

    /// <summary>
    /// Commits any in-flight color picker edits. Called before the panel is discarded
    /// (selection change) because an open flyout does not reliably raise Closed then.
    /// </summary>
    public void FlushPendingEdits()
    {
        EnabledTextColor?.EndColorEdit();
        EnabledTextBorderColor?.EndColorEdit();
        DisabledTextColor?.EndColorEdit();
        DisabledTextBorderColor?.EndColorEdit();
        HiliteTextColor?.EndColorEdit();
        HiliteTextBorderColor?.EndColorEdit();
        foreach (var collection in new[] { EnabledDrawData, DisabledDrawData, HiliteDrawData })
        {
            foreach (var entry in collection)
            {
                entry.Color.EndColorEdit();
                entry.BorderColor.EndColorEdit();
            }
        }
    }

    /// <summary>
    /// Reloads every editor from the window without committing edits.
    /// </summary>
    public void RefreshFromWindow()
    {
        _suppressCommit = true;
        try
        {
            RefreshIdentity();
            RefreshPosition();
            RefreshStatus();
            RefreshText();
            RefreshFont();
            RefreshTextColor();
            RefreshCallbacks();
            RefreshDrawData();
            RefreshControlData();
            RefreshOther();
            RefreshRaw();
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    private static IEnumerable<string> KnownTypedKeys()
    {
        var keys = new List<string>
        {
            WndConstants.PropertyKeys.WindowType,
            WndConstants.PropertyKeys.ScreenRect,
            WndConstants.PropertyKeys.Name,
            WndConstants.PropertyKeys.Status,
            WndConstants.PropertyKeys.Style,
            WndConstants.PropertyKeys.SystemCallback,
            WndConstants.PropertyKeys.InputCallback,
            WndConstants.PropertyKeys.TooltipCallback,
            WndConstants.PropertyKeys.DrawCallback,
            WndConstants.PropertyKeys.Font,
            WndConstants.PropertyKeys.HeaderTemplate,
            WndConstants.PropertyKeys.ListboxData,
            WndConstants.PropertyKeys.ComboBoxData,
            WndConstants.PropertyKeys.SliderData,
            WndConstants.PropertyKeys.RadioButtonData,
            WndConstants.PropertyKeys.TooltipText,
            WndConstants.PropertyKeys.TooltipDelay,
            WndConstants.PropertyKeys.Text,
            WndConstants.PropertyKeys.TextColor,
            WndConstants.PropertyKeys.StaticTextData,
            WndConstants.PropertyKeys.TextEntryData,
            WndConstants.PropertyKeys.TabControlData,
            WndConstants.PropertyKeys.EnabledDrawData,
            WndConstants.PropertyKeys.DisabledDrawData,
            WndConstants.PropertyKeys.HiliteDrawData,
            WndConstants.PropertyKeys.ImageOffset,
        };
        return keys;
    }

    private static string UnquoteOrEmpty(string? value)
    {
        return value == null ? string.Empty : WndValueTokenizer.Unquote(value);
    }

    private static string FormatQuoted(string value)
    {
        return WndValueTokenizer.Quote(value.Trim());
    }

    private static int ParseOptionalInt(string? value)
    {
        return value != null && int.TryParse(value.Trim(), out var parsed) ? parsed : 0;
    }

    private static string FormatToggled(WndStatusValue value, IReadOnlyList<string> canonicalOrder, string flag, bool isSet)
    {
        var flags = new HashSet<string>(value.Flags, StringComparer.OrdinalIgnoreCase);
        if (isSet)
        {
            flags.Add(flag);
        }
        else
        {
            flags.Remove(flag);
        }

        return new WndStatusValue(flags, value.UnknownTokens).ToString(canonicalOrder);
    }

    private static bool TryParseIntList(string text, out List<int> values)
    {
        values = [];
        foreach (var token in SplitListTokens(text))
        {
            if (!int.TryParse(token, out var parsed))
            {
                return false;
            }

            values.Add(parsed);
        }

        return true;
    }

    private static bool TryParseBoolList(string text, out List<bool> values)
    {
        values = [];
        foreach (var token in SplitListTokens(text))
        {
            if (!WndValueTokenizer.TryParseBool(token, out var parsed))
            {
                return false;
            }

            values.Add(parsed);
        }

        return true;
    }

    private static IEnumerable<string> SplitListTokens(string text)
    {
        return text
            .Split([WndConstants.Syntax.ComponentSeparator, ' ', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0);
    }

    private List<string> BuildWindowTypeOptions()
    {
        var options = new List<string>(WndConstants.StyleTypes.All) { WndConstants.ControlTypes.CommandButton };
        if (!options.Contains(Window.ControlTypeName, StringComparer.Ordinal))
        {
            options.Add(Window.ControlTypeName);
        }

        return options;
    }

    private void RefreshIdentity()
    {
        SelectedWindowType = Window.ControlTypeName;
        var name = WndDecoratedName.Parse(Window.GetProperty(WndConstants.PropertyKeys.Name));
        ShortName = name.ShortName;
        DecoratedPreview = Window.GetProperty(WndConstants.PropertyKeys.Name) ?? string.Empty;
        ControlKind = Window.ControlType;
        RefreshFlagCollection(StyleFlags, WndConstants.StyleTypes.All, WndStatusValue.ParseStyle(Window.GetProperty(WndConstants.PropertyKeys.Style)), CommitStyle);
    }

    private void RefreshPosition()
    {
        if (Window.TryGetScreenRect(out var rect) && rect != null)
        {
            HasScreenRect = true;
            UpperLeftX = rect.UpperLeftX;
            UpperLeftY = rect.UpperLeftY;
            BottomRightX = rect.BottomRightX;
            BottomRightY = rect.BottomRightY;
            CreationWidth = rect.CreationWidth;
            CreationHeight = rect.CreationHeight;
        }
        else
        {
            HasScreenRect = false;
            UpperLeftX = 0;
            UpperLeftY = 0;
            BottomRightX = (int)WndConstants.Editor.MinCanvasWidth;
            BottomRightY = (int)WndConstants.Editor.MinCanvasHeight;
            CreationWidth = (int)WndConstants.Editor.MinCanvasWidth;
            CreationHeight = (int)WndConstants.Editor.MinCanvasHeight;
        }

        OnPropertyChanged(nameof(WindowWidth));
        OnPropertyChanged(nameof(WindowHeight));
    }

    private void RefreshStatus()
    {
        var status = WndStatusValue.ParseStatus(Window.GetProperty(WndConstants.PropertyKeys.Status));
        RefreshFlagCollection(BasicStatusFlags, WndConstants.StatusFlags.Basic, status, CommitStatus);
        RefreshFlagCollection(InteractionStatusFlags, WndConstants.StatusFlags.Interaction, status, CommitStatus);
        RefreshFlagCollection(MiscStatusFlags, WndConstants.StatusFlags.Misc, status, CommitStatus);
        HasUnknownStatusFlags = status.UnknownTokens.Count > 0;
        UnknownStatusFlags = string.Join(WndConstants.Syntax.FlagSeparator, status.UnknownTokens);
    }

    private static void RefreshFlagCollection(
        ObservableCollection<WndFlagViewModel> collection,
        IReadOnlyList<string> names,
        WndStatusValue value,
        Action<string, bool> commit)
    {
        collection.Clear();
        foreach (var name in names)
        {
            var flagName = name;
            collection.Add(new WndFlagViewModel(name, value.Flags.Contains(name, StringComparer.OrdinalIgnoreCase), isSet => commit(flagName, isSet)));
        }
    }

    private void RefreshText()
    {
        Text = UnquoteOrEmpty(Window.GetProperty(WndConstants.PropertyKeys.Text));
        TooltipText = UnquoteOrEmpty(Window.GetProperty(WndConstants.PropertyKeys.TooltipText));
        TooltipDelay = ParseOptionalInt(Window.GetProperty(WndConstants.PropertyKeys.TooltipDelay));
    }

    private void RefreshFont()
    {
        if (WndFontValue.TryParse(Window.GetProperty(WndConstants.PropertyKeys.Font), out var font) && font != null)
        {
            FontName = font.Name;
            FontSize = font.Size;
            FontBold = font.Bold;
        }
        else
        {
            FontName = string.Empty;
            FontSize = WndConstants.Editor.DefaultFontSize;
            FontBold = false;
        }

        FontNameOptions = RefreshFontNameOptions(FontName);
    }

    private static IReadOnlyList<string> RefreshFontNameOptions(string? fontName)
    {
        var systemFonts = SystemFontNames.Value;
        if (string.IsNullOrWhiteSpace(fontName)
            || systemFonts.Contains(fontName, StringComparer.OrdinalIgnoreCase))
        {
            return systemFonts;
        }

        return new[] { fontName }.Concat(systemFonts).ToList();
    }

    private static IReadOnlyList<string> CollectSystemFontNames()
    {
        try
        {
            var names = Avalonia.Media.FontManager.Current.SystemFonts
                .Select(key => key.ToString())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (names.Count > 0)
            {
                return names;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or NotSupportedException)
        {
            _ = ex;
            return FallbackFontNames();
        }

        return FallbackFontNames();
    }

    private static IReadOnlyList<string> FallbackFontNames()
    {
        return new List<string> { "Arial", "Courier New", "Tahoma", "Times New Roman", "Verdana" };
    }

    private void RefreshTextColor()
    {
        if (!WndTextColorValue.TryParse(Window.GetProperty(WndConstants.PropertyKeys.TextColor), out var parsed) || parsed == null)
        {
            parsed = new WndTextColorValue(
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White,
                WndRgbaColor.White);
        }

        EnabledTextColor = new WndRgbaViewModel(parsed.Enabled, CommitTextColor);
        EnabledTextBorderColor = new WndRgbaViewModel(parsed.EnabledBorder, CommitTextColor);
        DisabledTextColor = new WndRgbaViewModel(parsed.Disabled, CommitTextColor);
        DisabledTextBorderColor = new WndRgbaViewModel(parsed.DisabledBorder, CommitTextColor);
        HiliteTextColor = new WndRgbaViewModel(parsed.Hilite, CommitTextColor);
        HiliteTextBorderColor = new WndRgbaViewModel(parsed.HiliteBorder, CommitTextColor);
    }

    private void RefreshCallbacks()
    {
        SystemCallback = UnquoteOrEmpty(Window.GetProperty(WndConstants.PropertyKeys.SystemCallback));
        InputCallback = UnquoteOrEmpty(Window.GetProperty(WndConstants.PropertyKeys.InputCallback));
        TooltipCallback = UnquoteOrEmpty(Window.GetProperty(WndConstants.PropertyKeys.TooltipCallback));
        DrawCallback = UnquoteOrEmpty(Window.GetProperty(WndConstants.PropertyKeys.DrawCallback));
        HeaderTemplate = UnquoteOrEmpty(Window.GetProperty(WndConstants.PropertyKeys.HeaderTemplate));
        if (WndImageOffset.TryParse(Window.GetProperty(WndConstants.PropertyKeys.ImageOffset), out var offset) && offset != null)
        {
            ImageOffsetX = offset.X;
            ImageOffsetY = offset.Y;
        }
        else
        {
            ImageOffsetX = 0;
            ImageOffsetY = 0;
        }
    }

    private void RefreshDrawData()
    {
        RefreshDrawDataCollection(EnabledDrawData, WndConstants.PropertyKeys.EnabledDrawData);
        RefreshDrawDataCollection(DisabledDrawData, WndConstants.PropertyKeys.DisabledDrawData);
        RefreshDrawDataCollection(HiliteDrawData, WndConstants.PropertyKeys.HiliteDrawData);
    }

    private void RefreshDrawDataCollection(ObservableCollection<WndDrawDataEntryViewModel> collection, string key)
    {
        if (!WndDrawDataSet.TryParse(Window.GetProperty(key), out var parsed) || parsed == null)
        {
            parsed = WndDrawDataSet.Empty;
        }

        collection.Clear();
        for (var i = 0; i < parsed.Entries.Count; i++)
        {
            collection.Add(new WndDrawDataEntryViewModel(i, parsed.Entries[i], () => CommitDrawData(key)));
        }
    }

    private void RefreshOther()
    {
        OtherRows.Clear();
        foreach (var property in Window.Properties.Where(property => !_knownKeys.Contains(property.Key)))
        {
            OtherRows.Add(new WndPropertyRowViewModel(property.Key, property.Value, _commitEdit));
        }
    }

    private void RefreshRaw()
    {
        RawText = string.Join(Environment.NewLine, Window.Properties.Select(property =>
            string.Concat(
                property.Key,
                ' ',
                WndConstants.Syntax.KeyValueSeparator,
                ' ',
                property.Value,
                WndConstants.Syntax.StatementTerminator)));
    }

    private void RefreshControlData()
    {
        RefreshStaticTextData();
        RefreshTextEntryData();
        RefreshSliderData();
        RefreshListboxData();
        RefreshComboBoxData();
        RefreshRadioButtonData();
        RefreshTabControlData();
        var controlDataKey = ControlDataKey(ControlKind);
        HasControlData = controlDataKey != null && Window.GetProperty(controlDataKey) != null;
    }

    private void RefreshStaticTextData()
    {
        if (WndStaticTextData.TryParse(Window.GetProperty(WndConstants.PropertyKeys.StaticTextData), out var data) && data != null)
        {
            StaticCentered = data.Centered;
        }
        else
        {
            StaticCentered = false;
        }
    }

    private void RefreshTextEntryData()
    {
        if (WndTextEntryData.TryParse(Window.GetProperty(WndConstants.PropertyKeys.TextEntryData), out var data) && data != null)
        {
            EntryMaxLen = data.MaxLen;
            EntrySecretText = data.SecretText;
            EntryNumericalOnly = data.NumericalOnly;
            EntryAlphaNumericalOnly = data.AlphaNumericalOnly;
            EntryAsciiOnly = data.AsciiOnly;
        }
        else
        {
            EntryMaxLen = 0;
            EntrySecretText = false;
            EntryNumericalOnly = false;
            EntryAlphaNumericalOnly = false;
            EntryAsciiOnly = false;
        }
    }

    private void RefreshSliderData()
    {
        if (WndSliderData.TryParse(Window.GetProperty(WndConstants.PropertyKeys.SliderData), out var data) && data != null)
        {
            SliderMinValue = data.MinValue;
            SliderMaxValue = data.MaxValue;
        }
        else
        {
            SliderMinValue = 0;
            SliderMaxValue = 0;
        }
    }

    private void RefreshListboxData()
    {
        if (WndListboxData.TryParse(Window.GetProperty(WndConstants.PropertyKeys.ListboxData), out var data) && data != null)
        {
            ListLength = data.Length;
            ListAutoScroll = data.AutoScroll;
            ListHasScrollIfAtEnd = data.ScrollIfAtEnd.HasValue;
            ListScrollIfAtEnd = data.ScrollIfAtEnd ?? false;
            ListAutoPurge = data.AutoPurge;
            ListScrollBar = data.ScrollBar;
            ListMultiSelect = data.MultiSelect;
            ListColumns = data.Columns;
            ListColumnWidths = string.Join(WndConstants.Syntax.ComponentListSeparator, data.ColumnWidths);
            ListForceSelect = data.ForceSelect;
        }
        else
        {
            ListLength = 0;
            ListAutoScroll = false;
            ListHasScrollIfAtEnd = false;
            ListScrollIfAtEnd = false;
            ListAutoPurge = false;
            ListScrollBar = false;
            ListMultiSelect = false;
            ListColumns = 0;
            ListColumnWidths = string.Empty;
            ListForceSelect = false;
        }
    }

    private void RefreshComboBoxData()
    {
        if (WndComboBoxData.TryParse(Window.GetProperty(WndConstants.PropertyKeys.ComboBoxData), out var data) && data != null)
        {
            ComboIsEditable = data.IsEditable;
            ComboMaxChars = data.MaxChars;
            ComboMaxDisplay = data.MaxDisplay;
            ComboAsciiOnly = data.AsciiOnly;
            ComboLettersAndNumbersOnly = data.LettersAndNumbersOnly;
        }
        else
        {
            ComboIsEditable = false;
            ComboMaxChars = 0;
            ComboMaxDisplay = 0;
            ComboAsciiOnly = false;
            ComboLettersAndNumbersOnly = false;
        }
    }

    private void RefreshRadioButtonData()
    {
        if (WndRadioButtonData.TryParse(Window.GetProperty(WndConstants.PropertyKeys.RadioButtonData), out var data) && data != null)
        {
            RadioGroup = data.Group;
        }
        else
        {
            RadioGroup = 0;
        }
    }

    private void RefreshTabControlData()
    {
        if (WndTabControlData.TryParse(Window.GetProperty(WndConstants.PropertyKeys.TabControlData), out var data) && data != null)
        {
            TabOrientation = data.TabOrientation;
            TabEdge = data.TabEdge;
            TabWidth = data.TabWidth;
            TabHeight = data.TabHeight;
            TabCount = data.TabCount;
            TabPaneBorder = data.PaneBorder;
            TabPaneDisabled = string.Join(WndConstants.Syntax.ComponentListSeparator, data.PaneDisabled.Select(WndValueTokenizer.FormatBool));
        }
        else
        {
            TabOrientation = 0;
            TabEdge = 0;
            TabWidth = 0;
            TabHeight = 0;
            TabCount = 0;
            TabPaneBorder = 0;
            TabPaneDisabled = string.Empty;
        }
    }

    private static string? ControlDataKey(WndControlType kind)
    {
        return kind switch
        {
            WndControlType.StaticText => WndConstants.PropertyKeys.StaticTextData,
            WndControlType.EntryField => WndConstants.PropertyKeys.TextEntryData,
            WndControlType.HorzSlider => WndConstants.PropertyKeys.SliderData,
            WndControlType.VertSlider => WndConstants.PropertyKeys.SliderData,
            WndControlType.ScrollListBox => WndConstants.PropertyKeys.ListboxData,
            WndControlType.ComboBox => WndConstants.PropertyKeys.ComboBoxData,
            WndControlType.RadioButton => WndConstants.PropertyKeys.RadioButtonData,
            WndControlType.TabControl => WndConstants.PropertyKeys.TabControlData,
            _ => null,
        };
    }

    /// <summary>
    /// Adds the entered raw property to the window.
    /// </summary>
    [RelayCommand]
    private void AddProperty()
    {
        var key = NewPropertyKey.Trim();
        if (key.Length == 0)
        {
            _notificationService.ShowWarning(
                _localizationService.GetString("Tools.WndEditor.Property.EmptyKeyTitle"),
                _localizationService.GetString("Tools.WndEditor.Property.EmptyKeyMessage"),
                NotificationDurations.Short);
            return;
        }

        if (key.IndexOfAny([';', '\r', '\n', '"']) >= 0 || NewPropertyValue.IndexOfAny([';', '\r', '\n', '"']) >= 0)
        {
            _notificationService.ShowWarning(
                _localizationService.GetString("Tools.WndEditor.Property.InvalidCharactersTitle"),
                _localizationService.GetString("Tools.WndEditor.Property.InvalidCharactersMessage"),
                NotificationDurations.Short);
            return;
        }

        if (Window.GetProperty(key) != null)
        {
            _notificationService.ShowWarning(
                _localizationService.GetString("Tools.WndEditor.Property.DuplicateKeyTitle"),
                _localizationService.GetString("Tools.WndEditor.Property.DuplicateKeyMessage", key),
                NotificationDurations.Short);
            return;
        }

        _commitEdit(key, NewPropertyValue);
        NewPropertyKey = string.Empty;
        NewPropertyValue = string.Empty;
    }

    /// <summary>
    /// Deletes a raw property row from the window.
    /// </summary>
    /// <param name="row">The property row to delete.</param>
    [RelayCommand]
    private void DeleteProperty(WndPropertyRowViewModel? row)
    {
        if (row != null)
        {
            _removeProperty(row.Key);
        }
    }

    /// <summary>
    /// Parses the raw draft and replaces the window properties.
    /// </summary>
    [RelayCommand]
    private void ApplyRawText()
    {
        var result = _documentService.ParseStatements(RawText);
        if (!result.Success || result.Data == null)
        {
            _notificationService.ShowError(
                _localizationService.GetString("Tools.WndEditor.Raw.InvalidTitle"),
                _localizationService.GetString("Tools.WndEditor.Raw.InvalidMessage", result.FirstError ?? string.Empty),
                NotificationDurations.Long);
            return;
        }

        _replaceProperties(result.Data);
        _notificationService.ShowSuccess(
            _localizationService.GetString("Tools.WndEditor.Raw.AppliedTitle"),
            _localizationService.GetString("Tools.WndEditor.Raw.AppliedMessage"),
            NotificationDurations.Medium);
    }

    /// <summary>
    /// Discards the raw draft and reloads it from the window.
    /// </summary>
    [RelayCommand]
    private void ResetRawText()
    {
        _suppressCommit = true;
        try
        {
            RefreshRaw();
        }
        finally
        {
            _suppressCommit = false;
        }
    }

    partial void OnSelectedWindowTypeChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(CommitWindowType);
    }

    partial void OnShortNameChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(CommitShortName);
    }

    partial void OnUpperLeftXChanged(int value)
    {
        OnPropertyChanged(nameof(WindowWidth));
        CommitPositionProperty(nameof(UpperLeftX));
    }

    partial void OnUpperLeftYChanged(int value)
    {
        OnPropertyChanged(nameof(WindowHeight));
        CommitPositionProperty(nameof(UpperLeftY));
    }

    partial void OnBottomRightXChanged(int value)
    {
        OnPropertyChanged(nameof(WindowWidth));
        CommitPositionProperty(nameof(BottomRightX));
    }

    partial void OnBottomRightYChanged(int value)
    {
        OnPropertyChanged(nameof(WindowHeight));
        CommitPositionProperty(nameof(BottomRightY));
    }

    partial void OnCreationWidthChanged(int value) => CommitPositionProperty(nameof(CreationWidth));

    partial void OnCreationHeightChanged(int value) => CommitPositionProperty(nameof(CreationHeight));

    private void CommitPositionProperty(string propertyName)
    {
        _ = propertyName;
        if (!HasScreenRect)
        {
            return;
        }

        CommitUnlessSuppressed(CommitPosition);
    }

    partial void OnTextChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(() => CommitQuoted(WndConstants.PropertyKeys.Text, Text));
    }

    partial void OnTooltipTextChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(() => CommitQuoted(WndConstants.PropertyKeys.TooltipText, TooltipText));
    }

    partial void OnTooltipDelayChanged(int value)
    {
        _ = value;
        CommitUnlessSuppressed(() => _commitEdit(WndConstants.PropertyKeys.TooltipDelay, TooltipDelay.ToString(CultureInfo.InvariantCulture)));
    }

    partial void OnFontNameChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(CommitFont);
    }

    partial void OnFontSizeChanged(int value)
    {
        _ = value;
        CommitUnlessSuppressed(CommitFont);
    }

    partial void OnFontBoldChanged(bool value)
    {
        _ = value;
        CommitUnlessSuppressed(CommitFont);
    }

    partial void OnSystemCallbackChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(() => CommitQuoted(WndConstants.PropertyKeys.SystemCallback, SystemCallback));
    }

    partial void OnInputCallbackChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(() => CommitQuoted(WndConstants.PropertyKeys.InputCallback, InputCallback));
    }

    partial void OnTooltipCallbackChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(() => CommitQuoted(WndConstants.PropertyKeys.TooltipCallback, TooltipCallback));
    }

    partial void OnDrawCallbackChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(() => CommitQuoted(WndConstants.PropertyKeys.DrawCallback, DrawCallback));
    }

    partial void OnHeaderTemplateChanged(string value)
    {
        _ = value;
        CommitUnlessSuppressed(() => CommitQuoted(WndConstants.PropertyKeys.HeaderTemplate, HeaderTemplate));
    }

    partial void OnImageOffsetXChanged(int value) => CommitImageOffsetProperty(nameof(ImageOffsetX));

    partial void OnImageOffsetYChanged(int value) => CommitImageOffsetProperty(nameof(ImageOffsetY));

    private void CommitImageOffsetProperty(string propertyName)
    {
        _ = propertyName;
        CommitUnlessSuppressed(CommitImageOffset);
    }

    partial void OnStaticCenteredChanged(bool value)
    {
        _ = value;
        CommitUnlessSuppressed(CommitStaticTextData);
    }

    partial void OnEntryMaxLenChanged(int value) => CommitTextEntryProperty(nameof(EntryMaxLen));

    partial void OnEntrySecretTextChanged(bool value) => CommitTextEntryProperty(nameof(EntrySecretText));

    partial void OnEntryNumericalOnlyChanged(bool value) => CommitTextEntryProperty(nameof(EntryNumericalOnly));

    partial void OnEntryAlphaNumericalOnlyChanged(bool value) => CommitTextEntryProperty(nameof(EntryAlphaNumericalOnly));

    partial void OnEntryAsciiOnlyChanged(bool value) => CommitTextEntryProperty(nameof(EntryAsciiOnly));

    private void CommitTextEntryProperty(string propertyName)
    {
        _ = propertyName;
        CommitUnlessSuppressed(CommitTextEntryData);
    }

    partial void OnSliderMinValueChanged(int value) => CommitSliderProperty(nameof(SliderMinValue));

    partial void OnSliderMaxValueChanged(int value) => CommitSliderProperty(nameof(SliderMaxValue));

    private void CommitSliderProperty(string propertyName)
    {
        _ = propertyName;
        CommitUnlessSuppressed(CommitSliderData);
    }

    partial void OnListLengthChanged(int value) => CommitListboxProperty(nameof(ListLength));

    partial void OnListAutoScrollChanged(bool value) => CommitListboxProperty(nameof(ListAutoScroll));

    partial void OnListHasScrollIfAtEndChanged(bool value) => CommitListboxProperty(nameof(ListHasScrollIfAtEnd));

    partial void OnListScrollIfAtEndChanged(bool value) => CommitListboxProperty(nameof(ListScrollIfAtEnd));

    partial void OnListAutoPurgeChanged(bool value) => CommitListboxProperty(nameof(ListAutoPurge));

    partial void OnListScrollBarChanged(bool value) => CommitListboxProperty(nameof(ListScrollBar));

    partial void OnListMultiSelectChanged(bool value) => CommitListboxProperty(nameof(ListMultiSelect));

    partial void OnListColumnsChanged(int value) => CommitListboxProperty(nameof(ListColumns));

    partial void OnListColumnWidthsChanged(string value) => CommitListboxProperty(nameof(ListColumnWidths));

    partial void OnListForceSelectChanged(bool value) => CommitListboxProperty(nameof(ListForceSelect));

    private void CommitListboxProperty(string propertyName)
    {
        _ = propertyName;
        CommitUnlessSuppressed(CommitListboxData);
    }

    partial void OnComboIsEditableChanged(bool value) => CommitComboBoxProperty(nameof(ComboIsEditable));

    partial void OnComboMaxCharsChanged(int value) => CommitComboBoxProperty(nameof(ComboMaxChars));

    partial void OnComboMaxDisplayChanged(int value) => CommitComboBoxProperty(nameof(ComboMaxDisplay));

    partial void OnComboAsciiOnlyChanged(bool value) => CommitComboBoxProperty(nameof(ComboAsciiOnly));

    partial void OnComboLettersAndNumbersOnlyChanged(bool value) => CommitComboBoxProperty(nameof(ComboLettersAndNumbersOnly));

    private void CommitComboBoxProperty(string propertyName)
    {
        _ = propertyName;
        CommitUnlessSuppressed(CommitComboBoxData);
    }

    partial void OnRadioGroupChanged(int value)
    {
        _ = value;
        CommitUnlessSuppressed(CommitRadioButtonData);
    }

    partial void OnTabOrientationChanged(int value) => CommitTabControlProperty(nameof(TabOrientation));

    partial void OnTabEdgeChanged(int value) => CommitTabControlProperty(nameof(TabEdge));

    partial void OnTabWidthChanged(int value) => CommitTabControlProperty(nameof(TabWidth));

    partial void OnTabHeightChanged(int value) => CommitTabControlProperty(nameof(TabHeight));

    partial void OnTabCountChanged(int value) => CommitTabControlProperty(nameof(TabCount));

    partial void OnTabPaneBorderChanged(int value) => CommitTabControlProperty(nameof(TabPaneBorder));

    partial void OnTabPaneDisabledChanged(string value) => CommitTabControlProperty(nameof(TabPaneDisabled));

    private void CommitTabControlProperty(string propertyName)
    {
        _ = propertyName;
        CommitUnlessSuppressed(CommitTabControlData);
    }

    private void CommitUnlessSuppressed(Action commit)
    {
        if (!_suppressCommit)
        {
            commit();
        }
    }

    private void CommitWindowType()
    {
        _commitEdit(WndConstants.PropertyKeys.WindowType, SelectedWindowType);
    }

    private void CommitShortName()
    {
        var current = WndDecoratedName.Parse(Window.GetProperty(WndConstants.PropertyKeys.Name));
        _commitEdit(WndConstants.PropertyKeys.Name, new WndDecoratedName(current.FileName, ShortName.Trim()).ToString());
    }

    private void CommitPosition()
    {
        _commitEdit(
            WndConstants.PropertyKeys.ScreenRect,
            new WndScreenRect(UpperLeftX, UpperLeftY, BottomRightX, BottomRightY, CreationWidth, CreationHeight).ToString());
    }

    private void CommitStatus(string flag, bool isSet)
    {
        var value = WndStatusValue.ParseStatus(Window.GetProperty(WndConstants.PropertyKeys.Status));
        _commitEdit(WndConstants.PropertyKeys.Status, FormatToggled(value, WndConstants.StatusFlags.All, flag, isSet));
    }

    private void CommitStyle(string flag, bool isSet)
    {
        var value = WndStatusValue.ParseStyle(Window.GetProperty(WndConstants.PropertyKeys.Style));
        _commitEdit(WndConstants.PropertyKeys.Style, FormatToggled(value, WndConstants.StyleTypes.All, flag, isSet));
    }

    private void CommitQuoted(string key, string value)
    {
        _commitEdit(key, FormatQuoted(value));
    }

    private void CommitFont()
    {
        _commitEdit(WndConstants.PropertyKeys.Font, new WndFontValue(FontName.Trim(), FontSize, FontBold).ToString());
    }

    private void CommitTextColor()
    {
        if (EnabledTextColor == null
            || EnabledTextBorderColor == null
            || DisabledTextColor == null
            || DisabledTextBorderColor == null
            || HiliteTextColor == null
            || HiliteTextBorderColor == null)
        {
            return;
        }

        _commitEdit(
            WndConstants.PropertyKeys.TextColor,
            new WndTextColorValue(
                EnabledTextColor.Current,
                EnabledTextBorderColor.Current,
                DisabledTextColor.Current,
                DisabledTextBorderColor.Current,
                HiliteTextColor.Current,
                HiliteTextBorderColor.Current).ToString());
    }

    private void CommitImageOffset()
    {
        _commitEdit(WndConstants.PropertyKeys.ImageOffset, new WndImageOffset(ImageOffsetX, ImageOffsetY).ToString());
    }

    private void CommitDrawData(string key)
    {
        var collection = key switch
        {
            WndConstants.PropertyKeys.EnabledDrawData => EnabledDrawData,
            WndConstants.PropertyKeys.DisabledDrawData => DisabledDrawData,
            WndConstants.PropertyKeys.HiliteDrawData => HiliteDrawData,
            _ => null,
        };
        if (collection == null)
        {
            return;
        }

        _commitEdit(key, new WndDrawDataSet(collection.Select(entry => entry.Current).ToList()).ToString());
    }

    private void CommitStaticTextData()
    {
        _commitEdit(WndConstants.PropertyKeys.StaticTextData, new WndStaticTextData(StaticCentered).ToString());
    }

    private void CommitTextEntryData()
    {
        _commitEdit(
            WndConstants.PropertyKeys.TextEntryData,
            new WndTextEntryData(EntryMaxLen, EntrySecretText, EntryNumericalOnly, EntryAlphaNumericalOnly, EntryAsciiOnly).ToString());
    }

    private void CommitSliderData()
    {
        _commitEdit(WndConstants.PropertyKeys.SliderData, new WndSliderData(SliderMinValue, SliderMaxValue).ToString());
    }

    private void CommitListboxData()
    {
        if (!TryParseIntList(ListColumnWidths, out var widths))
        {
            WarnInvalidControlData();
            return;
        }

        var normalized = NormalizeColumnWidths(widths, ListColumns);
        _commitEdit(
            WndConstants.PropertyKeys.ListboxData,
            new WndListboxData(
                ListLength,
                ListAutoScroll,
                ListHasScrollIfAtEnd ? ListScrollIfAtEnd : null,
                ListAutoPurge,
                ListScrollBar,
                ListMultiSelect,
                ListColumns,
                normalized,
                ListForceSelect).ToString());
    }

    private void CommitComboBoxData()
    {
        _commitEdit(
            WndConstants.PropertyKeys.ComboBoxData,
            new WndComboBoxData(ComboIsEditable, ComboMaxChars, ComboMaxDisplay, ComboAsciiOnly, ComboLettersAndNumbersOnly).ToString());
    }

    private void CommitRadioButtonData()
    {
        _commitEdit(WndConstants.PropertyKeys.RadioButtonData, new WndRadioButtonData(RadioGroup).ToString());
    }

    private void CommitTabControlData()
    {
        if (!TryParseBoolList(TabPaneDisabled, out var disabled))
        {
            WarnInvalidControlData();
            return;
        }

        _commitEdit(
            WndConstants.PropertyKeys.TabControlData,
            new WndTabControlData(TabOrientation, TabEdge, TabWidth, TabHeight, TabCount, TabPaneBorder, disabled).ToString());
    }

    private static List<int> NormalizeColumnWidths(List<int> widths, int columns)
    {
        if (columns <= 1)
        {
            return [];
        }

        var normalized = widths.Take(columns).ToList();
        while (normalized.Count < columns)
        {
            normalized.Add(0);
        }

        return normalized;
    }

    private void WarnInvalidControlData()
    {
        _notificationService.ShowWarning(
            _localizationService.GetString("Tools.WndEditor.ControlData.InvalidTitle"),
            _localizationService.GetString("Tools.WndEditor.ControlData.InvalidMessage"),
            NotificationDurations.Medium);
        RefreshFromWindow();
    }
}
