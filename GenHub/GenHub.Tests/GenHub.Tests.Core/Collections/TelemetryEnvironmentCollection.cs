namespace GenHub.Tests.Core.Collections;

/// <summary>
/// Prevents telemetry environment-mutating tests from running beside unrelated tests.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TelemetryEnvironmentCollection
{
    /// <summary>
    /// The collection name used by telemetry environment-mutating tests.
    /// </summary>
    public const string Name = "Telemetry environment";
}
