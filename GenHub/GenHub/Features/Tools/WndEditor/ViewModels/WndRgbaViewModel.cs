using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using GenHub.Core.Models.Tools.WndEditor;
using System;
using System.Diagnostics.CodeAnalysis;

namespace GenHub.Features.Tools.WndEditor.ViewModels;

/// <summary>
/// An editable red/green/blue/alpha color.
/// </summary>
public sealed partial class WndRgbaViewModel : ObservableObject
{
    private readonly Action _commit;
    private bool _editing;
    private bool _suppressChannelNotify;
    private WndRgbaColor _editStart;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndRgbaViewModel"/> class.
    /// </summary>
    /// <param name="color">The initial color.</param>
    /// <param name="commit">Callback invoked when the user commits a channel change.</param>
    public WndRgbaViewModel(WndRgbaColor color, Action commit)
    {
        _red = ClampChannel(color.Red);
        _green = ClampChannel(color.Green);
        _blue = ClampChannel(color.Blue);
        _alpha = ClampChannel(color.Alpha);
        _editStart = Current;
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
    [SuppressMessage("Major Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Instance property bound to RGBA color components.")]
    public WndRgbaColor Current => new(Red, Green, Blue, Alpha);

    /// <summary>
    /// Gets or sets the color as an Avalonia color for picker binding.
    /// </summary>
    public Color SelectedColor
    {
        get => Color.FromArgb(ToByte(Alpha), ToByte(Red), ToByte(Green), ToByte(Blue));
        set
        {
            if (SelectedColor == value)
            {
                return;
            }

            SetChannels(value.R, value.G, value.B, value.A);
        }
    }

    /// <summary>
    /// Gets the hexadecimal representation in RGBA channel order.
    /// </summary>
    public string HexValue => $"#{Red:X2}{Green:X2}{Blue:X2}{Alpha:X2}";

    /// <summary>
    /// Gets a brush previewing the current color.
    /// </summary>
    public SolidColorBrush SwatchBrush => new(SelectedColor);

    /// <summary>
    /// Starts a picker session, deferring commits until <see cref="EndColorEdit"/> so a
    /// drag across the spectrum records a single undoable edit.
    /// </summary>
    public void BeginColorEdit()
    {
        _editStart = Current;
        _editing = true;
    }

    /// <summary>
    /// Ends a picker session, committing once when the color changed.
    /// </summary>
    public void EndColorEdit()
    {
        if (!_editing)
        {
            return;
        }

        _editing = false;
        if (!Current.Equals(_editStart))
        {
            _commit();
        }
    }

    partial void OnRedChanged(int value)
    {
        _ = value;
        NotifyChannelChanged();
    }

    partial void OnGreenChanged(int value)
    {
        _ = value;
        NotifyChannelChanged();
    }

    partial void OnBlueChanged(int value)
    {
        _ = value;
        NotifyChannelChanged();
    }

    partial void OnAlphaChanged(int value)
    {
        _ = value;
        NotifyChannelChanged();
    }

    private static int ClampChannel(int channel)
    {
        return Math.Clamp(channel, 0, 255);
    }

    private static byte ToByte(int channel)
    {
        return (byte)ClampChannel(channel);
    }

    private void SetChannels(int red, int green, int blue, int alpha)
    {
        _suppressChannelNotify = true;
        Red = ClampChannel(red);
        Green = ClampChannel(green);
        Blue = ClampChannel(blue);
        Alpha = ClampChannel(alpha);
        _suppressChannelNotify = false;
        NotifyChannelsChanged();
    }

    private void NotifyChannelChanged()
    {
        if (_suppressChannelNotify)
        {
            return;
        }

        NotifyChannelsChanged();
    }

    private void NotifyChannelsChanged()
    {
        OnPropertyChanged(nameof(Current));
        OnPropertyChanged(nameof(SelectedColor));
        OnPropertyChanged(nameof(HexValue));
        OnPropertyChanged(nameof(SwatchBrush));
        if (!_editing)
        {
            _commit();
        }
    }
}
