using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace GenHub.Features.Tools.RmlEditor.ViewModels;

/// <summary>
/// A style sheet rule row shown in the styles tab.
/// </summary>
public sealed partial class RmlStyleRuleViewModel : ObservableObject
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RmlStyleRuleViewModel"/> class.
    /// </summary>
    /// <param name="sheetIndex">The index of the owning style sheet.</param>
    /// <param name="ruleIndex">The index of the rule within its sheet.</param>
    /// <param name="selectors">The rule selector text.</param>
    /// <param name="detail">The detail text describing the rule origin.</param>
    /// <param name="isEditable">Whether the rule can be edited.</param>
    public RmlStyleRuleViewModel(int sheetIndex, int ruleIndex, string selectors, string detail, bool isEditable)
    {
        SheetIndex = sheetIndex;
        RuleIndex = ruleIndex;
        _selectors = selectors;
        Detail = detail;
        IsEditable = isEditable;
    }

    /// <summary>
    /// Gets the index of the owning style sheet.
    /// </summary>
    public int SheetIndex { get; }

    /// <summary>
    /// Gets the index of the rule within its sheet.
    /// </summary>
    public int RuleIndex { get; }

    /// <summary>
    /// Gets or sets the rule selector text.
    /// </summary>
    [ObservableProperty]
    private string _selectors;

    /// <summary>
    /// Gets the detail text describing the rule origin.
    /// </summary>
    public string Detail { get; }

    /// <summary>
    /// Gets a value indicating whether the rule can be edited.
    /// </summary>
    public bool IsEditable { get; }

    /// <summary>
    /// Raised when the selector text is committed.
    /// </summary>
    public event Action<RmlStyleRuleViewModel>? SelectorsCommitted;

    partial void OnSelectorsChanged(string value)
    {
        SelectorsCommitted?.Invoke(this);
    }
}
