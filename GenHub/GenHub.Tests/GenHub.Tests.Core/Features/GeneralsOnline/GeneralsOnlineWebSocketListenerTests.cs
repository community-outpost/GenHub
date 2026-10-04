using GenHub.Core.Constants;
using GenHub.Features.GeneralsOnline.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineWebSocketListener"/> connection validation.
/// </summary>
public class GeneralsOnlineWebSocketListenerTests
{
    /// <summary>
    /// Tests that an invalid WebSocket URI fails fast without starting the loop.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectAsync_WithInvalidUri_ShouldFailWithoutConnectingAsync()
    {
        // Arrange
        using var listener = CreateListener();

        // Act
        var result = await listener.ConnectAsync("not a uri", "session-token");

        // Assert
        Assert.False(result.Success);
        Assert.False(listener.IsConnected);
    }

    /// <summary>
    /// Tests that missing arguments fail without starting the loop.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectAsync_WithoutArguments_ShouldFailWithoutConnectingAsync()
    {
        // Arrange
        using var listener = CreateListener();

        // Act
        var result = await listener.ConnectAsync(string.Empty, string.Empty);

        // Assert
        Assert.False(result.Success);
        Assert.False(listener.IsConnected);
    }

    /// <summary>
    /// Tests that a non-WebSocket absolute URI fails without starting the loop.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectAsync_WithHttpUri_ShouldFailWithoutConnectingAsync()
    {
        // Arrange
        using var listener = CreateListener();

        // Act
        var result = await listener.ConnectAsync("http://example.com/socket", "session-token");

        // Assert
        Assert.False(result.Success);
        Assert.False(listener.IsConnected);
    }

