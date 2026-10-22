using GenHub.Core.Models.Results;

namespace GenHub.Core.Models.Tools.ModBuilder;

/// <summary>
/// Represents the result of a build operation.
/// </summary>
public class BuildResult : ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BuildResult"/> class.
    /// </summary>
    /// <param name="success">Whether the build was successful.</param>
    /// <param name="errors">Any errors that occurred.</param>
    /// <param name="elapsed">Time taken for the build.</param>
    public BuildResult(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
        : base(success, errors, elapsed)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BuildResult"/> class.
    /// </summary>
    /// <param name="success">Whether the build was successful.</param>
    /// <param name="error">A single error message.</param>
    /// <param name="elapsed">Time taken for the build.</param>
    public BuildResult(bool success, string? error = null, TimeSpan elapsed = default)
        : base(success, error, elapsed)
    {
    }

    /// <summary>
    /// Gets or sets the number of files processed.
    /// </summary>
    public int FilesProcessed { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were unchanged.
    /// </summary>
    public int FilesUnchanged { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were added.
    /// </summary>
    public int FilesAdded { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were changed.
    /// </summary>
    public int FilesChanged { get; set; }

    /// <summary>
    /// Gets or sets the number of files that were removed.
    /// </summary>
    public int FilesRemoved { get; set; }

    /// <summary>
    /// Gets or sets the build steps that were executed.
    /// </summary>
    public BuildStep StepsExecuted { get; set; }

    /// <summary>
    /// Gets or sets the list of bundle items that were built.
    /// </summary>
    public List<string> BuiltItems { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of bundle packs that were created.
    /// </summary>
    public List<string> CreatedPacks { get; set; } = new();

    /// <summary>
    /// Gets or sets warnings generated during the build.
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Creates a successful build result.
    /// </summary>
    /// <param name="elapsed">Time taken for the build.</param>
    /// <returns>A successful build result.</returns>
    public static BuildResult CreateSuccess(TimeSpan elapsed)
    {
        return new BuildResult(true, (IEnumerable<string>?)null, elapsed);
    }

    /// <summary>
    /// Creates a failed build result.
    /// </summary>
    /// <param name="error">The error message.</param>
    /// <param name="elapsed">Time taken for the build.</param>
    /// <returns>A failed build result.</returns>
    public static BuildResult CreateFailure(string error, TimeSpan elapsed)
    {
        return new BuildResult(false, error, elapsed);
    }

    /// <summary>
    /// Creates a failed build result with multiple errors.
    /// </summary>
    /// <param name="errors">The error messages.</param>
    /// <param name="elapsed">Time taken for the build.</param>
    /// <returns>A failed build result.</returns>
    public static BuildResult CreateFailure(IEnumerable<string> errors, TimeSpan elapsed)
    {
        return new BuildResult(false, errors, elapsed);
    }
}
