namespace GenHub.Tests.Core.Features.Content.Tools;

/// <summary>
/// Serializes tests that mutate Playwright process environment variables.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class PlaywrightEnvironmentCollection
{
    /// <summary>
    /// The xUnit collection name.
    /// </summary>
    public const string Name = "Playwright environment";
}
