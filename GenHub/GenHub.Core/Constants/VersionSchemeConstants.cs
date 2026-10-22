namespace GenHub.Core.Constants;

/// <summary>
/// Version scheme identifiers referenced by the "versionScheme" field of a provider definition.
/// </summary>
public static class VersionSchemeConstants
{
    /// <summary>Numeric and semantic versions (e.g. "20251226", "weekly-2025-12-26", "1.7.2").</summary>
    public const string Numeric = "numeric";

    /// <summary>ISO calendar-date versions (e.g. "2025-11-07").</summary>
    public const string IsoDate = "iso-date";

    /// <summary>Generals Online date plus QFE versions (e.g. "060526_QFE1").</summary>
    public const string MmddyyQfe = "mmddyy-qfe";

    /// <summary>Scheme applied when a provider definition declares none.</summary>
    public const string Default = Numeric;
}
