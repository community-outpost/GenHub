using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Results;

/// <summary>
/// Generic result type for any “detect many items” operation.
/// </summary>
/// <typeparam name="T">The type of detected item.</typeparam>
public sealed class DetectionResult<T>
{
    private DetectionResult()
    {
        Success = false;
        Items = Array.Empty<T>();
        Errors = Array.Empty<string>();
        Elapsed = TimeSpan.Zero;
    }

    private DetectionResult(bool success, IEnumerable<T> items, IEnumerable<string> errors, TimeSpan elapsed)
    {
        Success = success;
        Items = items.ToList();
        Errors = errors.ToList();
        Elapsed = elapsed;
    }

    /// <summary>Gets a value indicating whether detection succeeded (even if 0 items).</summary>
    public bool Success { get; }

    /// <summary>Gets the items found.</summary>
    public IReadOnlyList<T> Items { get; }

    /// <summary>Gets any errors encountered.</summary>
    public IReadOnlyList<string> Errors { get; }

    /// <summary>Gets how long detection took.</summary>
    public TimeSpan Elapsed { get; }

    /// <summary>Factory for a successful result.</summary>
    /// <param name="items">The detected items.</param>
    /// <param name="elapsed">The elapsed time.</param>
    /// <returns>A successful detection result.</returns>
    public static DetectionResult<T> Succeeded(IEnumerable<T> items, TimeSpan elapsed) =>
        new DetectionResult<T>(true, items, Array.Empty<string>(), elapsed);

    /// <summary>Factory for a failed result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed detection result.</returns>
    public static DetectionResult<T> Failed(string error) =>
        new DetectionResult<T>(false, Array.Empty<T>(), new[] { error }, TimeSpan.Zero);
}
