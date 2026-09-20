using System.Net;

namespace GenHub.Core.Interfaces.Online;

/// <summary>
/// Local and STUN-resolved public endpoints for a P2P session.
/// </summary>
/// <param name="Local">The local bound endpoint.</param>
/// <param name="Public">The STUN-resolved public endpoint, or null when unavailable.</param>
public sealed record P2PEndpoints(IPEndPoint Local, IPEndPoint? Public);