    /// <summary>
    /// Tests the message byte-budget boundary.
    /// </summary>
    /// <param name="totalBytes">The bytes accumulated so far.</param>
    /// <param name="frameCount">The incoming frame size in bytes.</param>
    /// <param name="expected">Whether the frame exceeds the budget.</param>
    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(0, GeneralsOnlineConstants.WebSocketMaxMessageBytes, false)]
    [InlineData(GeneralsOnlineConstants.WebSocketMaxMessageBytes, 1, true)]
    [InlineData(GeneralsOnlineConstants.WebSocketMaxMessageBytes - 8, 8, false)]
    [InlineData(GeneralsOnlineConstants.WebSocketMaxMessageBytes - 8, 9, true)]
    public void ExceedsMaxMessageBytes_ShouldEnforceByteBudget(int totalBytes, int frameCount, bool expected)
    {
        Assert.Equal(expected, GeneralsOnlineWebSocketListener.ExceedsMaxMessageBytes(totalBytes, frameCount));
    }

    /// <summary>
    /// Tests that the room selection payload uses the backend wire contract.
    /// The room field is lowercase: the backend looks it up case-sensitively.
    /// </summary>
    [Fact]
    public void BuildRoomSelectionPayload_ShouldUseWireContract()
    {
        Assert.Equal("""{"msg_id":3,"room":0}""", GeneralsOnlineWebSocketListener.BuildRoomSelectionPayload(0));
    }

    /// <summary>
    /// Tests that message ids parse from objects and that non-object payloads are ignored.
    /// </summary>
    /// <param name="payload">The WebSocket payload.</param>
    /// <param name="expected">The expected message id, or null when ignored.</param>
    [Theory]
    [InlineData("""{"msg_id": 7}""", 7)]
    [InlineData("[]", null)]
    [InlineData("null", null)]
    [InlineData("not json", null)]
    [InlineData("""{"other": 1}""", null)]
    public void ReadMessageId_ShouldParseObjectIdsAndIgnoreOthers(string payload, int? expected)
    {
        Assert.Equal(expected, GeneralsOnlineWebSocketListener.ReadMessageId(payload));
    }

    /// <summary>
    /// Tests that room chat messages parse the sender text and flags.
    /// </summary>
    /// <param name="admin">Whether the sender is an admin.</param>
    /// <param name="action">Whether the chat is an action (/me).</param>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReadRoomChat_WithChatPayload_ShouldParseMessage(bool admin, bool action)
    {
        // Arrange
        var adminJson = admin ? "true" : "false";
        var actionJson = action ? "true" : "false";
        var payload = $$"""{"msg_id":2,"message":"[General] Hello","admin":{{adminJson}},"action":{{actionJson}}}""";

        // Act
        var chat = GeneralsOnlineWebSocketListener.ReadRoomChat(payload);

        // Assert
        Assert.NotNull(chat);
        Assert.Equal("[General] Hello", chat.Message);
        Assert.Equal(admin, chat.IsAdmin);
        Assert.Equal(action, chat.IsAction);
    }

    /// <summary>
    /// Tests that blank or malformed chat payloads are ignored.
    /// </summary>
    /// <param name="payload">The WebSocket payload.</param>
    [Theory]
    [InlineData("""{"msg_id":2,"message":"  "}""")]
    [InlineData("""{"msg_id":2}""")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void ReadRoomChat_WithEmptyPayload_ShouldReturnNull(string payload)
    {
        Assert.Null(GeneralsOnlineWebSocketListener.ReadRoomChat(payload));
    }

    /// <summary>
    /// Tests that cancelling the connect caller token does not stop the reconnect loop.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ConnectAsync_CallerTokenCancelled_ShouldKeepLoopRunningAsync()
    {
        // Arrange: the discard port refuses immediately, leaving the loop in backoff.
        using var listener = CreateListener();
        using var caller = new CancellationTokenSource();
        var result = await listener.ConnectAsync("ws://127.0.0.1:9/", "session-token", caller.Token);
        Assert.True(result.Success);

        // Act
        caller.Cancel();
        await Task.Delay(50);

        // Assert
        Assert.True(listener.IsConnected);
        await listener.DisconnectAsync(CancellationToken.None);
        Assert.False(listener.IsConnected);
    }

    /// <summary>
    /// Tests that friend chat payloads parse the source, target, and message text.
    /// </summary>
    [Fact]
    public void ReadFriendChat_ValidPayload_ShouldParseCorrectly()
    {
        var payload = """{"source_user_id": 1052, "target_user_id": 1053, "message": "GGWP"}""";
        var chat = GeneralsOnlineWebSocketListener.ReadFriendChat(payload);

        Assert.NotNull(chat);
        Assert.Equal(1052, chat.SourceUserId);
        Assert.Equal(1053, chat.TargetUserId);
        Assert.Equal("GGWP", chat.Message);
    }

    /// <summary>
    /// Tests that friend presence payloads parse the display name and online status.
    /// </summary>
    [Fact]
    public void ReadFriendPresence_ValidPayload_ShouldParseCorrectly()
    {
        var payload = """{"display_name": "GeneralAlex", "online": true}""";
        var presence = GeneralsOnlineWebSocketListener.ReadFriendPresence(payload);

        Assert.NotNull(presence);
        Assert.Equal("GeneralAlex", presence.DisplayName);
        Assert.True(presence.IsOnline);
        Assert.Equal(-1, presence.UserId);
    }

    /// <summary>
    /// Tests that friend presence payloads parse user id when present.
    /// </summary>
    [Fact]
    public void ReadFriendPresence_WithUserId_ShouldParseCorrectly()
    {
        var payload = """{"user_id": 42, "display_name": "GeneralAlex", "online": true}""";
        var presence = GeneralsOnlineWebSocketListener.ReadFriendPresence(payload);

        Assert.NotNull(presence);
        Assert.Equal(42, presence.UserId);
        Assert.Equal("GeneralAlex", presence.DisplayName);
        Assert.True(presence.IsOnline);
    }

    /// <summary>
    /// Tests that room occupant payloads parse occupants with admin flags.
    /// </summary>
    [Fact]
    public void ReadRoomOccupants_ValidPayload_ShouldParseCorrectly()
    {
        var payload = """{"members": [{"UserID": 101, "Name": "Admin", "IsAdmin": true}, {"UserID": 102, "Name": "Player", "IsAdmin": false}]}""";
        var occupants = GeneralsOnlineWebSocketListener.ReadRoomOccupants(payload);

        Assert.NotNull(occupants);
        Assert.Equal(2, occupants.Occupants.Count);
        Assert.Equal(101, occupants.Occupants[0].UserId);
        Assert.Equal("Admin", occupants.Occupants[0].Name);
        Assert.True(occupants.Occupants[0].IsAdmin);
        Assert.Equal(102, occupants.Occupants[1].UserId);
        Assert.Equal("Player", occupants.Occupants[1].Name);
        Assert.False(occupants.Occupants[1].IsAdmin);
    }

    /// <summary>
    /// Tests that incoming friend request payloads parse the applicant display name.
    /// </summary>
    [Fact]
    public void ReadIncomingFriendRequest_ValidPayload_ShouldParseCorrectly()
    {
        var payload = """{"display_name": "Rookie"}""";
        var request = GeneralsOnlineWebSocketListener.ReadIncomingFriendRequest(payload);

        Assert.NotNull(request);
        Assert.Equal("Rookie", request.DisplayName);
    }

    /// <summary>
    /// Tests that moderation notices parse action type and duration.
    /// </summary>
    [Fact]
    public void ReadModerationNotice_ValidPayload_ShouldParseCorrectly()
    {
        var payload = """{"action_type": "mute", "reason": "spam", "duration_seconds": 300}""";
        var notice = GeneralsOnlineWebSocketListener.ReadModerationNotice(payload);

        Assert.NotNull(notice);
        Assert.Equal("mute", notice.ActionType);
        Assert.Equal("spam", notice.Reason);
    }

    /// <summary>
    /// Tests that DisconnectAsync and DisposeAsync can be called concurrently without unhandled ObjectDisposedException.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task DisconnectAndDispose_ConcurrentExecution_ShouldNotThrowAsync()
    {
        // Arrange
        var listener = CreateListener();
        var connectResult = await listener.ConnectAsync("ws://127.0.0.1:9/", "session-token");
        Assert.True(connectResult.Success);

        // Act & Assert: run disconnect and dispose concurrently
        var disconnectTask = Task.Run(async () => await listener.DisconnectAsync());
        var disposeTask = Task.Run(async () => await listener.DisposeAsync());

        await Task.WhenAll(disconnectTask, disposeTask.AsTask());
        Assert.False(listener.IsConnected);
    }

    private static GeneralsOnlineWebSocketListener CreateListener()
    {
        return new GeneralsOnlineWebSocketListener(Mock.Of<ILogger<GeneralsOnlineWebSocketListener>>());
    }
}
