using GenHub.Core.Models.Providers;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// ViewModel for a release item in the Releases section.
/// Inherits common downloadable item and expandable row behavior from <see cref="DownloadableItemViewModel"/>.
/// </summary>
public partial class ReleaseItemViewModel : DownloadableItemViewModel
{
    /// <summary>
    /// Gets or sets the underlying content release from the publisher catalog, if any.
    /// </summary>
    public ContentRelease? Release { get; set; }
}
