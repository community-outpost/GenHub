namespace GenHub.Core.Models.Results;

/// <summary>
/// Base class for all result objects in GenHub, providing common success/failure semantics.
/// </summary>
public abstract class ResultBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResultBase"/> class.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="errors">Any errors that occurred.</param>
    /// <param name="elapsed">Time taken for the operation.</param>
    protected ResultBase(bool success, IEnumerable<string>? errors = null, TimeSpan elapsed = default)
    {
        Success = success;
        Errors = errors?.ToList().AsReadOnly() ?? new List<string>().AsReadOnly();
        Elapsed = elapsed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ResultBase"/> class.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="error">A single error message.</param>
    /// <param name="elapsed">Time taken for the operation.</param>
    protected ResultBase(bool success, string? error = null, TimeSpan elapsed = default)
        : this(success, error != null ? new[] { error } : null, elapsed)
    {
    }

    /// <summary>Gets a value indicating whether the operation was successful.</summary>
    public bool Success { get; }

    /// <summary>Gets any errors that occurred during the operation.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool Failed => !Success;

    /// <summary>Gets a value indicating whether there are any errors.</summary>
    public bool HasErrors => Errors.Count > 0;

    /// <summary>Gets the first error message, if any.</summary>
    public string? FirstError => Errors.FirstOrDefault();

    /// <summary>Gets all error messages as a single string.</summary>
    public string AllErrors => string.Join(Environment.NewLine, Errors);

    /// <summary>Gets the time taken for the operation.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Gets the timestamp when the operation completed.</summary>
    public DateTime CompletedAt { get; }

    /// <summary>Gets the elapsed time in milliseconds.</summary>
    public double ElapsedMilliseconds => Elapsed.TotalMilliseconds;

    /// <summary>Gets the elapsed time in seconds.</summary>
    public double ElapsedSeconds => Elapsed.TotalSeconds;
}