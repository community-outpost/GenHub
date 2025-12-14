using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GenHub.Core.Models.GeneralsOnline;
using Microsoft.Extensions.Logging;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Service for parsing HTML responses from Generals Online website.
/// 
/// NOTE: This is a temporary implementation that scrapes HTML from the Generals Online website.
/// This approach is fragile and should be replaced with proper API endpoints when available.
/// The HTML structure may change without notice, breaking this parsing logic.
/// </summary>
public partial class HtmlParsingService(ILogger<HtmlParsingService> logger)
{
    /// <summary>
    /// Parses leaderboard data from HTML response.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>A list of leaderboard entries and total player count.</returns>
    public (List<LeaderboardEntry> Entries, int TotalPlayers) ParseLeaderboard(string html)
    {
        var entries = new List<LeaderboardEntry>();
        int totalPlayers = 0;

        try
        {
            // Extract total players count: "There are 761 player(s) in this ladder"
            var totalMatch = TotalPlayersRegex().Match(html);
            if (totalMatch.Success && int.TryParse(totalMatch.Groups[1].Value, out var total))
            {
                totalPlayers = total;
            }

            // Parse table rows - format: | rank | name | score | wins | losses |
            var rowMatches = LeaderboardRowRegex().Matches(html);

            foreach (Match match in rowMatches)
            {
                if (int.TryParse(match.Groups[1].Value.Trim(), out var rank) &&
                    double.TryParse(match.Groups[3].Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var score) &&
                    int.TryParse(match.Groups[4].Value.Trim(), out var wins) &&
                    int.TryParse(match.Groups[5].Value.Trim(), out var losses))
                {
                    var playerName = match.Groups[2].Value.Trim();
                    entries.Add(new LeaderboardEntry(rank, playerName, score, wins, losses));
                }
            }

            logger.LogDebug("Parsed {Count} leaderboard entries from {Total} total", entries.Count, totalPlayers);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse leaderboard HTML");
        }

        return (entries, totalPlayers);
    }

