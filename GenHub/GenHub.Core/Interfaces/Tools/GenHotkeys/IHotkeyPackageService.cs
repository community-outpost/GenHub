using GenHub.Core.Models.Manifest;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.GenHotkeys;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.GenHotkeys;

/// <summary>
/// Service for building a standalone .big archive containing customized CSF
/// and icon TGAs, and registering it as a GenHub ContentManifest Addon.
/// </summary>
public interface IHotkeyPackageService
{
    /// <summary>
    /// Generates a .big archive from the given hotkey profile and registers or updates it as a local ContentManifest Addon.
    /// </summary>
    /// <param name="profile">The hotkey profile to export.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="existingManifestId">Optional existing manifest ID to update instead of creating a new one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the created or updated ContentManifest Addon.</returns>
    Task<OperationResult<ContentManifest>> CreateHotkeysAddonAsync(
        HotkeyProfile profile,
        IProgress<string>? progress = null,
        string? existingManifestId = null,
        CancellationToken cancellationToken = default);
}
