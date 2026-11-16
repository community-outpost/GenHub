using GenHub.ProxyLauncher;
using System.Text.Json;
using Xunit;

namespace GenHub.Tests.Core.Features.Launching;

/// <summary>
/// Contains tests for <see cref="ProxyJsonContext"/>.
/// </summary>
public class ProxyJsonContextTests
{
    /// <summary>
    /// Verifies proxy configuration deserializes through the trim-compatible source-gen context.
    /// </summary>
    [Fact]
    public void Deserialize_ValidConfig_PopulatesAllProperties()
    {
        const string json = @"{""TargetExecutable"":""game.exe"",""WorkingDirectory"":""C:\\Game"",""Arguments"":[""-a"",""-b""],""SteamAppId"":""1234""}";

        var config = JsonSerializer.Deserialize(json, ProxyJsonContext.Default.ProxyConfig);

        Assert.NotNull(config);
        Assert.Equal("game.exe", config.TargetExecutable);
        Assert.Equal("C:\\Game", config.WorkingDirectory);
        Assert.NotNull(config.Arguments);
        Assert.Equal(["-a", "-b"], config.Arguments);
        Assert.Equal("1234", config.SteamAppId);
    }

    /// <summary>
    /// Verifies missing optional properties deserialize as null.
    /// </summary>
    [Fact]
    public void Deserialize_MinimalConfig_LeavesOptionalsNull()
    {
        var config = JsonSerializer.Deserialize(@"{""TargetExecutable"":""game.exe""}", ProxyJsonContext.Default.ProxyConfig);

        Assert.NotNull(config);
        Assert.Equal("game.exe", config.TargetExecutable);
        Assert.Null(config.WorkingDirectory);
        Assert.Null(config.Arguments);
        Assert.Null(config.SteamAppId);
    }

    /// <summary>
    /// Verifies malformed JSON fails fast instead of producing a partial config.
    /// </summary>
    [Fact]
    public void Deserialize_MalformedJson_ThrowsJsonException()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{bad json", ProxyJsonContext.Default.ProxyConfig));
    }
}
