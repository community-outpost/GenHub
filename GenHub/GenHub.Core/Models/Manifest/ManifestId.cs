using System.Text.Json.Serialization;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Strongly typed value object for manifest identifiers.
/// Encapsulates validation and equality semantics for manifest ids.
/// </summary>
[JsonConverter(typeof(ManifestIdJsonConverter))]
public readonly struct ManifestId(string value)
: IEquatable<ManifestId>
{
    /// <summary>
    /// Gets the underlying string value of the manifest id.
    /// </summary>
    public string Value { get; } = value;

    /// <summary>
    /// Conversion from <see cref="string"/> to <see cref="ManifestId"/> which validates the input.
    /// Made implicit for convenience in tests and call-sites that use plain strings.
    /// </summary>
    /// <param name="id">The manifest id string.</param>
    public static implicit operator ManifestId(string id) => Create(id);

    /// <summary>
    /// Implicit conversion from <see cref="ManifestId"/> to <see cref="string"/> for convenience.
    /// </summary>
    /// <param name="id">The manifest id.</param>
    public static implicit operator string(ManifestId id) => id.Value;

    /// <summary>
    /// Equality operator.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(ManifestId left, ManifestId right) => left.Equals(right);

    /// <summary>
    /// Inequality operator.
    /// </summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns>True when not equal.</returns>
    public static bool operator !=(ManifestId left, ManifestId right) => !left.Equals(right);

    /// <summary>
    /// Creates a <see cref="ManifestId"/> from a string, validating the value.
    /// Throws <see cref="ArgumentException"/> when the id is invalid.
    /// </summary>
    /// <param name="id">The manifest id string to validate and wrap.</param>
    /// <returns>A validated <see cref="ManifestId"/> instance.</returns>
    public static ManifestId Create(string id)
    {
        if (!ManifestIdValidator.IsValid(id, out var reason))
        {
            throw new ArgumentException(reason, nameof(id));
        }

        return new ManifestId(id);
    }

    /// <inheritdoc/>
    public override string ToString() => Value;

    /// <inheritdoc/>
    public bool Equals(ManifestId other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public override bool Equals(object? obj)
    {
        if (obj is null)
        {
            return false;
        }

        return obj is ManifestId other && Equals(other);
    }

    /// <inheritdoc/>
    public override int GetHashCode() => Value.GetHashCode(StringComparison.OrdinalIgnoreCase);
}
