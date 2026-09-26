using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Placeholder command slot for the interactive Hotkey Editor demo.
/// </summary>
public partial class HotkeyDemoSlot : ObservableObject
{
    [ObservableProperty]
    private string _actionName;

    [ObservableProperty]
    private string _hotkey;

    [ObservableProperty]
    private bool _hasConflict;

    /// <summary>
    /// Initializes a new instance of the <see cref="HotkeyDemoSlot"/> class.
    /// </summary>
    /// <param name="actionName">The command action name.</param>
    /// <param name="hotkey">The assigned hotkey.</param>
    public HotkeyDemoSlot(string actionName, string hotkey)
    {
        _actionName = actionName;
        _hotkey = hotkey;
    }

    /// <summary>
    /// Gets the display label combining action name and hotkey.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325:Methods and properties that don't access instance data should be static", Justification = "Accesses generated observable properties ActionName and Hotkey in partial view model")]
    public string DisplayLabel => string.IsNullOrEmpty(Hotkey) ? ActionName : $"{ActionName} [{Hotkey}]";
}
