namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents Online edge environment-mutating tests from running beside unrelated tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OnlineEnvironmentCollection
{
    /// <summary>
    /// The collection name used by Online edge environment-mutating tests.
    /// </summary>
    public const string Name = "Online environment";
}