    /// <summary>
    /// Parses active players data from HTML response.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>A list of active players and statistics.</returns>
    public (List<ActivePlayer> Players, int OnlineCount, int LifetimeCount) ParseActivePlayers(string html)
    {
        var players = new List<ActivePlayer>();
        int onlineCount = 0;
        int lifetimeCount = 0;

        try
        {
            // Extract online players count: "There are 608 online player(s)."
            var onlineMatch = OnlinePlayersRegex().Match(html);
            if (onlineMatch.Success && int.TryParse(onlineMatch.Groups[1].Value, out var online))
            {
                onlineCount = online;
            }

            // Extract lifetime players count: "Total Lifetime Players: 20029"
            var lifetimeMatch = LifetimePlayersRegex().Match(html);
            if (lifetimeMatch.Success && int.TryParse(lifetimeMatch.Groups[1].Value.Replace(",", string.Empty), out var lifetime))
            {
                lifetimeCount = lifetime;
            }

            // Parse player rows - format: | name | status | client | Playing for time |
            var rowMatches = PlayerRowRegex().Matches(html);

            foreach (Match match in rowMatches)
            {
                var playerName = match.Groups[1].Value.Trim();
                var statusText = match.Groups[2].Value.Trim();
                var clientVersion = match.Groups[3].Value.Trim();
                var timeText = match.Groups[4].Value.Trim();

                // Parse status
                var (status, lobbyName) = ParsePlayerStatus(statusText);

                // Parse time online
                var timeOnlineSeconds = ParseTimeOnline(timeText);

                players.Add(new ActivePlayer(playerName, status, lobbyName, clientVersion, timeOnlineSeconds));
            }

            logger.LogDebug("Parsed {Count} active players, {Online} online, {Lifetime} lifetime", players.Count, onlineCount, lifetimeCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse active players HTML");
        }

        return (players, onlineCount, lifetimeCount);
    }

    /// <summary>
    /// Parses service stats from HTML response.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>The service statistics.</returns>
    public ServiceStats ParseServiceStats(string html)
    {
        try
        {
            // Extract peak concurrent players: "Peak Concurrent Players: 641"
            int peakConcurrent = ExtractNumber(html, PeakConcurrentRegex()) ?? 0;

            // Extract lifetime players: "Total Lifetime Players: 20000"
            int lifetimePlayers = ExtractNumber(html, TotalLifetimeRegex()) ?? 0;

            // Parse connection outcomes - the format is:
            // Total Connections: 70176Successful Connections: 70173 (100%)Failed Connections: 3 (0%)
            int totalConnections24h = ExtractNumber(html, TotalConnectionsRegex()) ?? 0;
            int successfulConnections24h = ExtractNumber(html, SuccessfulConnectionsRegex()) ?? 0;
            int failedConnections24h = ExtractNumber(html, FailedConnectionsRegex()) ?? 0;

            // For 30d, we use the same values (the page shows cumulative data)
            int totalConnections30d = totalConnections24h;
            int successfulConnections30d = successfulConnections24h;
            int failedConnections30d = failedConnections24h;

            // Parse protocols
            int ipv4Connections24h = ExtractNumber(html, Ipv4ConnectionsRegex()) ?? 0;
            int ipv6Connections24h = ExtractNumber(html, Ipv6ConnectionsRegex()) ?? 0;

            logger.LogDebug(
                "Parsed service stats: Peak={Peak}, Lifetime={Lifetime}, Total={Total}, Success={Success}",
                peakConcurrent,
                lifetimePlayers,
                totalConnections24h,
                successfulConnections24h);

            return new ServiceStats(
                peakConcurrentPlayers: peakConcurrent,
                totalLifetimePlayers: lifetimePlayers,
                playersOnline24h: 0, // Will be set from players endpoint
                playersOnline30d: 0, // Will be set from players endpoint
                totalConnections24h: totalConnections24h,
                successfulConnections24h: successfulConnections24h,
                failedConnections24h: failedConnections24h,
                totalConnections30d: totalConnections30d,
                successfulConnections30d: successfulConnections30d,
                failedConnections30d: failedConnections30d,
                ipv4Connections24h: ipv4Connections24h,
                ipv6Connections24h: ipv6Connections24h);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse service stats HTML");
            return ServiceStats.Empty;
        }
    }

    /// <summary>
    /// Parses match history from HTML response.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>A list of match info.</returns>
    public List<MatchInfo> ParseMatchHistory(string html)
    {
        var matches = new List<MatchInfo>();

        try
        {
            // Match history format:
            // ![Image](mapthumbnails/map.png)
            // # [AS] Lobby Name
            // ` player1, player2, player3
            var matchRegex = MatchEntryRegex();
            var matchEntries = matchRegex.Matches(html);

            int matchId = 100000;
            foreach (Match match in matchEntries)
            {
                var mapThumbnail = match.Groups[1].Value;
                var lobbyName = match.Groups[2].Value.Trim();
                var playersText = match.Groups[3].Value.Trim();

                // Extract map name from thumbnail URL
                var mapName = ExtractMapName(mapThumbnail);

                // Extract region from lobby name
                var region = ExtractRegion(lobbyName);

                // Parse players - they're comma-separated
                var playersList = playersText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // Determine match type from lobby name
                var matchType = lobbyName.Contains("Quickmatch", StringComparison.OrdinalIgnoreCase) ? "Quickmatch" : "Custom";

                matches.Add(new MatchInfo(
                    matchId++,
                    lobbyName,
                    mapName,
                    region,
                    playersList,
                    matchType,
                    DateTime.UtcNow.AddMinutes(-matchId % 60),
                    TimeSpan.FromMinutes(15 + (matchId % 30)),
                    null));
            }

            logger.LogDebug("Parsed {Count} match history entries", matches.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse match history HTML");
        }

        return matches;
    }

    private static (PlayerStatus Status, string? LobbyName) ParsePlayerStatus(string statusText)
    {
        if (statusText.Contains("Server List", StringComparison.OrdinalIgnoreCase) ||
            statusText.Contains("Chat Room", StringComparison.OrdinalIgnoreCase))
        {
            return (PlayerStatus.InChatRoom, null);
        }

        if (statusText.Contains("Match In Progress", StringComparison.OrdinalIgnoreCase))
        {
            var lobbyMatch = LobbyNameRegex().Match(statusText);
            var lobbyName = lobbyMatch.Success ? lobbyMatch.Groups[1].Value : null;
            return (PlayerStatus.InGame, lobbyName);
        }

        if (statusText.Contains("Waiting", StringComparison.OrdinalIgnoreCase) ||
            statusText.Contains("game setup", StringComparison.OrdinalIgnoreCase))
        {
            var lobbyMatch = LobbyNameRegex().Match(statusText);
            var lobbyName = lobbyMatch.Success ? lobbyMatch.Groups[1].Value : null;
            return (PlayerStatus.GameSetup, lobbyName);
        }

        return (PlayerStatus.InChatRoom, null);
    }

    private static int ParseTimeOnline(string timeText)
    {
        int totalSeconds = 0;

        // Parse hours
        var hoursMatch = HoursRegex().Match(timeText);
        if (hoursMatch.Success && int.TryParse(hoursMatch.Groups[1].Value, out var hours))
        {
            totalSeconds += hours * 3600;
        }

        // Parse minutes
        var minutesMatch = MinutesRegex().Match(timeText);
        if (minutesMatch.Success && int.TryParse(minutesMatch.Groups[1].Value, out var minutes))
        {
            totalSeconds += minutes * 60;
        }

        // Parse seconds
        var secondsMatch = SecondsRegex().Match(timeText);
        if (secondsMatch.Success && int.TryParse(secondsMatch.Groups[1].Value, out var seconds))
        {
            totalSeconds += seconds;
        }

        return totalSeconds;
    }

    private static int? ExtractNumber(string html, Regex regex)
    {
        var match = regex.Match(html);
        if (match.Success && int.TryParse(match.Groups[1].Value.Replace(",", string.Empty), out var value))
        {
            return value;
        }

        return null;
    }

    private static string ExtractMapName(string thumbnailUrl)
    {
        // Extract map name from URL like: mapthumbnails/[go][rank]%20arctic%20lagoon%20zh%20v2.png
        // Or: mapthumbnails/fallen%20empire.png
        var decoded = Uri.UnescapeDataString(thumbnailUrl);
        var fileName = decoded.Split('/')[^1];
        var mapName = fileName.Replace(".png", string.Empty)
                              .Replace("[go][rank] ", string.Empty)
                              .Replace("[go][rank]", string.Empty);
        return mapName;
    }

    private static string ExtractRegion(string lobbyName)
    {
        var regionMatch = RegionTagRegex().Match(lobbyName);
        return regionMatch.Success ? regionMatch.Groups[1].Value : "??";
    }

    // Leaderboard patterns
    [GeneratedRegex(@"There are (\d+) player\(s\) in this ladder")]
    private static partial Regex TotalPlayersRegex();

    [GeneratedRegex(@"\|\s*(\d+)\s*\|\s*([^|]+)\s*\|\s*([\d.]+)\s*\|\s*(\d+)\s*\|\s*(\d+)\s*\|")]
    private static partial Regex LeaderboardRowRegex();

    // Active players patterns
    [GeneratedRegex(@"There are (\d+) online player")]
    private static partial Regex OnlinePlayersRegex();

    [GeneratedRegex(@"Total Lifetime Players:\s*([\d,]+)")]
    private static partial Regex LifetimePlayersRegex();

    // Player row pattern: | name | In lobby/status | client | Playing for time |
    [GeneratedRegex(@"\|\s*([^|]+)\s*\|\s*((?:In lobby|In Server)[^|]+)\s*\|\s*([^|]+)\s*\|\s*Playing for\s*([^|]+)\s*\|")]
    private static partial Regex PlayerRowRegex();

    [GeneratedRegex(@"In lobby '([^']+)'")]
    private static partial Regex LobbyNameRegex();

    // Time parsing
    [GeneratedRegex(@"(\d+)\s*hours?")]
    private static partial Regex HoursRegex();

    [GeneratedRegex(@"(\d+)\s*minutes?")]
    private static partial Regex MinutesRegex();

    [GeneratedRegex(@"(\d+)\s*seconds?")]
    private static partial Regex SecondsRegex();

    // Service stats patterns
    [GeneratedRegex(@"Peak Concurrent Players:\s*(\d+)")]
    private static partial Regex PeakConcurrentRegex();

    [GeneratedRegex(@"Total Lifetime Players:\s*([\d,]+)")]
    private static partial Regex TotalLifetimeRegex();

    [GeneratedRegex(@"Total Connections:\s*([\d,]+)")]
    private static partial Regex TotalConnectionsRegex();

    [GeneratedRegex(@"Successful Connections:\s*([\d,]+)")]
    private static partial Regex SuccessfulConnectionsRegex();

    [GeneratedRegex(@"Failed Connections:\s*([\d,]+)")]
    private static partial Regex FailedConnectionsRegex();

    [GeneratedRegex(@"IPv4 Connections:\s*([\d,]+)")]
    private static partial Regex Ipv4ConnectionsRegex();

    [GeneratedRegex(@"IPv6 Connections:\s*([\d,]+)")]
    private static partial Regex Ipv6ConnectionsRegex();

    // Match history pattern - captures map thumbnail URL, lobby name, and players
    [GeneratedRegex(@"!\[Image\]\([^)]*mapthumbnails/([^)]+)\)[^#]*#\s*(\[[A-Z]{2}\][^\n`]+)\s*\n\s*`?\s*([^\n]+)", RegexOptions.Multiline)]
    private static partial Regex MatchEntryRegex();

    [GeneratedRegex(@"\[([A-Z]{2})\]")]
    private static partial Regex RegionTagRegex();
}
