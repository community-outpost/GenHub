using System.Collections.Generic;
using System.Text.Json.Serialization;
using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Individual installation step with typed operation kind and structured parameters.
/// </summary>
public class InstallationStep
{
    /// <summary>
    /// Gets or sets the step name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the kind of installation operation to execute.
    /// </summary>
    public InstallationStepKind Kind { get; set; } = InstallationStepKind.Unknown;

    /// <summary>
    /// Gets or sets the relative path of the target file to act upon in the delivered workspace or manifest.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TargetRelativePath { get; set; }

    /// <summary>
    /// Gets or sets the destination relative path when renaming or moving a file.
    /// Only used when <see cref="Kind"/> is <see cref="InstallationStepKind.RenameFile"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DestinationRelativePath { get; set; }

    /// <summary>
    /// Gets or sets the arguments for executable steps.
    /// Only used when <see cref="Kind"/> is <see cref="InstallationStepKind.RunVerifiedInstaller"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Arguments { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the step requires elevation.
    /// </summary>
    public bool RequiresElevation { get; set; }

    /// <summary>
    /// Gets or sets an optional user-facing status message to display in notifications or progress.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StatusMessage { get; set; }

    /// <summary>
    /// Gets or sets an optional unique key identifying this installation step for execution tracking across updates.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? StepKey { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this step should only run once and be skipped on subsequent updates if already executed.
    /// </summary>
    public bool RunOnce { get; set; }
}