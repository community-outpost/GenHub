namespace GenHub.Core.Models.Results;

/// <summary>Generic result type for any “detect many items” operation.</summary>
/// <typeparam name="T">The type of detected item.</typeparam>
public sealed class DetectionResult<T>(
    bool success,
    IEnumerable<T> items,
    IEnumerable<string> errors,
    TimeSpan elapsed)
    : ResultBase(success, errors, elapsed)
{
    /// <summary>Gets the items found.</summary>
    public IReadOnlyList<T> Items { get; } = items?.ToList() ?? new List<T>();

    /// <summary>Factory for a successful result.</summary>
    /// <param name="items">The detected items.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful detection result.</returns>
    public static DetectionResult<T> CreateSuccess(IEnumerable<T> items, TimeSpan elapsed) =>
        new DetectionResult<T>(true, items, Array.Empty<string>(), elapsed);

    /// <summary>Factory for a failed result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed detection result.</returns>
    public static DetectionResult<T> CreateFailure(string error) =>
        CreateFailure(new[] { error });

    /// <summary>Factory for a failed result with multiple errors.</summary>
    /// <param name="errors">The error messages.</param>
    /// <returns>A failed detection result.</returns>
    public static DetectionResult<T> CreateFailure(IEnumerable<string> errors) =>
        new DetectionResult<T>(false, Array.Empty<T>(), errors, TimeSpan.Zero);
}
