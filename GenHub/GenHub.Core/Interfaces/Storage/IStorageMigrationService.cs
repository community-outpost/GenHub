using System;
using System.Threading;
using System.Threading.Tasks;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Storage;

namespace GenHub.Core.Interfaces.Storage;

/// <summary>
/// Provides backend operations for validating and migrating the GenHub installation and its storage locations.
/// </summary>
public interface IStorageMigrationService
{
    /// <summary>
    /// Performs pre-flight checks before an installation migration is executed.
    /// </summary>
    /// <param name="targetPath">The destination directory where GenHub should be relocated.</param>
    /// <param name="relocateCasAndWorkspace">Whether the user also intends to relocate CAS storage and workspaces.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An operation result containing the pre-flight check outcomes.</returns>
    Task<OperationResult<StorageMigrationPreflightResult>> ValidatePreflightAsync(
        string targetPath,
        bool relocateCasAndWorkspace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the installation migration, moving data, staging updates, and relaunching the application from the new path.
    /// </summary>
    /// <param name="request">The migration configuration request.</param>
    /// <param name="progress">An optional progress reporter for tracking migration stages.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>An operation result indicating whether the migration was initiated successfully.</returns>
    Task<OperationResult<bool>> MigrateAsync(
        StorageMigrationRequest request,
        IProgress<StorageMigrationProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
