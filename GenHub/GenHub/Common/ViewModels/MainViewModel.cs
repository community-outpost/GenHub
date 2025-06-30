using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Shell ViewModel for the main launcher view.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    [ObservableProperty]
    private int _selectedTabIndex;

    /// <summary>
    /// Gets the tab index for Game Profiles.
    /// </summary>
    public int TabIndex0 => 0;

    /// <summary>
    /// Gets the tab index for Downloads.
    /// </summary>
    public int TabIndex1 => 1;

    /// <summary>
    /// Gets the tab index for Settings.
    /// </summary>
    public int TabIndex2 => 2;

    /// <summary>
    /// Performs asynchronous startup work.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        // Placeholder for future init logic
        await Task.CompletedTask;
    }

    /// <summary>Switches the active tab.</summary>
    [RelayCommand]
    private void SelectTab(int tabIndex) =>
        SelectedTabIndex = tabIndex;
}
