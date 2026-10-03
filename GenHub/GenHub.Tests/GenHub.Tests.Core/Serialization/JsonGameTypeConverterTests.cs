using GenHub.Core.Models.Enums;
using System.Text.Json;
using Xunit;

namespace GenHub.Tests.Core.Serialization;

/// <summary>
/// Tests for <see cref="GenHub.Core.Serialization.JsonGameTypeConverter"/>.
/// </summary>
public class JsonGameTypeConverterTests
{
    /// <summary>
    /// Verifies that GameType is written as its string name.
    /// </summary>
    /// <param name="gameType">The game type to serialize.</param>
    /// <param name="expectedJson">The expected JSON string.</param>
    [Theory]
    [InlineData(GameType.Generals, "\"Generals\"")]
    [InlineData(GameType.ZeroHour, "\"ZeroHour\"")]
    [InlineData(GameType.Unknown, "\"Unknown\"")]
    public void Serialize_WritesGameTypeName(GameType gameType, string expectedJson)
    {
        var json = JsonSerializer.Serialize(gameType);
        Assert.Equal(expectedJson, json);
    }

    /// <summary>
    /// Verifies that string representations deserialize correctly, case-insensitively.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="expected">The expected GameType enum.</param>
    [Theory]
    [InlineData("\"Generals\"", GameType.Generals)]
    [InlineData("\"generals\"", GameType.Generals)]
    [InlineData("\"ZeroHour\"", GameType.ZeroHour)]
    [InlineData("\"zerohour\"", GameType.ZeroHour)]
    [InlineData("\"Unknown\"", GameType.Unknown)]
    public void Deserialize_ValidString_ReturnsExpectedGameType(string json, GameType expected)
    {
        var result = JsonSerializer.Deserialize<GameType>(json);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that unrecognized, future, or invalid string values gracefully fall back to Unknown instead of throwing.
    /// </summary>
    /// <param name="json">The JSON payload with unrecognized value.</param>
    [Theory]
    [InlineData("\"FutureGame\"")]
    [InlineData("\"\"")]
    [InlineData("\"0\"")]
    [InlineData("\"1\"")]
    [InlineData("\"9999\"")]
    public void Deserialize_UnknownString_ReturnsUnknownGameType(string json)
    {
        var result = JsonSerializer.Deserialize<GameType>(json);
        Assert.Equal(GameType.Unknown, result);
    }

    /// <summary>
    /// Verifies that numeric values deserialize if defined, or fall back to Unknown if undefined.
    /// </summary>
    /// <param name="json">The numeric JSON value.</param>
    /// <param name="expected">The expected GameType enum.</param>
    [Theory]
    [InlineData("0", GameType.Generals)]
    [InlineData("1", GameType.ZeroHour)]
    [InlineData("2", GameType.Unknown)]
    [InlineData("99999", GameType.Unknown)]
    public void Deserialize_NumericValue_ReturnsExpectedOrFallback(string json, GameType expected)
    {
        var result = JsonSerializer.Deserialize<GameType>(json);
        Assert.Equal(expected, result);
    }
}
