using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;

namespace GenHub.Tests.Core.Models;

/// <summary>
/// Unit tests for <see cref="GameClient"/>.
/// </summary>
public class GameClientTests
{
    /// <summary>
    /// Verifies that default values are set correctly.
    /// </summary>
    [Fact]
    public void GameClient_Defaults_AreSet()
    {
        var client = new GameClient();
        Assert.False(string.IsNullOrEmpty(client.Id));
        Assert.Equal(string.Empty, client.Name);
        Assert.Equal(string.Empty, client.ExecutablePath);
        Assert.Equal(string.Empty, client.WorkingDirectory);
        Assert.Equal(GameType.Generals, client.GameType);
        Assert.Equal(string.Empty, client.InstallationId);
    }

    /// <summary>
    /// Verifies IsValid returns false when executable doesn't exist.
    /// </summary>
    [Fact]
    public void GameClient_IsValid_ReturnsFalse_WhenExecutableDoesNotExist()
    {
        var client = new GameClient
        {
            ExecutablePath = "C:\\NonExistent\\generals.exe",
        };

        Assert.False(client.IsValid);
    }

    /// <summary>
    /// Verifies ToString returns expected format.
    /// </summary>
    [Fact]
    public void GameClient_ToString_ReturnsExpectedFormat()
    {
        var client = new GameClient
        {
            Name = "Test Client",
            GameType = GameType.ZeroHour,
        };

        var result = client.ToString();
        Assert.Contains("Test Client", result);
        Assert.Contains("ZeroHour", result);
    }
}