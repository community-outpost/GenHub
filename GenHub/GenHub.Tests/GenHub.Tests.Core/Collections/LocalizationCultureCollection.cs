namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents culture-mutating localization tests from running beside unrelated tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizationCultureCollection
{
    /// <summary>
    /// The collection name used by culture-mutating tests.
    /// </summary>
    public const string Name = "Localization culture";
}
