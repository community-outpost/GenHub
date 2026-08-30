using System.Collections.ObjectModel;

namespace GenHub.Core.Extensions;

/// <summary>
/// Extension methods for IEnumerable to ObservableCollection conversions.
/// </summary>
public static class EnumerableExtensions
{
    /// <summary>
    /// Converts an IEnumerable to an ObservableCollection.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <returns>An ObservableCollection containing the elements from the source.</returns>
    public static ObservableCollection<T> ToObservableCollection<T>(this IEnumerable<T> source)
    {
        return new ObservableCollection<T>(source);
    }
}