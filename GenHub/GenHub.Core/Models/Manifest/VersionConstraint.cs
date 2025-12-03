using System;
using System.Text.RegularExpressions;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Represents a semantic version constraint for dependency resolution.
/// Supports ranges, exact matches, and constraint expressions.
/// </summary>
public class VersionConstraint
{
    /// <summary>
    /// Gets or sets the minimum version required (inclusive by default).
    /// </summary>
    public string? MinVersion { get; set; }

    /// <summary>
    /// Gets or sets the maximum version allowed (inclusive by default).
    /// </summary>
    public string? MaxVersion { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the minimum version bound is inclusive.
    /// </summary>
    public bool MinInclusive { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether the maximum version bound is inclusive.
    /// </summary>
    public bool MaxInclusive { get; set; } = true;

    /// <summary>
    /// Gets or sets an exact version requirement (overrides min/max if specified).
    /// </summary>
    public string? ExactVersion { get; set; }

    /// <summary>
    /// Gets or sets a constraint expression for complex version requirements.
    /// Examples: ">=1.0.0 &lt;2.0.0", "^3.0.0", "~1.2.0".
    /// </summary>
    public string? ConstraintExpression { get; set; }

    /// <summary>
    /// Creates a constraint that matches any version greater than or equal to the specified minimum.
    /// </summary>
    /// <param name="minVersion">The minimum version.</param>
    /// <returns>A version constraint.</returns>
    public static VersionConstraint AtLeast(string minVersion)
    {
        return new VersionConstraint
        {
            MinVersion = minVersion,
            MinInclusive = true,
        };
    }

    /// <summary>
    /// Creates a constraint that matches exactly the specified version.
    /// </summary>
    /// <param name="version">The exact version required.</param>
    /// <returns>A version constraint.</returns>
    public static VersionConstraint Exact(string version)
    {
        return new VersionConstraint
        {
            ExactVersion = version,
        };
    }

    /// <summary>
    /// Creates a constraint that matches versions within a range.
    /// </summary>
    /// <param name="minVersion">The minimum version (inclusive).</param>
    /// <param name="maxVersion">The maximum version (inclusive).</param>
    /// <returns>A version constraint.</returns>
    public static VersionConstraint Between(string minVersion, string maxVersion)
    {
        return new VersionConstraint
        {
            MinVersion = minVersion,
            MaxVersion = maxVersion,
            MinInclusive = true,
            MaxInclusive = true,
        };
    }

    /// <summary>
    /// Creates a constraint that accepts any version.
    /// </summary>
    /// <returns>A version constraint that accepts any version.</returns>
    public static VersionConstraint Any()
    {
        return new VersionConstraint();
    }

    /// <summary>
    /// Checks if a given version satisfies this constraint.
    /// </summary>
    /// <param name="version">The version to check.</param>
    /// <returns>True if the version satisfies the constraint.</returns>
    public bool IsSatisfiedBy(string? version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return false;
        }

        // Exact version match takes precedence
        if (!string.IsNullOrEmpty(ExactVersion))
        {
            return NormalizeVersion(version) == NormalizeVersion(ExactVersion);
        }

        // Parse constraint expression if present
        if (!string.IsNullOrEmpty(ConstraintExpression))
        {
            return EvaluateConstraintExpression(version, ConstraintExpression);
        }

        // Check min/max bounds
        var normalizedVersion = NormalizeVersion(version);
        var versionValue = ParseVersionToInt(normalizedVersion);

        if (!string.IsNullOrEmpty(MinVersion))
        {
            var minValue = ParseVersionToInt(NormalizeVersion(MinVersion));
            if (MinInclusive)
            {
                if (versionValue < minValue)
                {
                    return false;
                }
            }
            else
            {
                if (versionValue <= minValue)
                {
                    return false;
                }
            }
        }

        if (!string.IsNullOrEmpty(MaxVersion))
        {
            var maxValue = ParseVersionToInt(NormalizeVersion(MaxVersion));
            if (MaxInclusive)
            {
                if (versionValue > maxValue)
                {
                    return false;
                }
            }
            else
            {
                if (versionValue >= maxValue)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Normalizes a version string by removing leading 'v' and standardizing format.
    /// </summary>
    private static string NormalizeVersion(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return "0";
        }

        // Remove leading 'v' or 'V'
        var normalized = version.TrimStart('v', 'V');

        // Remove any non-numeric characters except dots
        normalized = Regex.Replace(normalized, @"[^0-9.]", string.Empty);

        return string.IsNullOrEmpty(normalized) ? "0" : normalized;
    }

    /// <summary>
    /// Parses a version string to an integer for comparison.
    /// Handles versions like "1.04", "1.08", "2.0.0" etc.
    /// </summary>
    private static int ParseVersionToInt(string version)
    {
        if (string.IsNullOrEmpty(version))
        {
            return 0;
        }

        var parts = version.Split('.');
        var result = 0;
        var multiplier = 10000;

        foreach (var part in parts)
        {
            if (int.TryParse(part, out var value))
            {
                result += value * multiplier;
                multiplier /= 100;

                if (multiplier < 1)
                {
                    break;
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Evaluates a constraint expression against a version.
    /// Supports: >=, >, &lt;=, &lt;, =, ^, ~.
    /// </summary>
    private static bool EvaluateConstraintExpression(string version, string expression)
    {
        // Split by logical operators (space = AND, || = OR)
        var orParts = expression.Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var orPart in orParts)
        {
            var andParts = orPart.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var allMatch = true;

            foreach (var constraint in andParts)
            {
                if (!EvaluateSingleConstraint(version, constraint.Trim()))
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluates a single constraint expression (e.g., ">=1.0.0", "^2.0.0").
    /// </summary>
    private static bool EvaluateSingleConstraint(string version, string constraint)
    {
        if (string.IsNullOrEmpty(constraint))
        {
            return true;
        }

        // Caret (^) - compatible with version (same major)
        if (constraint.StartsWith("^", StringComparison.Ordinal))
        {
            var targetVersion = constraint.Substring(1);
            var versionParts = NormalizeVersion(version).Split('.');
            var targetParts = NormalizeVersion(targetVersion).Split('.');

            if (versionParts.Length > 0 && targetParts.Length > 0)
            {
                return versionParts[0] == targetParts[0] &&
                       ParseVersionToInt(NormalizeVersion(version)) >= ParseVersionToInt(NormalizeVersion(targetVersion));
            }

            return false;
        }

        // Tilde (~) - approximately equivalent (same major.minor)
        if (constraint.StartsWith("~", StringComparison.Ordinal))
        {
            var targetVersion = constraint.Substring(1);
            var versionParts = NormalizeVersion(version).Split('.');
            var targetParts = NormalizeVersion(targetVersion).Split('.');

            if (versionParts.Length >= 2 && targetParts.Length >= 2)
            {
                return versionParts[0] == targetParts[0] &&
                       versionParts[1] == targetParts[1] &&
                       ParseVersionToInt(NormalizeVersion(version)) >= ParseVersionToInt(NormalizeVersion(targetVersion));
            }

            return false;
        }

        // Comparison operators
        if (constraint.StartsWith(">=", StringComparison.Ordinal))
        {
            return ParseVersionToInt(NormalizeVersion(version)) >= ParseVersionToInt(NormalizeVersion(constraint.Substring(2)));
        }

        if (constraint.StartsWith("<=", StringComparison.Ordinal))
        {
            return ParseVersionToInt(NormalizeVersion(version)) <= ParseVersionToInt(NormalizeVersion(constraint.Substring(2)));
        }

        if (constraint.StartsWith(">", StringComparison.Ordinal))
        {
            return ParseVersionToInt(NormalizeVersion(version)) > ParseVersionToInt(NormalizeVersion(constraint.Substring(1)));
        }

        if (constraint.StartsWith("<", StringComparison.Ordinal))
        {
            return ParseVersionToInt(NormalizeVersion(version)) < ParseVersionToInt(NormalizeVersion(constraint.Substring(1)));
        }

        if (constraint.StartsWith("=", StringComparison.Ordinal))
        {
            return NormalizeVersion(version) == NormalizeVersion(constraint.Substring(1));
        }

        // Plain version - exact match
        return NormalizeVersion(version) == NormalizeVersion(constraint);
    }
}
