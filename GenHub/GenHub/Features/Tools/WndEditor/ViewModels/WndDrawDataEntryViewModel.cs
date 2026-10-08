using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.WndEditor;
using System;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// An editable draw data entry row.
/// </summary>
public sealed partial class WndDrawDataEntryViewModel : ObservableObject
{
    private readonly Action _commit;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndDrawDataEntryViewModel"/> class.
    /// </summary>
    /// <param name="index">The zero-based entry index.</param>
    /// <param name="entry">The initial entry.</param>
    /// <param name="commit">Callback invoked when the user commits a change.</param>
    public WndDrawDataEntryViewModel(int index, WndDrawDataEntry entry, Action commit)
    {
        Index = index;
        _image = entry.Image;
        Color = new WndRgbaViewModel(entry.Color, commit);
        BorderColor = new WndRgbaViewModel(entry.BorderColor, commit);
        _commit = commit;
    }

    /// <summary>
    /// Gets the zero-based entry index.
    /// </summary>
    public int Index { get; }

    /// <summary>
    /// Gets the one-based display number.
    /// </summary>
    public int DisplayNumber => Index + 1;

    /// <summary>
    /// Gets the tint color editor.
    /// </summary>
    public WndRgbaViewModel Color { get; }

    /// <summary>
    /// Gets the border color editor.
    /// </summary>
    public WndRgbaViewModel BorderColor { get; }

    /// <summary>
    /// Gets or sets the mapped image name.
    /// </summary>
    [ObservableProperty]
    private string _image;

    /// <summary>
    /// Gets the current entry value.
    /// </summary>
    public WndDrawDataEntry Current => new(Image.Trim(), Color.Current, BorderColor.Current);

    partial void OnImageChanged(string value)
    {
        _commit();
    }
}
