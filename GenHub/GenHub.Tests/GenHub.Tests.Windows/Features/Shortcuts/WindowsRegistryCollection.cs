using Xunit;

namespace GenHub.Tests.Windows.Features.Shortcuts;

/// <summary>
/// Prevents registry tests from overlapping and racing.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class WindowsRegistryCollection
{
    /// <summary>
    /// The xUnit collection name.
    /// </summary>
    public const string Name = "Windows registry";
}
