using GenHub.Core.Constants;
using GenHub.Features.GeneralsOnline.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Net;
using System.Net.Http;
using System.Text;

namespace GenHub.Tests.Core.Features.GeneralsOnline;

/// <summary>
/// Unit tests for <see cref="GeneralsOnlineApiClient"/> request headers and failure mapping.
/// </summary>
public class GeneralsOnlineApiClientTests
{
    /// <summary>
    /// Tests that lobby requests identify as GenHub and carry the session token.
    /// The website and API edge reject requests without a user agent.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_ShouldSendUserAgentAndBearerTokenAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "lobbies": [], "latencies": [] }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal(ApiConstants.DefaultUserAgent, captured.Headers.UserAgent.ToString());
        Assert.Equal("Bearer", captured.Headers.Authorization?.Scheme);
        Assert.Equal("session-token", captured.Headers.Authorization?.Parameter);
    }

    /// <summary>
    /// Tests that a server error on the lobby list maps to the lobbies failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_OnServerError_ShouldReturnLobbiesUnavailableAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>proxy error</html>", Encoding.UTF8, "text/html"),
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLobbiesUnavailable, result.Errors);
    }

    /// <summary>
    /// Tests that the backend's access-denied shape maps to the forbidden code.
    /// The backend answers denied launcher sessions with 500 and an empty result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_OnEmptyResultBody_ShouldReturnLobbiesForbiddenAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"lobbies":null,"latencies":[],"playerlatencies":[]}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLobbiesForbidden, result.Errors);
    }

    /// <summary>
    /// Tests that a 200 lobby response without a lobby collection maps to the
    /// lobbies failure instead of succeeding with a null list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_OnNullLobbies_ShouldReturnLobbiesUnavailableAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => JsonResponse("""{ "lobbies": null }"""));
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLobbiesUnavailable, result.Errors);
    }

    /// <summary>
    /// Tests that a non-success login response with a JSON body is rejected
    /// instead of polling the empty payload as a pending login.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoginWithTokenAsync_OnServerErrorWithJsonBody_ShouldReturnServiceUnavailableAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        // Act
        var result = await client.LoginWithTokenAsync("refresh-token");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorServiceUnavailable, result.Errors);
    }

    /// <summary>
    /// Tests that an unauthorized login response maps to the login failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LoginWithTokenAsync_OnUnauthorized_ShouldReturnLoginFailedAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);

        // Act
        var result = await client.LoginWithTokenAsync("refresh-token");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLoginFailed, result.Errors);
    }

    /// <summary>
    /// Tests that a 500 JSON error object maps to the generic failure, not session-denied.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_OnServerErrorObject_ShouldReturnLobbiesUnavailableAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{ "error": "boom" }""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLobbiesUnavailable, result.Errors);
        Assert.DoesNotContain(GeneralsOnlineConstants.ErrorLobbiesForbidden, result.Errors);
    }

    /// <summary>
    /// Tests that the denial probe matches the lobbies key case-insensitively.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_OnUppercaseLobbies_ShouldReturnLobbiesForbiddenAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("""{"Lobbies":[],"latencies":[],"playerlatencies":[]}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLobbiesForbidden, result.Errors);
    }

    /// <summary>
    /// Tests that a 403 carrying the denial shape maps to the forbidden code.
    /// The backend may normalize the explicit 500 denial into a 403 later.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_OnForbiddenDenialBody_ShouldReturnLobbiesForbiddenAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{"lobbies":null,"latencies":[],"playerlatencies":[]}""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLobbiesForbidden, result.Errors);
    }

    /// <summary>
    /// Tests that a 403 error object without a lobby collection maps to the
    /// generic failure, not session-denied.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_OnForbiddenErrorObject_ShouldReturnLobbiesUnavailableAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("""{ "error": "boom" }""", Encoding.UTF8, "application/json"),
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetLobbiesAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorLobbiesUnavailable, result.Errors);
        Assert.DoesNotContain(GeneralsOnlineConstants.ErrorLobbiesForbidden, result.Errors);
    }

    /// <summary>
    /// Tests that caller cancellation propagates instead of mapping to a lobby failure.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetLobbiesAsync_WhenCallerCancels_ShouldPropagateAsync()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var handler = new RecordingHandler(_ => JsonResponse("""{ "lobbies": [], "latencies": [] }"""));
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetLobbiesAsync(cts.Token));
    }

    /// <summary>
    /// Tests that public count requests identify as GenHub and need no auth.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetPublicCountsAsync_ShouldSendUserAgentAndParseCountsAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{"players":1736,"lobbies":446}""");
        });
        var client = CreateClient(handler);

        // Act
        var result = await client.GetPublicCountsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(captured);
        Assert.Equal(ApiConstants.DefaultUserAgent, captured.Headers.UserAgent.ToString());
        Assert.Null(captured.Headers.Authorization);
        Assert.NotNull(result.Data);
        Assert.Equal(446, result.Data.Lobbies);
        Assert.Equal(1736, result.Data.Players);
    }

    /// <summary>
    /// Tests that a basic stats timeout maps to the service failure instead of throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetPublicCountsAsync_OnTimeout_ShouldReturnServiceUnavailableAsync()
    {
        // Arrange: HttpClient timeouts surface as TaskCanceledException with
        // the caller token uncancelled.
        var handler = new RecordingHandler(_ => throw new TaskCanceledException("Simulated timeout."));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetPublicCountsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorServiceUnavailable, result.Errors);
    }

    /// <summary>
    /// Tests that the unauthenticated uptime endpoint parses without a token.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetServiceUptimeAsync_ShouldParseUptimeWithoutTokenAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "start_time": "2026-09-26 01:24:45", "uptime": "Days: 2, Hours: 0, Minutes: 59" }""");
        });
        var client = CreateClient(handler);

        // Act
        var result = await client.GetServiceUptimeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("2026-09-26 01:24:45", result.Data!.StartTime);
        Assert.Equal("Days: 2, Hours: 0, Minutes: 59", result.Data.Uptime);
        Assert.Null(captured!.Headers.Authorization);
    }

    /// <summary>
    /// Tests that global stats carry the session token and aggregate matches.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetGlobalStatsAsync_ShouldSendBearerAndAggregateMatchesAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "globalstats": { "matches": [10, 5, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], "wins": [7, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0] } }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetGlobalStatsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(15, result.Data!.TotalMatches);
        Assert.Equal(9, result.Data.TotalWins);
        Assert.Equal("session-token", captured!.Headers.Authorization?.Parameter);
    }

    /// <summary>
    /// Tests that global stats without a session token map to auth required.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetGlobalStatsAsync_WithoutToken_ShouldRequireAuthAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => JsonResponse("""{ "globalstats": { "matches": [], "wins": [] } }"""));
        var client = CreateClient(handler);

        // Act
        var result = await client.GetGlobalStatsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorAuthRequired, result.Errors);
    }

    /// <summary>
    /// Tests that player stats request the given user id and parse career totals.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetPlayerStatsAsync_ShouldParseCareerStatsForUserAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "stats": { "userID": 1052, "EloRating": 1450, "EloMatches": 31, "MonthlyEloRating": 1420, "wins": [12, 8], "losses": [6, 5], "games": [18, 13] } }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetPlayerStatsAsync(1052);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1450, result.Data!.EloRating);
        Assert.Equal(20, result.Data.TotalWins);
        Assert.Equal(11, result.Data.TotalLosses);
        Assert.EndsWith("/PlayerStats/1052", captured!.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Tests that player stats with an invalid user id fail without a request.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetPlayerStatsAsync_WithInvalidUserId_ShouldFailAsync()
    {
        // Arrange
        var calls = 0;
        var handler = new RecordingHandler(_ =>
        {
            calls++;
            return JsonResponse("""{ "stats": null }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetPlayerStatsAsync(0);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, calls);
    }

    /// <summary>
    /// Tests that room requests carry the session token and parse the room list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetRoomsAsync_ShouldSendBearerAndParseRoomsAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "rooms": [{ "id": 0, "name": "ALL GAMES", "parent_id": null, "flags": 1 }], "supports_moderation_commands": true }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetRoomsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("session-token", captured!.Headers.Authorization?.Parameter);
        var room = Assert.Single(result.Data!);
        Assert.Equal(0, room.Id);
        Assert.Equal("ALL GAMES", room.Name);
        Assert.Null(room.ParentId);
        Assert.Equal(1, room.Flags);
    }

    /// <summary>
    /// Tests that room requests without a session token map to auth required.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetRoomsAsync_WithoutToken_ShouldRequireAuthAsync()
    {
        // Arrange
        var calls = 0;
        var handler = new RecordingHandler(_ =>
        {
            calls++;
            return JsonResponse("""{ "rooms": [] }""");
        });
        var client = CreateClient(handler);

        // Act
        var result = await client.GetRoomsAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(GeneralsOnlineConstants.ErrorAuthRequired, result.Errors);
        Assert.Equal(0, calls);
    }

    /// <summary>
    /// Tests that the message of the day is trimmed of surrounding whitespace.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetMotdAsync_ShouldTrimMessageAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => JsonResponse("""{ "MOTD": "  Welcome, 459 commanders online!  " }"""));
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetMotdAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Welcome, 459 commanders online!", result.Data);
    }

    /// <summary>
    /// Tests that a missing or null MOTD property returns failure rather than throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetMotdAsync_WhenMotdIsNull_ShouldReturnFailureAsync()
    {
        // Arrange
        var handler = new RecordingHandler(_ => JsonResponse("""{ "MOTD": null }"""));
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetMotdAsync();

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that the friends request sends the bearer token and parses friends with pending requests.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetFriendsAsync_ShouldSendBearerAndParseFriendsAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "friends": [{ "user_id": 7, "display_name": "Buddy", "online": true, "presence": "In lobby" }], "pending_requests": [{ "user_id": 8, "display_name": "Stranger", "online": false, "presence": "" }] }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetFriendsAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("session-token", captured!.Headers.Authorization?.Parameter);
        var friend = Assert.Single(result.Data!.Friends);
        Assert.Equal(7, friend.UserId);
        Assert.Equal("Buddy", friend.DisplayName);
        Assert.True(friend.IsOnline);
        Assert.Single(result.Data.PendingRequests);
    }

    /// <summary>
    /// Tests that the active users request parses the user list.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetActiveUsersAsync_ShouldParseActiveUsersAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "active_users": [{ "name": "General", "status": "In lobby", "duration": "5 minutes" }] }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetActiveUsersAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal("session-token", captured?.Headers.Authorization?.Parameter);
        var user = Assert.Single(result.Data!.ActiveUsers);
        Assert.Equal("General", user.Name);
        Assert.Equal("In lobby", user.Status);
    }

    /// <summary>
    /// Tests that current user endpoint parses the authenticated user details.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetMeAsync_ShouldParseCurrentUserAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "user_id": 42, "display_name": "Commander", "email": "c@example.com" }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.GetMeAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Equal(42, result.Data!.UserId);
        Assert.Equal("Commander", result.Data.DisplayName);
        Assert.EndsWith("/Users/Me", captured!.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that sending a friend request issues a PUT to the request endpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task SendFriendRequestAsync_ShouldSendPutToEndpointAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "success": true }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.SendFriendRequestAsync(99);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.EndsWith("/Social/Friends/Requests/99", captured.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that accepting a friend request issues a POST to the request endpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task AcceptFriendRequestAsync_ShouldSendPostToEndpointAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "success": true }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.AcceptFriendRequestAsync(99);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Post, captured!.Method);
        Assert.EndsWith("/Social/Friends/Requests/99", captured.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that rejecting a friend request issues a DELETE to the request endpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RejectFriendRequestAsync_ShouldSendDeleteToEndpointAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "success": true }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.RejectFriendRequestAsync(99);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.EndsWith("/Social/Friends/Requests/99", captured.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that removing a friend issues a DELETE to the friend endpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task RemoveFriendAsync_ShouldSendDeleteToEndpointAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "success": true }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.RemoveFriendAsync(99);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.EndsWith("/Social/Friends/99", captured.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that blocking a user issues a PUT to the blocked endpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task BlockUserAsync_ShouldSendPutToEndpointAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "success": true }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.BlockUserAsync(99);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Put, captured!.Method);
        Assert.EndsWith("/Social/Blocked/99", captured.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Tests that unblocking a user issues a DELETE to the blocked endpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UnblockUserAsync_ShouldSendDeleteToEndpointAsync()
    {
        // Arrange
        HttpRequestMessage? captured = null;
        var handler = new RecordingHandler(request =>
        {
            captured = request;
            return JsonResponse("""{ "success": true }""");
        });
        var client = CreateClient(handler);
        client.SetTokenProvider(() => Task.FromResult<string?>("session-token"));

        // Act
        var result = await client.UnblockUserAsync(99);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(HttpMethod.Delete, captured!.Method);
        Assert.EndsWith("/Social/Blocked/99", captured.RequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static GeneralsOnlineApiClient CreateClient(RecordingHandler handler)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(new HttpClient(handler));
        return new GeneralsOnlineApiClient(
            factory.Object,
            Mock.Of<ILogger<GeneralsOnlineApiClient>>());
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_responder(request));
        }
    }
}
