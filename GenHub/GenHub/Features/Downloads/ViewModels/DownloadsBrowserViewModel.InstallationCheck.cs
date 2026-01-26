using System.Threading.Tasks;

namespace GenHub.Features.Downloads.ViewModels;

/// <content>
/// Partial class for DownloadsBrowserViewModel containing installation check logic.
/// </content>
public partial class DownloadsBrowserViewModel
{
    /// <summary>
    /// Placeholder for performing an installation check for the downloads feature; currently disabled during refactoring.
    /// </summary>
    /// <returns>A completed <see cref="Task"/>; no installation check is performed.</returns>
    private static Task CheckIfInstalledAsync()
    {
        // Feature temporarily disabled during refactoring
        return Task.CompletedTask;
    }
}