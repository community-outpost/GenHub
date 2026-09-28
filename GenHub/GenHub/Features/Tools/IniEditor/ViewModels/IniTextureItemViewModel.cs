using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Tools.IniEditor.ViewModels;

/// <summary>
/// Represents a mapped image texture item in the 2-column texture picker list matching WND Editor.
/// </summary>
public sealed partial class IniTextureItemViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the mapped image name.
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// Gets or sets the cached thumbnail bitmap.
    /// </summary>
    [ObservableProperty]
    private Bitmap? _thumbnail;

    /// <summary>
    /// Gets or sets the tooltip containing resolution and source file details.
    /// </summary>
    [ObservableProperty]
    private string? _tooltip;
}
