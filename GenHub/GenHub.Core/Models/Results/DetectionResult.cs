using System;
using System.Collections.Generic;
using System.Linq;

namespace GenHub.Core.Models.Results
{
    /// <summary>
    /// Generic result type for any “detect many items” operation.
    /// </summary>
    /// <typeparam name="T">The type of detected item.</typeparam>
    public sealed class DetectionResult<T>
    {
        /// <summary>Gets or sets a value indicating whether detection succeeded (even if 0 items).</summary>
        public bool Success { get; set; }

        /// <summary>Gets or sets the items found.</summary>
        public List<T> Items { get; set; } = new();

        /// <summary>Gets or sets any errors encountered.</summary>
        public List<string> Errors { get; set; } = new();

        /// <summary>Gets or sets how long detection took.</summary>
        public TimeSpan Elapsed { get; set; }

        /// <summary>Factory for a successful result.</summary>
        /// <param name="items">The detected items.</param>
        /// <param name="elapsed">The elapsed time.</param>
        /// <returns>A successful detection result.</returns>
        public static DetectionResult<T> Succeeded(
            IEnumerable<T> items,
            TimeSpan elapsed) =>
            new DetectionResult<T>
            {
                Success = true,
                Items = items.ToList(),
                Elapsed = elapsed,
            };

        /// <summary>Factory for a failed result.</summary>
        /// <param name="error">The error message.</param>
        /// <returns>A failed detection result.</returns>
        public static DetectionResult<T> Failed(string error) =>
            new DetectionResult<T>
            {
                Success = false,
                Errors = new List<string> { error },
            };
    }
}
