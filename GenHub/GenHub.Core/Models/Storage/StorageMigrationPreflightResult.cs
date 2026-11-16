using System.Collections.Generic;

namespace GenHub.Core.Models.Storage;

/// <summary>
/// Represents the outcome of pre-flight validation checks before an installation migration is executed.
/// </summary>
public class StorageMigrationPreflightResult
{
    /// <summary>
    /// Gets or sets a value indicating whether all pre-flight checks passed and migration is safe to proceed.
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// Gets or sets the estimated disk space required for the migration in bytes.
    /// </summary>
    public long RequiredBytes { get; set; }

    /// <summary>
    /// Gets or sets the available free disk space on the target volume in bytes.
    /// </summary>
    public long AvailableBytes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the target drive has sufficient free space for the migration.
    /// </summary>
    public bool HasSufficientSpace { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether GenHub has write permissions in the target location.
    /// </summary>
    public bool HasWritePermission { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether active game instances or locking processes are currently running.
    /// </summary>
    public bool HasActiveProcesses { get; set; }

    /// <summary>
    /// Gets or sets the list of active process names or launch descriptions detected during pre-flight.
    /// </summary>
    public IReadOnlyList<string> ActiveProcessNames { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether the selected target path is inside the current application directory.
    /// </summary>
    public bool IsTargetInsideApplicationDirectory { get; set; }

    /// <summary>
    /// Gets or sets the detailed error or warning message if pre-flight checks failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
