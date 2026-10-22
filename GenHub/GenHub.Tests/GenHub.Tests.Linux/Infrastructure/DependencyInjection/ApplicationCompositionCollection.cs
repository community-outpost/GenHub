namespace GenHub.Tests.Linux.Infrastructure.DependencyInjection;

/// <summary>
/// Prevents temporary process environment changes from overlapping other tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class ApplicationCompositionCollection
{
    /// <summary>
    /// The xUnit collection name.
    /// </summary>
    public const string Name = "Application composition";
}
