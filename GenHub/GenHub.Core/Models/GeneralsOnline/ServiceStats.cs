namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Represents service statistics for Generals Online.
/// </summary>
public class ServiceStats
{
    /// <summary>
    /// Gets an empty ServiceStats instance.
    /// </summary>
    public static ServiceStats Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceStats"/> class.
    /// </summary>
    /// <param name="peakConcurrentPlayers">Peak number of concurrent players.</param>
    /// <param name="totalLifetimePlayers">Total number of players who have ever played.</param>
    /// <param name="playersOnline24h">Players online in the last 24 hours.</param>
    /// <param name="playersOnline30d">Players online in the last 30 days.</param>
    /// <param name="totalConnections24h">Total connections in the last 24 hours.</param>
    /// <param name="successfulConnections24h">Successful connections in the last 24 hours.</param>
    /// <param name="failedConnections24h">Failed connections in the last 24 hours.</param>
    /// <param name="totalConnections30d">Total connections in the last 30 days.</param>
    /// <param name="successfulConnections30d">Successful connections in the last 30 days.</param>
    /// <param name="failedConnections30d">Failed connections in the last 30 days.</param>
    /// <param name="ipv4Connections24h">IPv4 connections in the last 24 hours.</param>
    /// <param name="ipv6Connections24h">IPv6 connections in the last 24 hours.</param>
    public ServiceStats(
        int peakConcurrentPlayers,
        int totalLifetimePlayers,
        int playersOnline24h,
        int playersOnline30d,
        int totalConnections24h,
        int successfulConnections24h,
        int failedConnections24h,
        int totalConnections30d,
        int successfulConnections30d,
        int failedConnections30d,
        int ipv4Connections24h,
        int ipv6Connections24h)
    {
        PeakConcurrentPlayers = peakConcurrentPlayers;
        TotalLifetimePlayers = totalLifetimePlayers;
        PlayersOnline24h = playersOnline24h;
        PlayersOnline30d = playersOnline30d;
        TotalConnections24h = totalConnections24h;
        SuccessfulConnections24h = successfulConnections24h;
        FailedConnections24h = failedConnections24h;
        TotalConnections30d = totalConnections30d;
        SuccessfulConnections30d = successfulConnections30d;
        FailedConnections30d = failedConnections30d;
        Ipv4Connections24h = ipv4Connections24h;
        Ipv6Connections24h = ipv6Connections24h;
    }

    /// <summary>
    /// Gets the peak number of concurrent players.
    /// </summary>
    public int PeakConcurrentPlayers { get; }

    /// <summary>
    /// Gets the total number of players who have ever played.
    /// </summary>
    public int TotalLifetimePlayers { get; }

    /// <summary>
    /// Gets the number of players online in the last 24 hours.
    /// </summary>
    public int PlayersOnline24h { get; }

    /// <summary>
    /// Gets the number of players online in the last 30 days.
    /// </summary>
    public int PlayersOnline30d { get; }

    /// <summary>
    /// Gets the total connections in the last 24 hours.
    /// </summary>
    public int TotalConnections24h { get; }

    /// <summary>
    /// Gets the successful connections in the last 24 hours.
    /// </summary>
    public int SuccessfulConnections24h { get; }

    /// <summary>
    /// Gets the failed connections in the last 24 hours.
    /// </summary>
    public int FailedConnections24h { get; }

    /// <summary>
    /// Gets the total connections in the last 30 days.
    /// </summary>
    public int TotalConnections30d { get; }

    /// <summary>
    /// Gets the successful connections in the last 30 days.
    /// </summary>
    public int SuccessfulConnections30d { get; }

    /// <summary>
    /// Gets the failed connections in the last 30 days.
    /// </summary>
    public int FailedConnections30d { get; }

    /// <summary>
    /// Gets the IPv4 connections in the last 24 hours.
    /// </summary>
    public int Ipv4Connections24h { get; }

    /// <summary>
    /// Gets the IPv6 connections in the last 24 hours.
    /// </summary>
    public int Ipv6Connections24h { get; }

    /// <summary>
    /// Gets the connection success rate for the last 24 hours.
    /// </summary>
    public double SuccessRate24h => TotalConnections24h > 0
        ? (double)SuccessfulConnections24h / TotalConnections24h * 100
        : 0;

    /// <summary>
    /// Gets the connection success rate for the last 30 days.
    /// </summary>
    public double SuccessRate30d => TotalConnections30d > 0
        ? (double)SuccessfulConnections30d / TotalConnections30d * 100
        : 0;

    /// <summary>
    /// Gets the IPv6 adoption rate.
    /// </summary>
    public double Ipv6Rate24h => TotalConnections24h > 0
        ? (double)Ipv6Connections24h / TotalConnections24h * 100
        : 0;
}