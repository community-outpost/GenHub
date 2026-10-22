using GenHub.Features.Content.Services.SteamWorkshop;
using System.Text.Json;
using Xunit;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopFileSizeConverter"/>.
/// </summary>
public sealed class SteamWorkshopFileSizeConverterTests
{
    private readonly JsonSerializerOptions _options = new()
    {
        Converters = { new SteamWorkshopFileSizeConverter() },
    };

    /// <summary>
    /// Verifies that positive integers deserialize correctly.
    /// </summary>
    [Fact]
    public void Read_PositiveInteger_ReturnsExpected()
    {
        var json = "123456";
        var result = JsonSerializer.Deserialize<long>(json, _options);
        Assert.Equal(123456L, result);
    }

    /// <summary>
    /// Verifies that negative numbers clamp to zero.
    /// </summary>
    /// <param name="json">The JSON payload representing a negative number.</param>
    [Theory]
    [InlineData("-1")]
    [InlineData("-99999")]
    [InlineData("-12.34")]
    public void Read_NegativeNumber_ClampsToZero(string json)
    {
        var result = JsonSerializer.Deserialize<long>(json, _options);
        Assert.Equal(0L, result);
    }

    /// <summary>
    /// Verifies that numbers exceeding long.MaxValue clamp to long.MaxValue without overflowing.
    /// </summary>
    [Fact]
    public void Read_ExceedsLongMax_ClampsToLongMax()
    {
        var json = "1e19";
        var result = JsonSerializer.Deserialize<long>(json, _options);
        Assert.Equal(long.MaxValue, result);
    }
}
