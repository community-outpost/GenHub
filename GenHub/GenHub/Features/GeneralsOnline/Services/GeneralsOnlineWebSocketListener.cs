using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Core.Models.Results;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Listens to Generals Online WebSocket hint opcodes and raises re-fetch signals.
/// The backend sends compact msg_id notifications instead of state dumps.
/// </summary>
/// <param name="logger">The logger instance.</param>
public sealed class GeneralsOnlineWebSocketListener(ILogger<GeneralsOnlineWebSocketListener> logger)
    : IGeneralsOnlineWebSocketListener
{
    private sealed record RoomSelectionMessage(
        [property: JsonPropertyName("msg_id")] int MessageId,
        [property: JsonPropertyName("room")] short Room);

    private sealed record RoomChatSendMessage(
        [property: JsonPropertyName("msg_id")] int MessageId,
        [property: JsonPropertyName("message")] string Message,
        [property: JsonPropertyName("action")] bool Action);

    private sealed record FriendChatSendMessage(
        [property: JsonPropertyName("msg_id")] int MessageId,
        [property: JsonPropertyName("target_user_id")] long TargetUserId,
        [property: JsonPropertyName("message")] string Message);

    private sealed record SocialSubscribeMessage(
        [property: JsonPropertyName("msg_id")] int MessageId);

    private readonly SemaphoreSlim _stateLock = new(1, 1);
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _syncLock = new();
    private readonly byte[] _receiveBuffer = new byte[8192];

    private ClientWebSocket? _socket;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private string _webSocketUri = string.Empty;
    private string _sessionToken = string.Empty;
    private short? _selectedRoomId;
    private bool _disposed;

    /// <inheritdoc />
    public bool IsConnected
    {
        get
        {
            lock (_syncLock)
            {
                return _loopTask is { IsCompleted: false };
            }
        }
    }

    /// <inheritdoc />
    public bool IsSocketOpen
    {
        get
        {
            lock (_syncLock)
            {
                return _socket?.State == WebSocketState.Open;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? LobbyListChanged;

    /// <inheritdoc />
    public event EventHandler? CurrentLobbyChanged;

    /// <inheritdoc />
    public event EventHandler<GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomChatMessage>? RoomChatReceived;

    /// <inheritdoc />
    public event EventHandler? FriendsChanged;

    /// <inheritdoc />
    public event EventHandler<GenHub.Core.Models.GeneralsOnline.GeneralsOnlineFriendChatMessage>? FriendChatReceived;

    /// <inheritdoc />
    public event EventHandler<GenHub.Core.Models.GeneralsOnline.GeneralsOnlineFriendPresence>? FriendPresenceChanged;

    /// <inheritdoc />
    public event EventHandler<GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomMemberList>? RoomOccupantsChanged;

    /// <inheritdoc />
    public event EventHandler<GenHub.Core.Models.GeneralsOnline.GeneralsOnlineIncomingFriendRequest>? NewFriendRequestReceived;

    /// <inheritdoc />
    public event EventHandler<GenHub.Core.Models.GeneralsOnline.GeneralsOnlineModerationNotice>? ModerationNoticeReceived;

    /// <inheritdoc />
    public async Task<OperationResult<bool>> ConnectAsync(
        string webSocketUri,
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webSocketUri) || string.IsNullOrWhiteSpace(sessionToken))
        {
            return OperationResult<bool>.CreateFailure("WebSocket URI and session token are required.");
        }

        if (!Uri.TryCreate(webSocketUri, UriKind.Absolute, out var parsedUri)
            || (parsedUri.Scheme != Uri.UriSchemeWs && parsedUri.Scheme != Uri.UriSchemeWss))
        {
            logger.LogWarning("Generals Online WebSocket URI is invalid.");
            return OperationResult<bool>.CreateFailure("WebSocket URI is invalid.");
        }

        ThrowIfDisposed();
        try
        {
            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return OperationResult<bool>.CreateFailure("Listener has been disposed.");
        }

        try
        {
            ThrowIfDisposed();
            await StopLoopAsync().ConfigureAwait(false);

            _webSocketUri = webSocketUri;
            _sessionToken = sessionToken;

            // The loop owns an independent source: linking the short-lived
            // caller token would let one cancelled connect disable live
            // updates until the next explicit connect.
            _loopCts = new CancellationTokenSource();
            _loopTask = RunLoopAsync(_loopCts.Token);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            try
            {
                _stateLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while connect was in flight.
            }
        }
    }

    /// <inheritdoc />
    public async Task<OperationResult<bool>> DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        try
        {
            await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return OperationResult<bool>.CreateSuccess(true);
        }

        try
        {
            await StopLoopAsync().ConfigureAwait(false);
            return OperationResult<bool>.CreateSuccess(true);
        }
        finally
        {
            try
            {
                _stateLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while disconnect was in flight.
            }
        }
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SelectNetworkRoomAsync(short roomId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        lock (_syncLock)
        {
            _selectedRoomId = roomId;
        }

        return SendPayloadAsync(
            BuildRoomSelectionPayload(roomId),
            "room selection",
            "Network room selection requires an open connection.",
            "Network room selection failed.",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SendRoomChatAsync(string message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure("Chat message is required."));
        }

        var payload = JsonSerializer.Serialize(new RoomChatSendMessage(
            GeneralsOnlineConstants.WebSocketRoomChatSendId,
            TruncateChat(message),
            false));
        return SendPayloadAsync(
            payload,
            "room chat send",
            "Room chat requires an open connection.",
            "Room chat send failed.",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SendFriendChatAsync(long targetUserId, string message, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (targetUserId <= 0)
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure("A friend must be selected."));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Task.FromResult(OperationResult<bool>.CreateFailure("Chat message is required."));
        }

        var payload = JsonSerializer.Serialize(new FriendChatSendMessage(
            GeneralsOnlineConstants.WebSocketFriendChatSendId,
            targetUserId,
            TruncateChat(message)));
        return SendPayloadAsync(
            payload,
            "friend chat send",
            "Friend chat requires an open connection.",
            "Friend chat send failed.",
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<OperationResult<bool>> SubscribeSocialAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var payload = JsonSerializer.Serialize(new SocialSubscribeMessage(GeneralsOnlineConstants.WebSocketSocialSubscribeId));
        return SendPayloadAsync(
            payload,
            "social subscribe",
            "Social updates require an open connection.",
            "Social subscribe failed.",
            cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_syncLock)
        {
            _selectedRoomId = null;
        }

        if (_loopCts is not null)
        {
            try
            {
                _loopCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }

            try
            {
                _loopCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _socket?.Dispose();
        _stateLock.Dispose();
        _sendLock.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        lock (_syncLock)
        {
            _selectedRoomId = null;
        }

        if (_loopCts is not null)
        {
            try
            {
                await _loopCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Already disposed.
            }

            try
            {
                _loopCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently.
            }

            _loopTask = null;
        }

        _socket?.Dispose();
        _stateLock.Dispose();
        _sendLock.Dispose();
    }

    /// <summary>
    /// Determines whether appending a frame would exceed the message byte budget.
    /// </summary>
    /// <param name="totalBytes">The bytes accumulated so far.</param>
    /// <param name="frameCount">The incoming frame size in bytes.</param>
    /// <returns>True when the frame would exceed the budget.</returns>
    internal static bool ExceedsMaxMessageBytes(int totalBytes, int frameCount)
    {
        return totalBytes + frameCount > GeneralsOnlineConstants.WebSocketMaxMessageBytes;
    }

    /// <summary>
    /// Builds the room-change payload the backend parses for lobby filtering.
    /// Keys are lowercase: the backend looks up the room field case-sensitively.
    /// </summary>
    /// <param name="roomId">The room id to select.</param>
    /// <returns>The JSON payload to send.</returns>
    internal static string BuildRoomSelectionPayload(short roomId)
    {
        return JsonSerializer.Serialize(
            new RoomSelectionMessage(GeneralsOnlineConstants.WebSocketNetworkRoomChangeId, roomId));
    }

    /// <summary>
    /// Reads the hint message id from a WebSocket payload, ignoring non-object JSON.
    /// </summary>
    /// <param name="payload">The WebSocket message payload.</param>
    /// <returns>The message id, or null when the payload carries none.</returns>
    internal static int? ReadMessageId(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("msg_id", out var id)
                && id.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Reads a network room chat message from a WebSocket payload.
    /// </summary>
    /// <param name="payload">The WebSocket message payload.</param>
    /// <returns>The chat message, or null when the payload carries none.</returns>
    internal static GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomChatMessage? ReadRoomChat(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            var message = root.TryGetProperty("message", out var messageProp) ? messageProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            var admin = root.TryGetProperty("admin", out var adminProp) && adminProp.ValueKind == JsonValueKind.True;
            var action = root.TryGetProperty("action", out var actionProp) && actionProp.ValueKind == JsonValueKind.True;
            return new GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomChatMessage
            {
                Message = message,
                IsAdmin = admin,
                IsAction = action,
                ReceivedAtUtc = DateTime.UtcNow,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a friend direct message from a WebSocket payload.
    /// </summary>
    /// <param name="payload">The WebSocket message payload.</param>
    /// <returns>The direct message, or null when the payload carries none.</returns>
    internal static GenHub.Core.Models.GeneralsOnline.GeneralsOnlineFriendChatMessage? ReadFriendChat(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            var message = root.TryGetProperty("message", out var messageProp) ? messageProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(message))
            {
                return null;
            }

            return new GenHub.Core.Models.GeneralsOnline.GeneralsOnlineFriendChatMessage
            {
                SourceUserId = root.TryGetProperty("source_user_id", out var sourceProp) && sourceProp.TryGetInt64(out var source) ? source : -1,
                TargetUserId = root.TryGetProperty("target_user_id", out var targetProp) && targetProp.TryGetInt64(out var target) ? target : -1,
                Message = message,
                ReceivedAtUtc = DateTime.UtcNow,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a friend presence change from a WebSocket payload.
    /// </summary>
    /// <param name="payload">The WebSocket message payload.</param>
    /// <returns>The presence change, or null when the payload carries none.</returns>
    internal static GenHub.Core.Models.GeneralsOnline.GeneralsOnlineFriendPresence? ReadFriendPresence(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            var displayName = root.TryGetProperty("display_name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return null;
            }

            long userId = -1;
            if (root.TryGetProperty("user_id", out var uidProp) && uidProp.TryGetInt64(out var parsedUid))
            {
                userId = parsedUid;
            }

            return new GenHub.Core.Models.GeneralsOnline.GeneralsOnlineFriendPresence
            {
                UserId = userId,
                DisplayName = displayName,
                IsOnline = root.TryGetProperty("online", out var onlineProp) && onlineProp.ValueKind == JsonValueKind.True,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a room occupant list from a WebSocket payload.
    /// </summary>
    /// <param name="payload">The WebSocket message payload.</param>
    /// <returns>The occupant list, or null when the payload carries none.</returns>
    internal static GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomMemberList? ReadRoomOccupants(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("members", out var membersProp)
                || membersProp.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var occupants = new List<GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomOccupant>();
            foreach (var element in membersProp.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var name = element.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                occupants.Add(new GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomOccupant
                {
                    UserId = element.TryGetProperty("UserID", out var idProp) && idProp.TryGetInt64(out var id) ? id : -1,
                    Name = name,
                    IsAdmin = element.TryGetProperty("IsAdmin", out var adminProp) && adminProp.ValueKind == JsonValueKind.True,
                });
            }

            return new GenHub.Core.Models.GeneralsOnline.GeneralsOnlineRoomMemberList { Occupants = occupants };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads an incoming friend request from a WebSocket payload.
    /// </summary>
    /// <param name="payload">The WebSocket message payload.</param>
    /// <returns>The request, or null when the payload carries none.</returns>
    internal static GenHub.Core.Models.GeneralsOnline.GeneralsOnlineIncomingFriendRequest? ReadIncomingFriendRequest(string payload)
    {
        var presence = ReadFriendPresence(payload);
        return presence is null
            ? null
            : new GenHub.Core.Models.GeneralsOnline.GeneralsOnlineIncomingFriendRequest { DisplayName = presence.DisplayName };
    }

    /// <summary>
    /// Reads a moderation notice from a WebSocket payload.
    /// </summary>
    /// <param name="payload">The WebSocket message payload.</param>
    /// <returns>The notice, or null when the payload carries none.</returns>
    internal static GenHub.Core.Models.GeneralsOnline.GeneralsOnlineModerationNotice? ReadModerationNotice(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var root = document.RootElement;
            var action = root.TryGetProperty("action_type", out var actionProp) ? actionProp.GetString() ?? string.Empty : string.Empty;
            var reason = root.TryGetProperty("reason", out var reasonProp) ? reasonProp.GetString() ?? string.Empty : string.Empty;
            if (string.IsNullOrWhiteSpace(action) && string.IsNullOrWhiteSpace(reason))
            {
                return null;
            }

            return new GenHub.Core.Models.GeneralsOnline.GeneralsOnlineModerationNotice
            {
                ActionType = action,
                Reason = reason,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string TruncateChat(string message)
    {
        var text = message.Trim();
        return text.Length > GeneralsOnlineConstants.RoomChatMaxLength
            ? text[..GeneralsOnlineConstants.RoomChatMaxLength]
            : text;
    }

    private async Task<ClientWebSocket?> WaitForOpenSocketAsync(CancellationToken cancellationToken)
    {
        // ConnectAsync returns once the loop starts, so the socket may not be
        // open yet when room selection follows immediately after connecting.
        var deadline = DateTime.UtcNow.AddMilliseconds(GeneralsOnlineConstants.WebSocketRoomSelectTimeoutMs);
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket;
            lock (_syncLock)
            {
                socket = _socket;
            }

            if (socket is not null && socket.State == WebSocketState.Open)
            {
                return socket;
            }

            await Task.Delay(GeneralsOnlineConstants.WebSocketOpenPollIntervalMs, cancellationToken).ConfigureAwait(false);
        }

        lock (_syncLock)
        {
            return _socket is not null && _socket.State == WebSocketState.Open ? _socket : null;
        }
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        var delaySeconds = OnlineConstants.ReconnectInitialDelaySeconds;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConnectSocketAsync(cancellationToken).ConfigureAwait(false);
                delaySeconds = OnlineConstants.ReconnectInitialDelaySeconds;
                await ReplaySelectedRoomAsync(cancellationToken).ConfigureAwait(false);
                await ReceiveLoopAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException ex)
            {
                logger.LogDebug(ex, "Generals Online WebSocket listener disposed during loop run.");
                break;
            }
            catch (WebSocketException ex)
            {
                logger.LogWarning(ex, "Generals Online WebSocket dropped; retrying in {Delay}s.", delaySeconds);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Generals Online WebSocket dropped; retrying in {Delay}s.", delaySeconds);
            }
            catch (InvalidOperationException ex)
            {
                logger.LogWarning(ex, "Generals Online WebSocket dropped; retrying in {Delay}s.", delaySeconds);
            }

            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            delaySeconds = Math.Min(delaySeconds * 2, OnlineConstants.ReconnectMaxDelaySeconds);
        }
    }

    private async Task ReplaySelectedRoomAsync(CancellationToken cancellationToken)
    {
        short? roomId;
        lock (_syncLock)
        {
            roomId = _selectedRoomId;
        }

        if (roomId.HasValue)
        {
            var result = await SendPayloadAsync(
                BuildRoomSelectionPayload(roomId.Value),
                "room re-selection",
                "Network room selection requires an open connection.",
                "Network room selection failed.",
                cancellationToken).ConfigureAwait(false);
            if (!result.Success)
            {
                logger.LogWarning("Room re-selection replay failed for room {RoomId}: {Error}", roomId.Value, result.Errors.Count > 0 ? result.Errors[0] : null);
            }
        }
    }

    private async Task ConnectSocketAsync(CancellationToken cancellationToken)
    {
        DisposeSocket();

        var socket = new ClientWebSocket();
        try
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {_sessionToken}");
            await socket.ConnectAsync(new Uri(_webSocketUri), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        lock (_syncLock)
        {
            _socket = socket;
        }

        logger.LogInformation("Connected to the Generals Online WebSocket.");
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        // Buffer raw bytes: a multi-byte UTF-8 character can split across
        // frames, and decoding each frame separately would corrupt it.
        using var message = new MemoryStream();
        var totalBytes = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            ClientWebSocket? socket;
            lock (_syncLock)
            {
                socket = _socket;
            }

            if (socket is null || socket.State != WebSocketState.Open)
            {
                break;
            }

            WebSocketReceiveResult? frame = null;
            try
            {
                frame = await socket.ReceiveAsync(_receiveBuffer, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }

            if (frame is null || frame.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            if (ExceedsMaxMessageBytes(totalBytes, frame.Count))
            {
                logger.LogWarning("Generals Online WebSocket message exceeded the size limit; closing the connection.");
                await CloseOversizedSocketAsync(socket, cancellationToken);
                break;
            }

            totalBytes += frame.Count;
            await message.WriteAsync(_receiveBuffer.AsMemory(0, frame.Count), cancellationToken).ConfigureAwait(false);
            if (!frame.EndOfMessage)
            {
                continue;
            }

            var payload = Encoding.UTF8.GetString(message.ToArray());
            message.SetLength(0);
            totalBytes = 0;

            DispatchMessage(payload);
        }
    }

    // skipcq: CS-R1008
    private void DispatchMessage(string payload)
    {
        var messageId = ReadMessageId(payload);
        try
        {
            switch (messageId)
            {
                case GeneralsOnlineConstants.WebSocketLobbyListUpdateId:
                    LobbyListChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case GeneralsOnlineConstants.WebSocketCurrentLobbyUpdateId:
                    CurrentLobbyChanged?.Invoke(this, EventArgs.Empty);
                    break;
                case GeneralsOnlineConstants.WebSocketRoomChatReceiveId:
                case GeneralsOnlineConstants.WebSocketRoomMembersUpdateId:
                    DispatchRoomMessage(messageId.Value, payload);
                    break;
                case GeneralsOnlineConstants.WebSocketFriendChatReceiveId:
                case GeneralsOnlineConstants.WebSocketFriendPresenceId:
                case GeneralsOnlineConstants.WebSocketFriendRequestId:
                case GeneralsOnlineConstants.WebSocketFriendsDirtyId:
                case GeneralsOnlineConstants.WebSocketFriendsStatusId:
                case GeneralsOnlineConstants.WebSocketFriendRequestAcceptedId:
                case GeneralsOnlineConstants.WebSocketModerationNoticeId:
                    DispatchSocialMessage(messageId.Value, payload);
                    break;
                default:
                    logger.LogTrace("Ignored unhandled WebSocket message {MessageId}.", messageId);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Subscriber code runs on the socket loop; a throwing handler must
            // not fault the loop and stop reconnects. This is the worker
            // boundary for untrusted event subscribers.
            logger.LogWarning(ex, "Generals Online WebSocket subscriber failed.");
        }
    }

    private void DispatchRoomMessage(int messageId, string payload)
    {
        if (messageId == GeneralsOnlineConstants.WebSocketRoomChatReceiveId)
        {
            var chat = ReadRoomChat(payload);
            if (chat is not null)
            {
                RoomChatReceived?.Invoke(this, chat);
            }
        }
        else if (messageId == GeneralsOnlineConstants.WebSocketRoomMembersUpdateId)
        {
            var members = ReadRoomOccupants(payload);
            if (members is not null)
            {
                RoomOccupantsChanged?.Invoke(this, members);
            }
        }
    }

    private void DispatchSocialMessage(int messageId, string payload)
    {
        switch (messageId)
        {
            case GeneralsOnlineConstants.WebSocketFriendChatReceiveId:
                var chat = ReadFriendChat(payload);
                if (chat is not null)
                {
                    FriendChatReceived?.Invoke(this, chat);
                }

                break;
            case GeneralsOnlineConstants.WebSocketFriendPresenceId:
                var presence = ReadFriendPresence(payload);
                if (presence is not null)
                {
                    FriendPresenceChanged?.Invoke(this, presence);
                }

                FriendsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case GeneralsOnlineConstants.WebSocketFriendRequestId:
                var request = ReadIncomingFriendRequest(payload);
                if (request is not null)
                {
                    NewFriendRequestReceived?.Invoke(this, request);
                }

                FriendsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case GeneralsOnlineConstants.WebSocketFriendsDirtyId:
            case GeneralsOnlineConstants.WebSocketFriendsStatusId:
            case GeneralsOnlineConstants.WebSocketFriendRequestAcceptedId:
                FriendsChanged?.Invoke(this, EventArgs.Empty);
                break;
            case GeneralsOnlineConstants.WebSocketModerationNoticeId:
                var notice = ReadModerationNotice(payload);
                if (notice is not null)
                {
                    ModerationNoticeReceived?.Invoke(this, notice);
                }

                break;
            default:
                logger.LogTrace("Ignored unhandled WebSocket social message {MessageId}.", messageId);
                break;
        }
    }

    private async Task<OperationResult<bool>> SendPayloadAsync(
        string payload,
        string operation,
        string closedMessage,
        string failedMessage,
        CancellationToken cancellationToken)
    {
        var socket = await WaitForOpenSocketAsync(cancellationToken).ConfigureAwait(false);
        if (socket is null)
        {
            return OperationResult<bool>.CreateFailure(closedMessage);
        }

        try
        {
            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            return OperationResult<bool>.CreateFailure(closedMessage);
        }

        try
        {
            // The loop may have replaced the socket while waiting for the send lock.
            lock (_syncLock)
            {
                socket = _socket;
            }

            if (socket is null || socket.State != WebSocketState.Open)
            {
                return OperationResult<bool>.CreateFailure(closedMessage);
            }

            await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning(ex, "Generals Online {Operation} failed.", operation);
            return OperationResult<bool>.CreateFailure(failedMessage);
        }
        catch (ObjectDisposedException ex)
        {
            logger.LogWarning(ex, "Generals Online {Operation} failed.", operation);
            return OperationResult<bool>.CreateFailure(failedMessage);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "Generals Online {Operation} failed.", operation);
            return OperationResult<bool>.CreateFailure(failedMessage);
        }
        finally
        {
            try
            {
                _sendLock.Release();
            }
            catch (ObjectDisposedException)
            {
                // Disposed while send was completing.
            }
        }

        return OperationResult<bool>.CreateSuccess(true);
    }

    private async Task CloseOversizedSocketAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        try
        {
            if (socket.State == WebSocketState.Open)
            {
                await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown requested; the loop exits on the next token check.
        }
        catch (WebSocketException ex)
        {
            logger.LogDebug(ex, "Failed to close the oversized Generals Online WebSocket.");
        }
        catch (InvalidOperationException ex)
        {
            logger.LogDebug(ex, "Failed to close the oversized Generals Online WebSocket.");
        }
    }

    private async Task StopLoopAsync()
    {
        lock (_syncLock)
        {
            _selectedRoomId = null;
        }

        if (_loopCts is not null)
        {
            try
            {
                await _loopCts.CancelAsync().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently by Dispose()/DisposeAsync().
            }

            try
            {
                _loopCts.Dispose();
            }
            catch (ObjectDisposedException)
            {
            }

            _loopCts = null;
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
            catch (ObjectDisposedException)
            {
                // Disposed concurrently during shutdown.
            }

            _loopTask = null;
        }

        DisposeSocket();
        _webSocketUri = string.Empty;
        _sessionToken = string.Empty;
    }

    private void DisposeSocket()
    {
        lock (_syncLock)
        {
            _socket?.Dispose();
            _socket = null;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
