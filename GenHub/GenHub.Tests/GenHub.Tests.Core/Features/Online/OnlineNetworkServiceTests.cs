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

    private const string DirectoryJson = """
        [
          {"id":"net-1","name":"Zero Hour EU","tags":["zerohour"],"slotsUsed":3,"slotsMax":8,
           "region":"EU","hostDisplayName":"Commander","quality":1,"requiresPassword":true,
           "lastHeartbeatUtc":"2026-09-20T00:00:00Z"}
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
        // Arrange
        using var document = JsonDocument.Parse(DirectoryJson);
        var forbidden = new[] { "endpoint", "candidate", "publicip", "underlay", "reflexive" };

        // Act
        var keys = document.RootElement.EnumerateArray()
            .SelectMany(entry => entry.EnumerateObject().Select(property => property.Name))
            .Select(name => name.ToLowerInvariant())
            .ToList();

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
    /// Tests that adapter failure during join maps to the adapter error code and tears down.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task JoinNetworkAsync_WhenAdapterFails_ShouldTearDownAsync()
    {
        // Arrange
        var adapter = new Mock<IVirtualLanAdapter>();
        adapter.Setup(a => a.BringUpAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateFailure("no driver"));
        adapter.Setup(a => a.TearDownAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenHub.Core.Models.Results.OperationResult<bool>.CreateSuccess(true));
        var service = CreateService(CreateFactory(), adapter.Object);

        // Act
        var result = await service.JoinNetworkAsync("net-1", "secret");

        // Assert
        Assert.False(result.Success);
        Assert.Contains(OnlineConstants.ErrorAdapterFailed, result.Errors);
        Assert.Null(service.CurrentJoin);
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
        var update = await service.UpdateNetworkAsync("New desc", "zh-1.04");

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
        var result = await service.JoinNetworkAsync("net-1", "secret");

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

    private static OnlineNetworkService CreateService(
        IHttpClientFactory factory,
        IVirtualLanAdapter? adapter = null,
        IP2PConnectionService? p2p = null)
    {
        var presence = new Mock<IOnlinePresenceService>();
        presence.Setup(p => p.ConnectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
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
        Action<string>? onJoinBody = null)
    {
        handler ??= new CountingHandler();
        handler.Responder = request => Route(request, joinStatus, onJoinBody);
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

    private static HttpResponseMessage Route(HttpRequestMessage request, HttpStatusCode joinStatus, Action<string>? onJoinBody = null)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (request.Method == HttpMethod.Post && path.EndsWith("/v1/sessions/anonymous", StringComparison.Ordinal))
        {
            return JsonResponse(SessionJson);
        }

        if (request.Method == HttpMethod.Get && path.EndsWith("/v1/networks", StringComparison.Ordinal))
        {
            return JsonResponse(DirectoryJson);
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
