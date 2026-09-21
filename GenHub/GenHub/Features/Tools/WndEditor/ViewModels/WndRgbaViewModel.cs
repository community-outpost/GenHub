using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.WndEditor;
using System;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// An editable red/green/blue/alpha color.
/// </summary>
public sealed partial class WndRgbaViewModel : ObservableObject
{
    private readonly Action _commit;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndRgbaViewModel"/> class.
    /// </summary>
    /// <param name="color">The initial color.</param>
    /// <param name="commit">Callback invoked when the user commits a channel change.</param>
    public WndRgbaViewModel(WndRgbaColor color, Action commit)
    {
        _red = color.Red;
        _green = color.Green;
        _blue = color.Blue;
        _alpha = color.Alpha;
        _commit = commit;
    }

    /// <summary>
    /// Gets or sets the red channel.
    /// </summary>
    [ObservableProperty]
    private int _red;

    /// <summary>
    /// Gets or sets the green channel.
    /// </summary>
    [ObservableProperty]
    private int _green;

    /// <summary>
    /// Gets or sets the blue channel.
    /// </summary>
    [ObservableProperty]
    private int _blue;

    /// <summary>
    /// Gets or sets the alpha channel.
    /// </summary>
    [ObservableProperty]
    private int _alpha;

    /// <summary>
    /// Gets the current color value.
    /// </summary>
    public WndRgbaColor Current => new(Red, Green, Blue, Alpha);

    partial void OnRedChanged(int value)
    {
        _ = value;
        CommitChannel();
    }

    partial void OnGreenChanged(int value)
    {
        _ = value;
        CommitChannel();
    }

    partial void OnBlueChanged(int value)
    {
        _ = value;
        CommitChannel();
    }

    partial void OnAlphaChanged(int value)
    {
        _ = value;
        CommitChannel();
    }

    private void CommitChannel()
    {
        OnPropertyChanged(nameof(Current));
        _commit();
    }
}
