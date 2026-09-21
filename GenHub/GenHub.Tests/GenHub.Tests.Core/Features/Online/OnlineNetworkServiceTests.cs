using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Online;
using GenHub.Core.Models.Online;
using GenHub.Features.Online.Services;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace GenHub.Tests.Core.Features.Online;

/// <summary>
/// Unit tests for <see cref="OnlineNetworkService"/>.
/// </summary>
public sealed class OnlineNetworkServiceTests
{
    private const string SessionJson = """{"token":"test-session-token"}""";

    private const string DirectoryJsonTemplate = """
        [
          {"id":"net-1","name":"Zero Hour EU","tags":["zerohour"],"slotsUsed":3,"slotsMax":8,
           "region":"EU","hostDisplayName":"Commander","quality":1,"requiresPassword":true,
           "lastHeartbeatUtc":"FRESH_TIMESTAMP"}
        ]
        """;

    private const string StaleDirectoryJsonTemplate = """
        [
          {"id":"live","name":"Live Lobby","tags":[],"slotsUsed":2,"slotsMax":8,
           "region":"EU","hostDisplayName":"Host","quality":2,"requiresPassword":false,
           "lastHeartbeatUtc":"FRESH_TIMESTAMP"},
          {"id":"corpse","name":"Dead Lobby","tags":[],"slotsUsed":1,"slotsMax":8,
           "region":"EU","hostDisplayName":"Ghost","quality":1,"requiresPassword":true,
           "lastHeartbeatUtc":"STALE_TIMESTAMP"}
        ]
        """;

    private const string JoinJson = """
        {"networkId":"net-1","grant":"grant-token","grantExpiresUtc":"2026-09-20T01:00:00Z",
         "overlayIp":"10.42.0.7","adapterConfig":"opaque-config","expectedProfileId":"zh-1.04",
         "members":[{"displayName":"Commander","overlayIp":"10.42.0.7","quality":1,"isHost":true}]}
        """;

    /// <summary>
    /// Tests that the directory contract carries metadata only, never endpoints.
    /// </summary>
    [Fact]
    public void DirectoryContract_ShouldContainNoEndpointFields()
    {
        // Arrange: serialize the real DTOs with web defaults, as the edge serves them,
        // so a future endpoint-bearing property fails this test instead of a fixture.
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var summary = new OnlineNetworkSummary
        {
            Id = "net-1",
            Name = "Zero Hour EU",
            Tags = ["zerohour"],
            SlotsUsed = 3,
            SlotsMax = 8,
            Region = "EU",
            HostDisplayName = "Commander",
            Quality = OnlineConnectionQuality.Direct,
            RequiresPassword = true,
            LastHeartbeatUtc = new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
        };
        var detail = new OnlineNetworkDetail
        {
            Id = "net-1",
            Name = "Zero Hour EU",
            Description = "House rules",
            Tags = ["zerohour"],
            SlotsUsed = 3,
            SlotsMax = 8,
            ExpectedProfileId = "zh-1.04",
            RequiresPassword = true,
            HostPresent = true,
        };
        var forbidden = new[] { "endpoint", "candidate", "publicip", "underlay", "reflexive" };

        // Act
        var payloads = new[] { JsonSerializer.Serialize(summary, options), JsonSerializer.Serialize(detail, options) };
        var keys = new List<string>();
        foreach (var payload in payloads)
        {
            using var document = JsonDocument.Parse(payload);
            keys.AddRange(document.RootElement.EnumerateObject().Select(property => property.Name.ToLowerInvariant()));
        }

        // Assert
        Assert.NotEmpty(keys);
        Assert.DoesNotContain(keys, key => forbidden.Any(key.Contains));
    }

