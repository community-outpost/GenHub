using System.Threading.Tasks;

namespace GenHub.Features.Downloads.ViewModels;

/// <content>
/// Partial class for DownloadsBrowserViewModel containing installation check logic.
/// </content>
public partial class DownloadsBrowserViewModel
{
    private static Task CheckIfInstalledAsync()
    {
        // Feature temporarily disabled during refactoring
        return Task.CompletedTask;
    }
}