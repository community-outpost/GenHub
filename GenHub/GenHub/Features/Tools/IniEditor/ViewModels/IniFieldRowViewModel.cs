using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.IniEditor;
using System;
using System.Collections.Generic;

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
    /// Gets or sets the texture thumbnail, loaded asynchronously for texture rows.
    /// </summary>
    [ObservableProperty]
    private IImage? _textureThumbnail;

    /// <summary>
    /// Gets or sets the edited value, writing through to the document on change.
    /// </summary>
    [ObservableProperty]
    private string _value;

    partial void OnValueChanged(string value)
    {
        var current = _fields[_fieldIndex];
        _fields[_fieldIndex] = current with { Value = value, IsBare = current.IsBare && value.Length == 0 };
        _onChanged();
        _onEditCommitted(current.Value, value);
    }
}
