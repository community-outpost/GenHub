using Avalonia.Media;
using System.Collections.Concurrent;

namespace GenHub.Infrastructure.Converters;

/// <summary>
/// Shared cache of immutable <see cref="SolidColorBrush"/> instances for value converters.
/// Converters must return stable instances: a fresh brush per evaluation keeps brush
/// transitions perpetually dirty, which can recurse into a stack overflow when bindings
/// re-evaluate rapidly (for example while demo panels attach and detach).
/// </summary>
internal static class BrushCache
{
    private static readonly ConcurrentDictionary<Color, SolidColorBrush> Cache = new();

    /// <summary>
    /// Gets the shared transparent brush used for null or unparsable values.
    /// </summary>
    public static SolidColorBrush Transparent { get; } = new(Colors.Transparent);

    /// <summary>
    /// Gets the shared brush for the given color, creating and caching it on first use.
    /// </summary>
    /// <param name="color">The color to get a brush for.</param>
    /// <returns>The shared <see cref="SolidColorBrush"/> instance for <paramref name="color"/>.</returns>
    public static SolidColorBrush Get(Color color)
    {
        if (color == Colors.Transparent)
        {
            return Transparent;
        }

        return Cache.GetOrAdd(color, static c => new SolidColorBrush(c));
    }
}
