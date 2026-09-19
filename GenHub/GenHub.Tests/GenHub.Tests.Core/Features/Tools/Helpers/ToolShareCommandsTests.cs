using GenHub.Core.Models.Enums;
using GenHub.Features.Tools.Helpers;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace GenHub.Tests.Core.Features.Tools.Helpers;

/// <summary>
/// Unit tests for <see cref="ToolShareCommands"/>.
/// </summary>
public sealed class ToolShareCommandsTests
{
    /// <summary>
    /// Verifies that a protocol-link import selects the game, sets the URL, and forwards cancellation.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportSharedUrlAsync_ForwardsGameUrlAndCancellationTokenAsync()
    {
        using var cancellation = new CancellationTokenSource();
        GameType? selectedGame = null;
        string? importUrl = null;
        CancellationToken receivedToken = default;

        await ToolShareCommands.ImportSharedUrlAsync(
            "https://example.com/cool.map",
            GameType.Generals,
            game => selectedGame = game,
            url => importUrl = url,
            token =>
            {
                receivedToken = token;
                return Task.CompletedTask;
            },
            cancellation.Token);

        Assert.Equal(GameType.Generals, selectedGame);
        Assert.Equal("https://example.com/cool.map", importUrl);
        Assert.Equal(cancellation.Token, receivedToken);
    }

    /// <summary>
    /// Verifies that an import without a recorded game keeps the current selection.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task ImportSharedUrlAsync_WithoutGame_SkipsGameSelectionAsync()
    {
        bool gameSelected = false;
        bool importRan = false;

        await ToolShareCommands.ImportSharedUrlAsync(
            "https://example.com/cool.map",
            null,
            _ => gameSelected = true,
            _ => { },
            _ =>
            {
                importRan = true;
                return Task.CompletedTask;
            });

        Assert.False(gameSelected);
        Assert.True(importRan);
    }
}
