using System.Text.Json;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Serialization;

/// <summary>
/// Tests for <see cref="GenHub.Core.Serialization.JsonWorkspaceStrategyConverter"/>.
/// </summary>
public class JsonWorkspaceStrategyConverterTests
{
    /// <summary>
    /// Verifies that the strategy is written as its member name rather than its ordinal.
    /// </summary>
    /// <param name="strategy">The strategy to serialize.</param>
    /// <param name="expectedJson">The expected JSON payload.</param>
    [Theory]
    [InlineData(WorkspaceStrategy.SymlinkOnly, "\"SymlinkOnly\"")]
    [InlineData(WorkspaceStrategy.FullCopy, "\"FullCopy\"")]
    [InlineData(WorkspaceStrategy.HybridCopySymlink, "\"HybridCopySymlink\"")]
    [InlineData(WorkspaceStrategy.HardLink, "\"HardLink\"")]
    public void Serialize_WritesStrategyName(WorkspaceStrategy strategy, string expectedJson)
    {
        var json = JsonSerializer.Serialize(strategy);

        Assert.Equal(expectedJson, json);
    }

    /// <summary>
    /// Verifies that the ordinals written by releases up to v0.0.3 still map to the same strategies.
    /// </summary>
    /// <param name="json">The legacy numeric JSON payload.</param>
    /// <param name="expected">The strategy the payload must resolve to.</param>
    [Theory]
    [InlineData("0", WorkspaceStrategy.SymlinkOnly)]
    [InlineData("1", WorkspaceStrategy.FullCopy)]
    [InlineData("2", WorkspaceStrategy.HybridCopySymlink)]
    [InlineData("3", WorkspaceStrategy.HardLink)]
    public void Deserialize_LegacyNumericValue_ReturnsOriginalStrategy(string json, WorkspaceStrategy expected)
    {
        var result = JsonSerializer.Deserialize<WorkspaceStrategy>(json);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that string payloads are still accepted.
    /// </summary>
    /// <param name="json">The string JSON payload.</param>
    /// <param name="expected">The strategy the payload must resolve to.</param>
    [Theory]
    [InlineData("\"SymlinkOnly\"", WorkspaceStrategy.SymlinkOnly)]
    [InlineData("\"FullCopy\"", WorkspaceStrategy.FullCopy)]
    [InlineData("\"HybridCopySymlink\"", WorkspaceStrategy.HybridCopySymlink)]
    [InlineData("\"HardLink\"", WorkspaceStrategy.HardLink)]
    [InlineData("\"hardlink\"", WorkspaceStrategy.HardLink)]
    public void Deserialize_StringValue_ReturnsMatchingStrategy(string json, WorkspaceStrategy expected)
    {
        var result = JsonSerializer.Deserialize<WorkspaceStrategy>(json);

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that a round trip preserves the strategy and produces a string payload.
    /// </summary>
    /// <param name="strategy">The strategy to round trip.</param>
    [Theory]
    [InlineData(WorkspaceStrategy.SymlinkOnly)]
    [InlineData(WorkspaceStrategy.FullCopy)]
    [InlineData(WorkspaceStrategy.HybridCopySymlink)]
    [InlineData(WorkspaceStrategy.HardLink)]
    public void RoundTrip_PreservesStrategy(WorkspaceStrategy strategy)
    {
        var json = JsonSerializer.Serialize(strategy);

        using (var document = JsonDocument.Parse(json))
        {
            Assert.Equal(JsonValueKind.String, document.RootElement.ValueKind);
        }

        Assert.Equal(strategy, JsonSerializer.Deserialize<WorkspaceStrategy>(json));
    }

    /// <summary>
    /// Verifies that unrecognised payloads fall back to the default strategy.
    /// </summary>
    /// <param name="json">The unrecognised JSON payload.</param>
    [Theory]
    [InlineData("999")]
    [InlineData("\"NotAStrategy\"")]
    public void Deserialize_UnknownValue_ReturnsHardLink(string json)
    {
        var result = JsonSerializer.Deserialize<WorkspaceStrategy>(json);

        Assert.Equal(WorkspaceStrategy.HardLink, result);
    }
}
