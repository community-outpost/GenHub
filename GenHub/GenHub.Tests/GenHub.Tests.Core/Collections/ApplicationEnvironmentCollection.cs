namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents tests that redirect the application data and home environment variables from running beside unrelated tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ApplicationEnvironmentCollection
{
    /// <summary>
    /// The collection name used by application environment-mutating tests.
    /// </summary>
    public const string Name = "Application environment";
}
