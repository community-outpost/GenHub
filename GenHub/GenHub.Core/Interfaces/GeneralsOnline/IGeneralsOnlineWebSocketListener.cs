using GenHub.Core.Models.Results;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.GeneralsOnline;

/// <summary>
/// Listens to Generals Online WebSocket hint opcodes. The backend sends compact
/// notifications instead of state dumps; each hint triggers a REST re-fetch.
/// </summary>
public interface IGeneralsOnlineWebSocketListener : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets a value indicating whether the listener loop is active, including
    /// reconnect backoff. True means <c>DisconnectAsync</c> must be called to
    /// stop authenticated traffic; it does not guarantee an open socket.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets a value indicating whether the underlying WebSocket is currently open.
    /// </summary>
    bool IsSocketOpen { get; }

    /// <summary>
    /// Occurs when the lobby list changed and should be re-fetched.
    /// </summary>
    event EventHandler? LobbyListChanged;

    /// <summary>
    /// Occurs when the currently viewed lobby changed and should be re-fetched.
    /// </summary>
    event EventHandler? CurrentLobbyChanged;

    /// <summary>
    /// Occurs when a network room chat message arrives.
    /// </summary>
    event EventHandler<Models.GeneralsOnline.GeneralsOnlineRoomChatMessage>? RoomChatReceived;

    /// <summary>
    /// Occurs when the friends list changed and should be re-fetched.
    /// </summary>
    event EventHandler? FriendsChanged;

    /// <summary>
    /// Occurs when a friend direct message arrives.
    /// </summary>
    event EventHandler<Models.GeneralsOnline.GeneralsOnlineFriendChatMessage>? FriendChatReceived;

    /// <summary>
    /// Occurs when a friend's online status changes.
    /// </summary>
    event EventHandler<Models.GeneralsOnline.GeneralsOnlineFriendPresence>? FriendPresenceChanged;

    /// <summary>
    /// Occurs when the network room occupant list is pushed.
    /// </summary>
    event EventHandler<Models.GeneralsOnline.GeneralsOnlineRoomMemberList>? RoomOccupantsChanged;

    /// <summary>
    /// Occurs when a new friend request arrives.
    /// </summary>
    event EventHandler<Models.GeneralsOnline.GeneralsOnlineIncomingFriendRequest>? NewFriendRequestReceived;

    /// <summary>
    /// Occurs when a moderation notice is pushed to the session.
    /// </summary>
    event EventHandler<Models.GeneralsOnline.GeneralsOnlineModerationNotice>? ModerationNoticeReceived;

    /// <summary>
    /// Connects to the session WebSocket URI.
    /// </summary>
    /// <param name="webSocketUri">The session WebSocket URI.</param>
    /// <param name="sessionToken">The session token for authorization.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the listener loop started.</returns>
    Task<OperationResult<bool>> ConnectAsync(string webSocketUri, string sessionToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects the listener.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the listener stopped.</returns>
    Task<OperationResult<bool>> DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Selects the backend network room used for lobby filtering. The show-all
    /// room is required: sessions without a selected room see no lobbies.
    /// </summary>
    /// <param name="roomId">The room id to select.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the selection was sent.</returns>
    Task<OperationResult<bool>> SelectNetworkRoomAsync(short roomId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a chat message to the selected network room.
    /// </summary>
    /// <param name="message">The chat message text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the message was sent.</returns>
    Task<OperationResult<bool>> SendRoomChatAsync(string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a direct message to an online friend.
    /// </summary>
    /// <param name="targetUserId">The recipient user id.</param>
    /// <param name="message">The chat message text.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the message was sent.</returns>
    Task<OperationResult<bool>> SendFriendChatAsync(long targetUserId, string message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes the session to realtime social updates.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success when the subscription was sent.</returns>
    Task<OperationResult<bool>> SubscribeSocialAsync(CancellationToken cancellationToken = default);
}