    /// <summary>
    /// Tests that the directory call returns parsed summaries.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetNetworksAsync_ShouldReturnSummariesAsync()
    {
        // Arrange
        var service = CreateService(CreateFactory());

        // Act
        var result = await service.GetNetworksAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Data);
        Assert.Equal("Zero Hour EU", result.Data[0].Name);
        Assert.Equal(3, result.Data[0].SlotsUsed);
    }

    /// <summary>
    /// Tests that an expired session is re-issued once and the call retried.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetNetworksAsync_WithExpiredSession_ShouldRetryOnceAsync()
    {
        // Arrange
        var sessionIssuances = 0;
        var directoryCalls = 0;
        var service = CreateService(CreateFactory(responder: request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal))
            {
                sessionIssuances++;
                return JsonResponse($$$"""{"token":"session-{{{sessionIssuances}}}"}""");
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/v1/networks", StringComparison.Ordinal))
            {
                directoryCalls++;
                if (directoryCalls == 1)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent(
                            """{"error":"Invalid session","code":"online.session-required"}""",
                            Encoding.UTF8,
                            "application/json"),
                    };
                }

                return JsonResponse(FreshDirectoryJson());
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        // Act
        var result = await service.GetNetworksAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Data);
        Assert.Equal(2, sessionIssuances);
        Assert.Equal(2, directoryCalls);
    }

    /// <summary>
    /// Tests that an HTTP timeout maps to a failure result instead of throwing.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetNetworksAsync_WithTimeout_ShouldReturnServiceUnavailableAsync()
    {
        // Arrange
        var service = CreateService(CreateFactory(responder: request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal))
            {
                return JsonResponse(SessionJson);
            }

            throw new TaskCanceledException("Simulated client timeout.");
        }));

        // Act
        var result = await service.GetNetworksAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorServiceUnavailable, result.Errors);
    }

    /// <summary>
    /// Tests that a malformed directory body maps to a failure result.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetNetworksAsync_WithMalformedBody_ShouldReturnServiceUnavailableAsync()
    {
        // Arrange
        var service = CreateService(CreateFactory(responder: request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal))
            {
                return JsonResponse(SessionJson);
            }

            return JsonResponse("not json{{{");
        }));

        // Act
        var result = await service.GetNetworksAsync();

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorServiceUnavailable, result.Errors);
    }

    /// <summary>
    /// Tests that directory corpses older than the stale window are dropped.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetNetworksAsync_WithStaleEntries_ShouldDropThemAsync()
    {
        // Arrange: one live lobby plus one corpse whose room died a day ago.
        var json = StaleDirectoryJsonTemplate
            .Replace("FRESH_TIMESTAMP", DateTime.UtcNow.ToString("O"), StringComparison.Ordinal)
            .Replace("STALE_TIMESTAMP", DateTime.UtcNow.AddDays(-1).ToString("O"), StringComparison.Ordinal);
        var service = CreateService(CreateFactory(responder: request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal))
            {
                return JsonResponse(SessionJson);
            }

            if (request.Method == HttpMethod.Get && path.EndsWith("/v1/networks", StringComparison.Ordinal))
            {
                return JsonResponse(json);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        // Act
        var result = await service.GetNetworksAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Single(result.Data);
        Assert.Equal("live", result.Data[0].Id);
    }

    /// <summary>
    /// Tests that a twice-rejected session surfaces as unavailable, not wrong password.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithRepeatedSessionRejection_ShouldReturnServiceUnavailableAsync()
    {
        // Arrange
        var joinCalls = 0;
        var service = CreateService(CreateFactory(responder: request =>
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (request.Method == HttpMethod.Post && path.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal))
            {
                return JsonResponse(SessionJson);
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/join", StringComparison.Ordinal))
            {
                joinCalls++;
                return new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent(
                        """{"error":"Invalid session","code":"online.session-required"}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }));

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret-password");

        // Assert
        Assert.False(result.Success);
        Assert.Equal(2, joinCalls);
        Assert.Contains(OnlineConstants.ErrorServiceUnavailable, result.Errors);
        Assert.DoesNotContain(OnlineConstants.ErrorWrongPassword, result.Errors);
    }

    /// <summary>
    /// Tests that a blank network id fails without HTTP traffic.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task GetNetworkDetailAsync_WithBlankId_ShouldFailAsync()
    {
        // Arrange
        var handler = new CountingHandler();
        var service = CreateService(CreateFactory(handler));

        // Act
        var result = await service.GetNetworkDetailAsync("  ");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorNetworkNotFound, result.Errors);
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>
    /// Tests that a 401 join maps to the wrong-password error code.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithUnauthorized_ShouldMapWrongPasswordAsync()
    {
        // Arrange
        var service = CreateService(CreateFactory(joinStatus: HttpStatusCode.Unauthorized));

        // Act
        var result = await service.JoinNetworkAsync("net-1", "wrong");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorWrongPassword, result.Errors);
    }

    /// <summary>
    /// Tests that a 409 join maps to the network-full error code.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithConflict_ShouldMapNetworkFullAsync()
    {
        // Arrange
        var service = CreateService(CreateFactory(joinStatus: HttpStatusCode.Conflict));

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorNetworkFull, result.Errors);
    }

    /// <summary>
    /// Tests that a successful join brings the adapter up and raises the roster.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_OnSuccess_ShouldBringUpAdapterAsync()
    {
        // Arrange
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        var service = CreateService(CreateFactory(), adapter.Object);
        IReadOnlyList<OnlineMember>? roster = null;
        service.RosterChanged += (_, members) => roster = members;

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret");

        // Assert
        Assert.True(result.Success);
        Assert.Equal("10.42.0.7", result.Data.OverlayIp);
        Assert.NotNull(service.CurrentJoin);
        adapter.Verify(a => a.BringUpAsync("opaque-config", "10.42.0.7", It.IsAny<CancellationToken>()), Times.Once);
        Assert.NotNull(roster);
        Assert.Single(roster);
    }

    /// <summary>
    /// Tests that adapter failure during join stays joined without tunneling and tears down.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WhenAdapterFails_ShouldStayJoinedWithoutTunnelingAsync()
    {
        // Arrange
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateFailure("no driver"));
        adapter.Setup(a => a.TearDownAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        adapter.SetupGet(a => a.State).Returns(OnlineAdapterState.Down);
        var service = CreateService(CreateFactory(), adapter.Object);

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret");

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(service.CurrentJoin);
        Assert.Equal(OnlineAdapterState.Down, service.AdapterState);
        adapter.Verify(a => a.TearDownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that an invalid create request fails without HTTP traffic.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateNetworkAsync_WithShortName_ShouldFailAsync()
    {
        // Arrange
        var handler = new CountingHandler();
        var service = CreateService(CreateFactory(handler));

        // Act
        var result = await service.CreateNetworkAsync(
            new OnlineCreateNetworkRequest { Name = "ab", SlotsMax = 8 });

        // Assert
        Assert.False(result.Success);
        Assert.Equal(0, handler.CallCount);
    }

    /// <summary>
    /// Tests that leaving tears the adapter down and clears the join.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task LeaveNetworkAsync_AfterJoin_ShouldTearDownAdapterAsync()
    {
        // Arrange
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        adapter.Setup(a => a.TearDownAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        var service = CreateService(CreateFactory(), adapter.Object);
        await service.JoinNetworkAsync("net-1", "secret");

        // Act
        var result = await service.LeaveNetworkAsync();

        // Assert
        Assert.True(result.Success);
        Assert.Null(service.CurrentJoin);
        adapter.Verify(a => a.TearDownAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that updating without a join fails.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task UpdateNetworkAsync_WhenNotJoined_ShouldFailAsync()
    {
        // Arrange
        var service = CreateService(CreateFactory());

        // Act
        var result = await service.UpdateNetworkAsync("desc", null);

        // Assert
        Assert.False(result.Success);
    }

    /// <summary>
    /// Tests that reporting and banning succeed after a join.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ReportAndBanMemberAsync_AfterJoin_ShouldSucceedAsync()
    {
        // Arrange
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        var service = CreateService(CreateFactory(), adapter.Object);
        await service.JoinNetworkAsync("net-1", "secret");

        // Act
        var report = await service.ReportMemberAsync("10.42.0.8", "Cheating");
        var ban = await service.BanMemberAsync("10.42.0.8");
        var update = await service.UpdateNetworkAsync("New desc", new OnlineExpectedProfile { ExpectedProfileId = "zh-1.04" });

        // Assert
        Assert.True(report.Success);
        Assert.True(ban.Success);
        Assert.True(update.Success);
    }

    /// <summary>
    /// Tests that a resolved reflexive endpoint is published on join.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithStunEndpoint_ShouldPublishAsync()
    {
        // Arrange
        var local = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000);
        var reflexive = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("203.0.113.7"), 4321);
        var p2p = new Mock<IP2PConnectionService>();
        p2p.Setup(p => p.StartListeningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<System.Net.IPEndPoint>.CreateSuccess(local));
        p2p.Setup(p => p.GetLocalAndPublicEndpointsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<P2PEndpoints>.CreateSuccess(new P2PEndpoints(local, reflexive)));
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        string? joinBody = null;
        var service = CreateService(CreateFactory(joinStatus: HttpStatusCode.OK, onJoinBody: body => joinBody = body), adapter.Object, p2p.Object);

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret", false);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("203.0.113.7:4321", joinBody ?? string.Empty);
        Assert.Equal("203.0.113.7:4321", service.LocalEndpoint);
    }

    /// <summary>
    /// Tests that relay mode hides the endpoint without STUN traffic.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithPreferRelay_ShouldHideEndpointAsync()
    {
        // Arrange
        var p2p = new Mock<IP2PConnectionService>(MockBehavior.Strict);
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        string? joinBody = null;
        var service = CreateService(CreateFactory(joinStatus: HttpStatusCode.OK, onJoinBody: body => joinBody = body), adapter.Object, p2p.Object);

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret", true);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("\"endpoint\":\"\"", joinBody ?? string.Empty);
        Assert.Equal(string.Empty, service.LocalEndpoint);
    }

    /// <summary>
    /// Tests that a failed join releases the UDP listener and clears the endpoint.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WhenJoinFails_ShouldReleaseListenerAsync()
    {
        // Arrange
        var local = new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 5000);
        var reflexive = new System.Net.IPEndPoint(System.Net.IPAddress.Parse("203.0.113.7"), 4321);
        var p2p = new Mock<IP2PConnectionService>();
        p2p.Setup(p => p.StartListeningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<System.Net.IPEndPoint>.CreateSuccess(local));
        p2p.Setup(p => p.GetLocalAndPublicEndpointsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<P2PEndpoints>.CreateSuccess(new P2PEndpoints(local, reflexive)));
        p2p.Setup(p => p.StopListeningAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        var adapter = new Mock<IVirtualLanAdapter>();
        var service = CreateService(CreateFactory(joinStatus: HttpStatusCode.Conflict), adapter.Object, p2p.Object);

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret", false);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(string.Empty, service.LocalEndpoint);
        p2p.Verify(p => p.StopListeningAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// Tests that creating with the default relay preference hides the endpoint without STUN traffic.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task CreateNetworkAsync_WithRelayDefault_ShouldHideEndpointAsync()
    {
        // Arrange
        var p2p = new Mock<IP2PConnectionService>(MockBehavior.Strict);
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        string? createBody = null;
        var service = CreateService(CreateFactory(onCreateBody: body => createBody = body), adapter.Object, p2p.Object);

        // Act
        var result = await service.CreateNetworkAsync(new OnlineCreateNetworkRequest { Name = "Lobby", SlotsMax = 8 });

        // Assert
        Assert.True(result.Success);
        Assert.Contains("\"preferRelay\":true", createBody ?? string.Empty);
        Assert.Contains("\"endpoint\":\"\"", createBody ?? string.Empty);
        Assert.Equal(string.Empty, service.LocalEndpoint);
    }

    /// <summary>
    /// Tests that calling JoinNetworkAsync with default parameters masks public IP and uses relay.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WithDefaultParameters_ShouldMaskIpAndPreferRelayAsync()
    {
        // Arrange
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        string? joinBody = null;
        var service = CreateService(CreateFactory(joinStatus: HttpStatusCode.OK, onJoinBody: body => joinBody = body), adapter.Object);

        // Act - omit preferRelay to test default parameter
        var result = await service.JoinNetworkAsync("net-1", "secret");

        // Assert
        Assert.True(result.Success);
        Assert.Contains(""preferRelay":true", joinBody ?? string.Empty);
        Assert.Contains(""endpoint":""", joinBody ?? string.Empty);
        Assert.Equal(string.Empty, service.LocalEndpoint);
    }

    /// <summary>
    /// Tests that when the primary edge fails to issue a session token, the client fails over to the backup edge.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task EnsureSessionAsync_WhenPrimaryEdgeFails_ShouldFailoverToBackupEdgeAsync()
    {
        // Arrange
        ApiConstants.ResetActiveOnlineEdgeBaseUrl();
        var primaryAttempts = 0;
        var backupAttempts = 0;

        var handler = new CountingHandler();
        handler.Responder = request =>
        {
            var host = request.RequestUri?.Authority ?? string.Empty;
            if (request.RequestUri?.AbsolutePath.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal) == true)
            {
                if (host.Contains("152.70.171.121", StringComparison.Ordinal))
                {
                    primaryAttempts++;
                    throw new HttpRequestException("Primary VPS unreachable");
                }

                backupAttempts++;
                return JsonResponse(SessionJson);
            }

            return Route(request, HttpStatusCode.OK);
        };

        try
        {
            var service = CreateService(CreateFactory(handler));

            // Act
            var result = await service.GetNetworksAsync();

            // Assert
            Assert.True(result.Success);
            Assert.True(primaryAttempts > 0, "Expected primary edge to be attempted first");
            Assert.True(backupAttempts > 0, "Expected backup edge to be attempted upon failover");
            Assert.Equal(ApiConstants.FallbackOnlineEdgeBaseUrl, ApiConstants.ActiveOnlineEdgeBaseUrl);
        }
        finally
        {
            ApiConstants.ResetActiveOnlineEdgeBaseUrl();
        }
    }

    private static OnlineNetworkService CreateService(
        IHttpClientFactory factory,
        IVirtualLanAdapter? adapter = null,
        IP2PConnectionService? p2p = null)
    {
        var presence = new Mock<IOnlinePresenceService>();
        presence.Setup(p => p.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        presence.Setup(p => p.DisconnectAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));

        Mock<IP2PConnectionService>? p2pMock = null;
        if (p2p is null)
        {
            p2pMock = new Mock<IP2PConnectionService>();
            p2pMock.Setup(p => p.StartListeningAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<System.Net.IPEndPoint>.CreateFailure("No listener"));
            p2pMock.Setup(p => p.StopListeningAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
            p2p = p2pMock.Object;
        }

        return new OnlineNetworkService(
            factory,
            adapter ?? Mock.Of<IVirtualLanAdapter>(),
            presence.Object,
            p2p,
            Mock.Of<ILogger<OnlineNetworkService>>());
    }

    private static IHttpClientFactory CreateFactory(
        CountingHandler? handler = null,
        HttpStatusCode joinStatus = HttpStatusCode.OK,
        Action<string>? onJoinBody = null,
        Action<string>? onCreateBody = null,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        handler ??= new CountingHandler();
        handler.Responder = responder ?? (request => Route(request, joinStatus, onJoinBody, onCreateBody));
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(() =>
            {
                var inner = new CountingHandler { Responder = handler.Responder };
                inner.Counted += (_, _) => handler.Increment();
                return new HttpClient(inner, disposeHandler: true);
            });
        return factory.Object;
    }

    private static HttpResponseMessage Route(
        HttpRequestMessage request,
        HttpStatusCode joinStatus,
        Action<string>? onJoinBody = null,
        Action<string>? onCreateBody = null)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (request.Method == HttpMethod.Post && path.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal))
        {
            return JsonResponse(SessionJson);
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/v1/networks", StringComparison.Ordinal))
        {
            onCreateBody?.Invoke(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
            return JsonResponse(JoinJson);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/v1/networks", StringComparison.Ordinal))
        {
            return JsonResponse(FreshDirectoryJson());
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/join", StringComparison.Ordinal))
        {
            onJoinBody?.Invoke(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
            return joinStatus == HttpStatusCode.OK
                ? JsonResponse(JoinJson)
                : new HttpResponseMessage(joinStatus);
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/leave", StringComparison.Ordinal))
        {
            return JsonResponse("""{"success":true}""");
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/report", StringComparison.Ordinal))
        {
            return JsonResponse("""{"success":true}""");
        }

        if (request.Method == HttpMethod.Post && path.EndsWith("/ban", StringComparison.Ordinal))
        {
            return JsonResponse("""{"success":true}""");
        }

        if (request.Method == HttpMethod.Patch)
        {
            return JsonResponse("""{"success":true}""");
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }

    private static string FreshDirectoryJson() =>
        DirectoryJsonTemplate.Replace("FRESH_TIMESTAMP", DateTime.UtcNow.ToString("O"), StringComparison.Ordinal);

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public event EventHandler? Counted;

        public Func<HttpRequestMessage, HttpResponseMessage>? Responder { get; set; }

        public int CallCount { get; private set; }

        public void Increment()
        {
            CallCount++;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            Counted?.Invoke(this, EventArgs.Empty);
            var response = Responder?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
            return Task.FromResult(response);
        }
    }
}
