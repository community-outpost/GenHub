using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Placeholder window node for the interactive WND Editor demo.
/// </summary>
public partial class WndDemoNode : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _controlType;

    [ObservableProperty]
    private int _x;

    [ObservableProperty]
    private int _y;

    [ObservableProperty]
    private int _width;

    [ObservableProperty]
    private int _height;

    /// <summary>
    /// Initializes a new instance of the <see cref="WndDemoNode"/> class.
    /// </summary>
    /// <param name="name">The window name.</param>
    /// <param name="controlType">The control type.</param>
    /// <param name="x">The X position.</param>
    /// <param name="y">The Y position.</param>
    /// <param name="width">The width.</param>
    /// <param name="height">The height.</param>
    public WndDemoNode(string name, string controlType, int x, int y, int width, int height)
    {
        _name = name;
        _controlType = controlType;
        _x = x;
        _y = y;
        _width = width;
        _height = height;
        Children = new ObservableCollection<WndDemoNode>();
    }

    /// <summary>
    /// Gets the child windows.
    /// </summary>
    public ObservableCollection<WndDemoNode> Children { get; }

    /// <summary>
    /// Gets the display label combining name and control type.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated observable properties Name and ControlType in partial view model")]
    public string DisplayLabel => $"{Name} : {ControlType}";
}
