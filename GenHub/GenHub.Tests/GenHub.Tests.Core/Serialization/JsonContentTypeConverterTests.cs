using System.Text.Json;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Serialization;

/// <summary>
/// Tests for <see cref="GenHub.Core.Serialization.JsonContentTypeConverter"/>.
/// </summary>
public class JsonContentTypeConverterTests
{
    /// <summary>
    /// Verifies that ContentType is written as its string name.
    /// </summary>
    /// <param name="contentType">The content type to serialize.</param>
    /// <param name="expectedJson">The expected JSON string.</param>
    [Theory]
    [InlineData(ContentType.Mod, "\"Mod\"")]
    [InlineData(ContentType.GenHubBuild, "\"GenHubBuild\"")]
    [InlineData(ContentType.GameClient, "\"GameClient\"")]
    [InlineData(ContentType.Patch, "\"Patch\"")]
    [InlineData(ContentType.ModdingTool, "\"ModdingTool\"")]
    [InlineData(ContentType.Executable, "\"Executable\"")]
    [InlineData(ContentType.UnknownContentType, "\"UnknownContentType\"")]
    public void Serialize_WritesContentTypeName(ContentType contentType, string expectedJson)
    {
        var json = JsonSerializer.Serialize(contentType);
        Assert.Equal(expectedJson, json);
    }

    /// <summary>
    /// Verifies that string representations deserialize correctly, case-insensitively.
    /// </summary>
    /// <param name="json">The JSON string.</param>
    /// <param name="expected">The expected ContentType enum.</param>
    [Theory]
    [InlineData("\"GenHubBuild\"", ContentType.GenHubBuild)]
    [InlineData("\"genhubbuild\"", ContentType.GenHubBuild)]
    [InlineData("\"Mod\"", ContentType.Mod)]
    [InlineData("\"mod\"", ContentType.Mod)]
    [InlineData("\"GameClient\"", ContentType.GameClient)]
    [InlineData("\"Patch\"", ContentType.Patch)]
    [InlineData("\"ModdingTool\"", ContentType.ModdingTool)]
    [InlineData("\"Executable\"", ContentType.Executable)]
    [InlineData("\"ContentBundle\"", ContentType.ContentBundle)]
    public void Deserialize_ValidString_ReturnsExpectedContentType(string json, ContentType expected)
    {
        var result = JsonSerializer.Deserialize<ContentType>(json);
        Assert.Equal(expected, result);
    }

    /// <summary>
    /// Verifies that unrecognized, future, or invalid string values gracefully fall back to UnknownContentType instead of throwing.
    /// </summary>
    /// <param name="json">The JSON payload with unrecognized value.</param>
    [Theory]
    [InlineData("\"NonExistentFutureType\"")]
    [InlineData("\"Unknown\"")]
    [InlineData("\"\"")]
    [InlineData("\"12345\"")]
    public void Deserialize_UnknownString_ReturnsUnknownContentType(string json)
    {
        var result = JsonSerializer.Deserialize<ContentType>(json);
        Assert.Equal(ContentType.UnknownContentType, result);
    }

    /// <summary>
    /// Verifies that numeric values deserialize if defined, or fall back to UnknownContentType if undefined.
    /// </summary>
    /// <param name="json">The numeric JSON value.</param>
    /// <param name="expected">The expected ContentType enum.</param>
    [Theory]
    [InlineData("1", ContentType.GameClient)]
    [InlineData("2", ContentType.Mod)]
    [InlineData("18", ContentType.UnknownContentType)]
    [InlineData("19", ContentType.GenHubBuild)]
    [InlineData("99999", ContentType.UnknownContentType)]
    public void Deserialize_NumericValue_ReturnsExpectedOrFallback(string json, ContentType expected)
    {
        var result = JsonSerializer.Deserialize<ContentType>(json);
        Assert.Equal(expected, result);
    }
}
