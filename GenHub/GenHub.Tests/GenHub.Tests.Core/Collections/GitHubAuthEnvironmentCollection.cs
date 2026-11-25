namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents GitHub environment-mutating auth tests from running beside unrelated tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class GitHubAuthEnvironmentCollection
{
    /// <summary>
    /// The collection name used by GitHub environment-mutating tests.
    /// </summary>
    public const string Name = "GitHub auth environment";
}
