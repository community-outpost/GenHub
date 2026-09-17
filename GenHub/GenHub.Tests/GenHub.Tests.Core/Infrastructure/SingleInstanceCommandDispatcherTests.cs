using GenHub.Core.Constants;
using GenHub.Core.Infrastructure.SingleInstance;
using System;
using System.Collections.Generic;
using Xunit;

namespace GenHub.Tests.Core.Infrastructure;

/// <summary>
/// Unit tests for <see cref="SingleInstanceCommandDispatcher"/>.
/// </summary>
public class SingleInstanceCommandDispatcherTests
{
    /// <summary>
    /// Verifies that commands sent before subscription are buffered and replayed when subscribed.
    /// </summary>
    [Fact]
    public void DispatchOrBuffer_Should_Buffer_And_Replay_To_Subscribers()
    {
        var dispatcher = new SingleInstanceCommandDispatcher();
        dispatcher.DispatchOrBuffer(IpcCommands.ActivateCommand);

        var received = new List<string>();
        dispatcher.CommandReceived += (_, cmd) => received.Add(cmd);

        Assert.Single(received);
        Assert.Equal(IpcCommands.ActivateCommand, received[0]);
    }

    /// <summary>
    /// Verifies that an exception in one replayed item or subscriber does not crash replay of remaining items.
    /// </summary>
    [Fact]
    public void Replay_Should_Not_Fail_When_Subscriber_Throws()
    {
        var dispatcher = new SingleInstanceCommandDispatcher();
        dispatcher.DispatchOrBuffer(IpcCommands.ActivateCommand);
        dispatcher.DispatchOrBuffer($"{IpcCommands.LaunchProfilePrefix}test-profile");

        var received = new List<string>();
        dispatcher.CommandReceived += (_, cmd) =>
        {
            if (cmd == IpcCommands.ActivateCommand)
            {
                throw new InvalidOperationException("Simulated handler fault");
            }

            received.Add(cmd);
        };

        Assert.Single(received);
        Assert.Equal($"{IpcCommands.LaunchProfilePrefix}test-profile", received[0]);
    }

    /// <summary>
    /// Verifies that IsValidIpcCommand correctly distinguishes valid and invalid commands.
    /// </summary>
    /// <param name="command">The command string to evaluate.</param>
    /// <param name="expected">Whether the command is expected to be valid.</param>
    [Theory]
    [InlineData("activate", true)]
    [InlineData("ACTIVATE", true)]
    [InlineData("launch-profile:12345", true)]
    [InlineData("launch-profile:../malicious", false)]
    [InlineData("launch-profile:dir/file", false)]
    [InlineData("launch-profile:dir\\file", false)]
    [InlineData("subscribe:https://example.com/manifest.json", true)]
    [InlineData("subscribe:ftp://example.com/manifest.json", false)]
    [InlineData("import-profile:genhub://profile/import/xyz", true)]
    [InlineData("import-profile:genhub://profile/view/xyz", true)]
    [InlineData("unknown-command", false)]
    public void IsValidIpcCommand_Should_Validate_Correctly(string command, bool expected)
    {
        var actual = SingleInstanceCommandDispatcher.IsValidIpcCommand(command);
        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Verifies that TryProcessRawPayload ignores empty payloads and rejects invalid commands.
    /// </summary>
    [Fact]
    public void TryProcessRawPayload_Should_Reject_Empty_And_Invalid_Payloads()
    {
        var dispatcher = new SingleInstanceCommandDispatcher();

        Assert.False(dispatcher.TryProcessRawPayload(null, "Test"));
        Assert.False(dispatcher.TryProcessRawPayload(string.Empty, "Test"));
        Assert.False(dispatcher.TryProcessRawPayload("invalid-action:123", "Test"));
    }

    /// <summary>
    /// Verifies that TryProcessRawPayload dispatches valid commands to subscribers.
    /// </summary>
    [Fact]
    public void TryProcessRawPayload_Should_Dispatch_Valid_Commands()
    {
        var dispatcher = new SingleInstanceCommandDispatcher();
        var received = new List<string>();
        dispatcher.CommandReceived += (_, cmd) => received.Add(cmd);

        var handled = dispatcher.TryProcessRawPayload("activate\n", "Test");

        Assert.True(handled);
        Assert.Single(received);
        Assert.Equal("activate", received[0]);
    }
}
