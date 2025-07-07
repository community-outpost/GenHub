using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GenHub.Common.ViewModels;

/// <summary>
/// Shell ViewModel for the main launcher view.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// Performs asynchronous startup work.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        // Placeholder for future init logic
        await Task.CompletedTask;
    }
}
