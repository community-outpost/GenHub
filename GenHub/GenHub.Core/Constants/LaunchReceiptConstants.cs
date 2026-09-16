namespace GenHub.Core.Constants;

/// <summary>Launch receipt serialization constants.</summary>
public static class LaunchReceiptConstants
{
    /// <summary>Schema storing environment names without value fingerprints.</summary>
    public const int CurrentSchemaVersion = 2;

    /// <summary>Display text when an entry point could not be resolved.</summary>
    public const string UnresolvedEntryPoint = "(unresolved)";

    /// <summary>Previous receipt schema supported for migration.</summary>
    public const int LegacySchemaVersion = 1;

    /// <summary>Suffix for temporary receipt files.</summary>
    public const string TemporaryFileExtension = ".tmp";

    /// <summary>Display text for a missing recorded value.</summary>
    public const string MissingValue = "(none)";
}
