using System.Windows.Input;
using GenHub.Core.Models.Enums;

namespace GenHub.Features.Downloads.ViewModels;

/// <summary>
/// Shared contract for release/addon rows so the detail view model can resolve install state
/// and react to state changes without duplicated per-type logic.
/// </summary>
public interface IDownloadableRowViewModel
{
    /// <summary>Gets the display name of the row.</summary>
    string Name { get; }

    /// <summary>Gets the direct download URL for the row's file, if any.</summary>
    string? DownloadUrl { get; }

    /// <summary>Gets or sets a value indicating whether the row's content is already installed.</summary>
    bool IsDownloaded { get; set; }

    /// <summary>Gets or sets a value indicating whether an update is available for this row's content.</summary>
    bool IsUpdateAvailable { get; set; }

    /// <summary>Gets or sets the on-disk manifest ID produced when this row was acquired.</summary>
    string? DownloadedManifestId { get; set; }

    /// <summary>Gets or sets a value indicating whether this row is currently selected as the active target.</summary>
    bool IsSelected { get; set; }

    /// <summary>Gets or sets the content type of this row.</summary>
    ContentType ContentType { get; set; }

    /// <summary>Gets or sets the command to select this row as the active download/profile target.</summary>
    ICommand? SelectCommand { get; set; }
}
