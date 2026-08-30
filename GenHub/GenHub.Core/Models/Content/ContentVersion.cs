namespace GenHub.Core.Models.Content;

/// <summary>
/// An ordered, comparable representation of a publisher version string.
/// Components run most-significant first, so "060526_QFE1" becomes [2026, 6, 5, 1]
/// and "2025-11-07" becomes [2025, 11, 7]. Missing trailing components compare as zero,
/// which makes "1.7" and "1.7.0" equal.
/// </summary>
public readonly struct ContentVersion : IComparable<ContentVersion>, IEquatable<ContentVersion>
{
    private static readonly IReadOnlyList<long> NoComponents =
        Array.AsReadOnly(Array.Empty<long>());

    private readonly IReadOnlyList<long>? _components;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentVersion"/> struct.
    /// </summary>
    /// <param name="components">The version components, most-significant first.</param>
    public ContentVersion(params long[] components)
    {
        ArgumentNullException.ThrowIfNull(components);
        _components = Array.AsReadOnly((long[])components.Clone());
    }

    /// <summary>
    /// Gets the version components, most-significant first.
    /// </summary>
    public IReadOnlyList<long> Components => _components ?? NoComponents;

    /// <summary>
    /// Gets a value indicating whether this version carries no components.
    /// </summary>
    public bool IsEmpty => Components.Count == 0;

    /// <summary>
    /// Determines whether two versions are equal.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><c>true</c> if the versions are equal.</returns>
    public static bool operator ==(ContentVersion left, ContentVersion right) => left.CompareTo(right) == 0;

    /// <summary>
    /// Determines whether two versions differ.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><c>true</c> if the versions differ.</returns>
    public static bool operator !=(ContentVersion left, ContentVersion right) => left.CompareTo(right) != 0;

    /// <summary>
    /// Determines whether the left version precedes the right version.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is older.</returns>
    public static bool operator <(ContentVersion left, ContentVersion right) => left.CompareTo(right) < 0;

    /// <summary>
    /// Determines whether the left version follows the right version.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is newer.</returns>
    public static bool operator >(ContentVersion left, ContentVersion right) => left.CompareTo(right) > 0;

    /// <summary>
    /// Determines whether the left version precedes or equals the right version.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is not newer.</returns>
    public static bool operator <=(ContentVersion left, ContentVersion right) => left.CompareTo(right) <= 0;

    /// <summary>
    /// Determines whether the left version follows or equals the right version.
    /// </summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    /// <returns><c>true</c> if <paramref name="left"/> is not older.</returns>
    public static bool operator >=(ContentVersion left, ContentVersion right) => left.CompareTo(right) >= 0;

    /// <inheritdoc/>
    public int CompareTo(ContentVersion other)
    {
        var left = Components;
        var right = other.Components;

        for (var i = 0; i < Math.Max(left.Count, right.Count); i++)
        {
            var leftComponent = i < left.Count ? left[i] : 0;
            var rightComponent = i < right.Count ? right[i] : 0;

            if (leftComponent != rightComponent)
            {
                return leftComponent.CompareTo(rightComponent);
            }
        }

        return 0;
    }

    /// <inheritdoc/>
    public bool Equals(ContentVersion other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is ContentVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        var components = Components;

        var significant = components.Count;
        while (significant > 0 && components[significant - 1] == 0)
        {
            significant--;
        }

        for (var i = 0; i < significant; i++)
        {
            hash.Add(components[i]);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc/>
    public override string ToString() => string.Join('.', Components);
}
