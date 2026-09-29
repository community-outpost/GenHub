using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Constants;
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

    [ObservableProperty]
    private string _selectedFlagToAdd = string.Empty;

    [ObservableProperty]
    private IImage? _textureThumbnail;

    [ObservableProperty]
    private string _value;

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
        _onChanged = onChanged;
        _onEditCommitted = onEditCommitted;
        _value = fields[fieldIndex].Value;
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
    public bool HasSuggestions => Suggestions != null && Suggestions.Count > 0;

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
    /// Appends a flag to the KindOf value.
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
        if (!parts.Contains(flag.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            parts.Add(flag.Trim());
            Value = string.Join(" ", parts);
            OnPropertyChanged(nameof(ActiveFlags));
        }
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
            SelectedFlagToAdd = string.Empty;
        }
    }

    partial void OnValueChanged(string value)
    {
        var current = _fields[_fieldIndex];
        _fields[_fieldIndex] = current with { Value = value, IsBare = current.IsBare && value.Length == 0 };
        _onChanged();
        _onEditCommitted(current.Value, value);
        OnPropertyChanged(nameof(ActiveFlags));
    }

    [SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Shared flag splitter kept as an instance helper to satisfy member ordering.")]
    private string[] SplitFlags(string? value) => (value ?? string.Empty)
        .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
