using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Tools.IniEditor;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// Editable row wrapping a single INI field with write-through to the owning field list,
/// which is either a block field list or the document file-scope settings.
/// </summary>
public sealed partial class IniFieldRowViewModel : ObservableObject
{
    private readonly IList<IniField> _fields;
    private readonly int _fieldIndex;
    private readonly Action _onChanged;
    private readonly Action<string, string> _onEditCommitted;
    private bool _isSyncingPair;

    [ObservableProperty]
    private string _selectedFlagToAdd = string.Empty;

    [ObservableProperty]
    private IImage? _textureThumbnail;

    [ObservableProperty]
    private string _value;

    [ObservableProperty]
    private string _pairTarget = string.Empty;

    [ObservableProperty]
    private string _pairPercent = string.Empty;

    [ObservableProperty]
    private bool _isSuggestionsOpen;

    [ObservableProperty]
    private bool _isPairSuggestionsOpen;

    /// <summary>
    /// Initializes a new instance of the <see cref="IniFieldRowViewModel"/> class.
    /// </summary>
    /// <param name="fields">The owning field list.</param>
    /// <param name="fieldIndex">Index of the field within the list.</param>
    /// <param name="metadata">Display and schema metadata for the field row.</param>
    /// <param name="onChanged">Callback invoked when the value changes.</param>
    /// <param name="onEditCommitted">Callback invoked with the pre-edit and current value for undo tracking.</param>
    public IniFieldRowViewModel(
        IList<IniField> fields,
        int fieldIndex,
        IniFieldMetadata metadata,
        Action onChanged,
        Action<string, string> onEditCommitted)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        _fields = fields;
        _fieldIndex = fieldIndex;
        Description = metadata.Description;
        IsKnown = metadata.IsKnown;
        Tooltip = metadata.Tooltip ?? metadata.Description;
        Suggestions = metadata.Suggestions;
        ReferenceBlockType = metadata.ReferenceBlockType;
        IsTexture = metadata.IsTexture;
        PairTargetSuggestions = metadata.PairTargets;
        _onChanged = onChanged;
        _onEditCommitted = onEditCommitted;
        _value = fields[fieldIndex].Value;
        if (IsPercentPair)
        {
            SplitPercentPair(_value, out var target, out var percent);
            _pairTarget = target;
            _pairPercent = percent;
        }
    }

    /// <summary>
    /// Gets the index of the field within the owning list.
    /// </summary>
    public int FieldIndex => _fieldIndex;

    /// <summary>
    /// Gets the owning field list, used to route row actions such as delete.
    /// </summary>
    public IList<IniField> OwnerFields => _fields;

    /// <summary>
    /// Gets the field key.
    /// </summary>
    public string Key => _fields[_fieldIndex].Key;

    /// <summary>
    /// Gets the schema description, when known.
    /// </summary>
    public string? Description { get; }

    /// <summary>
    /// Gets a value indicating whether the field is covered by the schema.
    /// </summary>
    public bool IsKnown { get; }

    /// <summary>
    /// Gets the full tooltip text for the row.
    /// </summary>
    public string? Tooltip { get; }

    /// <summary>
    /// Gets searchable value suggestions, when any.
    /// </summary>
    public IReadOnlyList<string>? Suggestions { get; }

    /// <summary>
    /// Gets a value indicating whether the row offers a searchable value dropdown.
    /// </summary>
    public bool HasSuggestions => Suggestions?.Count > 0;

    /// <summary>
    /// Gets a value indicating whether the row renders a searchable dropdown editor.
    /// Reference, texture, and option-backed fields keep the dropdown even while their
    /// suggestion lists are still loading so the picker affordance never disappears.
    /// </summary>
    public bool HasValuePicker => HasSuggestions || CanGoToDefinition || IsTexture;

    /// <summary>
    /// Gets the icon kind for the value picker button.
    /// </summary>
    public string FieldIconKind
    {
        get
        {
            if (ReferenceBlockType != null)
            {
                return IniBlockIconHelper.GetIconKind(ReferenceBlockType);
            }

            return IsTexture ? "ImageOutline" : "ChevronDown";
        }
    }

    /// <summary>
    /// Gets the icon kind for the pair target picker button.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public string PairIconKind => IniBlockIconHelper.GetIconKind(ReferenceBlockType ?? IniConstants.BlockTypes.Object);

    /// <summary>
    /// Gets the referenced block type for go-to-definition, when any.
    /// </summary>
    public string? ReferenceBlockType { get; }

    /// <summary>
    /// Gets a value indicating whether go-to-definition is available.
    /// </summary>
    public bool CanGoToDefinition => ReferenceBlockType != null;

    /// <summary>
    /// Gets a value indicating whether the value names a mapped image texture.
    /// </summary>
    public bool IsTexture { get; }

    /// <summary>
    /// Gets a value indicating whether this field is KindOf flags.
    /// </summary>
    public bool IsKindOf => string.Equals(Key, IniConstants.FieldKeys.KindOf, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether this field holds a target plus percent pair
    /// such as <c>AmericaCommandCenter -80%</c>.
    /// </summary>
    public bool IsPercentPair => string.Equals(Key, IniConstants.FieldKeys.ProductionTimeChange, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Gets a value indicating whether the row uses the plain single value editor.
    /// </summary>
    public bool IsPlainValue => !IsKindOf && !IsPercentPair;

    /// <summary>
    /// Gets known target names offered by the pair target dropdown.
    /// </summary>
    public IReadOnlyList<string>? PairTargetSuggestions { get; }

    /// <summary>
    /// Gets a value indicating whether the pair target dropdown has entries.
    /// </summary>
    public bool HasPairTargets => PairTargetSuggestions?.Count > 0;

    /// <summary>
    /// Gets available KindOf flags.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Bound from XAML; instance member required for binding.")]
    public IReadOnlyList<string> AvailableKindOfFlags => IniConstants.KindOfFlags.All;

    /// <summary>
    /// Gets the active flags split from the space- or tab-separated value.
    /// </summary>
    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Reads source-generated value instance state and is bound from XAML.")]
    public IReadOnlyList<string> ActiveFlags => SplitFlags(Value);

    /// <summary>
    /// Appends a flag to the KindOf value and clears the flag entry box.
    /// </summary>
    /// <param name="flag">Flag to add.</param>
    [RelayCommand]
    public void AddFlag(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
        {
            return;
        }

        var parts = SplitFlags(Value).ToList();
        var added = false;
        foreach (var token in SplitFlags(flag).Where(t => !parts.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            parts.Add(token);
            added = true;
        }

        if (added)
        {
            Value = string.Join(" ", parts);
            OnPropertyChanged(nameof(ActiveFlags));
        }

        SelectedFlagToAdd = string.Empty;
    }

    /// <summary>
    /// Toggles the searchable value dropdown open or closed.
    /// </summary>
    [RelayCommand]
    public void ToggleSuggestions()
    {
        IsSuggestionsOpen = !IsSuggestionsOpen;
    }

    /// <summary>
    /// Toggles the pair target dropdown open or closed.
    /// </summary>
    [RelayCommand]
    public void TogglePairSuggestions()
    {
        IsPairSuggestionsOpen = !IsPairSuggestionsOpen;
    }

    /// <summary>
    /// Removes a flag from the KindOf value.
    /// </summary>
    /// <param name="flag">Flag to remove.</param>
    [RelayCommand]
    public void RemoveFlag(string? flag)
    {
        if (string.IsNullOrWhiteSpace(flag))
        {
            return;
        }

        var parts = SplitFlags(Value).ToList();
        if (parts.RemoveAll(p => string.Equals(p, flag.Trim(), StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Value = string.Join(" ", parts);
            OnPropertyChanged(nameof(ActiveFlags));
        }
    }

    partial void OnSelectedFlagToAddChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var trimmed = value.Trim();
        var matched = AvailableKindOfFlags.FirstOrDefault(f => string.Equals(f, trimmed, StringComparison.OrdinalIgnoreCase));
        if (matched is not null)
        {
            AddFlag(matched);
        }
    }

    partial void OnPairTargetChanged(string value)
    {
        RecomposePercentPair();
    }

    partial void OnPairPercentChanged(string value)
    {
        RecomposePercentPair();
    }

    partial void OnValueChanged(string value)
    {
        var current = _fields[_fieldIndex];
        _fields[_fieldIndex] = current with { Value = value, IsBare = current.IsBare && value.Length == 0 };
        _onChanged();
        _onEditCommitted(current.Value, value);
        OnPropertyChanged(nameof(ActiveFlags));
        if (IsPercentPair && !_isSyncingPair)
        {
            SplitPercentPair(value, out var target, out var percent);
            _isSyncingPair = true;
            try
            {
                PairTarget = target;
                PairPercent = percent;
            }
            finally
            {
                _isSyncingPair = false;
            }
        }
    }

    private void RecomposePercentPair()
    {
        if (!IsPercentPair || _isSyncingPair)
        {
            return;
        }

        var target = PairTarget?.Trim() ?? string.Empty;
        var percent = PairPercent?.Trim().TrimEnd(IniConstants.Syntax.PercentSuffix).Trim() ?? string.Empty;
        var recomposed = (target, percent) switch
        {
            (_, "") => target,
            ("", _) => $"{percent}%",
            _ => $"{target} {percent}%",
        };
        if (!string.Equals(Value, recomposed, StringComparison.Ordinal))
        {
            _isSyncingPair = true;
            try
            {
                Value = recomposed;
            }
            finally
            {
                _isSyncingPair = false;
            }
        }
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Shared flag splitter kept as an instance helper to satisfy member ordering.")]
    private string[] SplitFlags(string? value) => (value ?? string.Empty)
        .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Shared pair splitter kept as an instance helper to satisfy member ordering.")]
    private void SplitPercentPair(string? value, out string target, out string percent)
    {
        target = string.Empty;
        percent = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var lineBreak = value.IndexOfAny(['\r', '\n']);
        var firstLine = lineBreak < 0 ? value : value[..lineBreak];
        var parts = firstLine.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        if (parts.Length == 1 || !parts[^1].EndsWith(IniConstants.Syntax.PercentSuffix))
        {
            target = firstLine.Trim();
            return;
        }

        percent = parts[^1].TrimEnd(IniConstants.Syntax.PercentSuffix).Trim();
        target = string.Join(" ", parts[..^1]);
    }
}
