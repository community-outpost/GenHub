using Xunit;

namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents tests that mutate global storage migration static overrides from running in parallel.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class StorageMigrationStaticStateCollection
{
    /// <summary>
    /// The collection name used by storage migration static-mutating tests.
    /// </summary>
    public const string Name = "Storage migration static state";
}
