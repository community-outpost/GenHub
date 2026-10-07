namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents VLC environment-mutating tests from running beside unrelated tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class VlcEnvironmentCollection
{
    /// <summary>
    /// The collection name used by VLC environment-mutating tests.
    /// </summary>
    public const string Name = "Vlc environment";
}
