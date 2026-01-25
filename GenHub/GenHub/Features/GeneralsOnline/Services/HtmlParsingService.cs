using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
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

            // Parse table rows - HTML table format
            var rowMatches = LeaderboardRowRegex().Matches(html);

            foreach (Match match in rowMatches)
            {
                if (int.TryParse(match.Groups[1].Value.Trim(), out var rank) &&
                    double.TryParse(match.Groups[3].Value.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var score) &&
                    int.TryParse(match.Groups[4].Value.Trim(), out var wins) &&
                    int.TryParse(match.Groups[5].Value.Trim(), out var losses))
                {
                    var playerName = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
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

            // Parse player rows - HTML table format
            var rowMatches = PlayerRowRegex().Matches(html);

            foreach (Match match in rowMatches)
            {
                var playerNameRaw = match.Groups[1].Value.Trim();

                // Strip any HTML tags (e.g., Cloudflare email protection) and decode entities
                var playerName = WebUtility.HtmlDecode(StripHtmlTags(playerNameRaw));
                var statusText = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
                var clientVersion = WebUtility.HtmlDecode(match.Groups[3].Value.Trim());
                var timeText = WebUtility.HtmlDecode(match.Groups[4].Value.Trim());

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
            // Match history HTML card format:
            // <img src="assets/images/mapthumbnails/map.png" ...>
            // <h1 class="card-title small_h1">[EU] Quickmatch Lobby</h1>
            // <p class="card-text">player1, player2</p>
            var matchRegex = MatchEntryRegex();
            var matchEntries = matchRegex.Matches(html);

            int matchId = 100000;
            foreach (Match match in matchEntries)
            {
                var mapThumbnail = match.Groups[1].Value;
                var lobbyName = WebUtility.HtmlDecode(match.Groups[2].Value.Trim());
                var playersText = WebUtility.HtmlDecode(match.Groups[3].Value.Trim());

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

    /// <summary>
    /// Parses active lobbies/matches from HTML response.
    /// </summary>
    /// <param name="html">The HTML content to parse.</param>
    /// <returns>A list of active lobbies and lobby count.</returns>
    public (List<ActiveLobby> Lobbies, int LobbyCount) ParseActiveLobbies(string html)
    {
        var lobbies = new List<ActiveLobby>();
        int lobbyCount = 0;

        try
        {
            // Extract lobby count from nav badge: Lobbies <span class="badge">57</span>
            var countMatch = LobbyCountRegex().Match(html);
            if (countMatch.Success && int.TryParse(countMatch.Groups[1].Value, out var count))
            {
                lobbyCount = count;
            }

            // Parse lobby cards - each card represents a lobby
            var cardMatches = LobbyCardRegex().Matches(html);

            foreach (Match cardMatch in cardMatches)
            {
                try
                {
                    var cardHtml = cardMatch.Value;

                    // Extract map info: <small>[RANK] Map Name (slots)</small>
                    var mapMatch = LobbyMapInfoRegex().Match(cardHtml);
                    var mapName = mapMatch.Success ? WebUtility.HtmlDecode(mapMatch.Groups[1].Value.Trim()) : "Unknown Map";
                    var mapSlots = mapMatch.Success && int.TryParse(mapMatch.Groups[2].Value, out var slots) ? slots : 8;

                    // Extract map thumbnail
                    var thumbMatch = LobbyMapThumbnailRegex().Match(cardHtml);
                    var mapThumbnailUrl = thumbMatch.Success ? thumbMatch.Groups[1].Value : string.Empty;

                    // Extract lobby name: <h5 class='card-title'>[AS] Generals Online Lobby</h5>
                    var lobbyMatch = LobbyTitleRegex().Match(cardHtml);
                    var lobbyName = lobbyMatch.Success ? WebUtility.HtmlDecode(lobbyMatch.Groups[1].Value.Trim()) : "Unknown Lobby";

                    // Extract region from lobby name
                    var region = ExtractRegion(lobbyName);

                    // Parse player slots
                    var playerSlots = ParseLobbySlots(cardHtml);

                    // Determine status - check for "Game In Progress" indicator
                    var status = cardHtml.Contains("Game In Progress", StringComparison.OrdinalIgnoreCase)
                        ? LobbyStatus.InProgress
                        : LobbyStatus.Waiting;

                    // Parse settings
                    var settings = ParseLobbySettings(cardHtml);

                    lobbies.Add(new ActiveLobby(
                        lobbyName,
                        mapName,
                        mapThumbnailUrl,
                        mapSlots,
                        region,
                        playerSlots,
                        status,
                        settings));
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to parse individual lobby card");
                }
            }

            logger.LogDebug("Parsed {Count} active lobbies from {Total} total", lobbies.Count, lobbyCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to parse active lobbies HTML");
        }

        return (lobbies, lobbyCount);
    }

    private static List<LobbySlot> ParseLobbySlots(string cardHtml)
    {
        var slots = new List<LobbySlot>();

        // Find the Players section and parse each row
        // HTML structure:
        // <div class="row">
        //   <div class="col-sm-2"><i class="fa-solid fa-square" style="color: #FF0000;"></i></div>
        //   <div class="col-sm-2"><img src="assets/images/teams/..." /> OR <i class="fa-solid fa-circle-question" /></div>
        //   <div class="col-sm">PlayerName OR Closed OR Open</div>
        // </div>
        var playerRows = LobbyPlayerRowRegex().Matches(cardHtml);

        foreach (Match rowMatch in playerRows)
        {
            var rowHtml = rowMatch.Value;

            // Extract team color from fa-square style
            var colorMatch = LobbySlotColorRegex().Match(rowHtml);
            var teamColor = colorMatch.Success ? colorMatch.Groups[1].Value.TrimEnd(';') : "#FFFFFF";

            // Check for faction icon image
            var factionMatch = LobbyFactionIconRegex().Match(rowHtml);
            string? factionIcon = null;
            if (factionMatch.Success)
            {
                // Make it a full URL
                factionIcon = "https://www.playgenerals.online/" + factionMatch.Groups[1].Value;
            }

            // Extract player name from the col-sm div (not col-sm-2)
            var nameMatch = LobbyPlayerNameRegex().Match(rowHtml);
            var slotContent = nameMatch.Success ? WebUtility.HtmlDecode(nameMatch.Groups[1].Value.Trim()) : string.Empty;

            // Determine slot state and player name
            SlotState slotState;
            string? playerName = null;

            if (string.Equals(slotContent, "Closed", StringComparison.OrdinalIgnoreCase))
            {
                slotState = SlotState.Closed;
            }
            else if (string.Equals(slotContent, "Open", StringComparison.OrdinalIgnoreCase))
            {
                slotState = SlotState.Open;
            }
            else if (!string.IsNullOrWhiteSpace(slotContent))
            {
                playerName = slotContent;
                slotState = SlotState.Occupied;
            }
            else
            {
                slotState = SlotState.Closed;
            }

            slots.Add(new LobbySlot(playerName, teamColor, factionIcon, slotState));
        }

        return slots;
    }

    private static LobbySettings ParseLobbySettings(string cardHtml)
    {
        // Extract settings section
        bool isCustomMap = cardHtml.Contains("Custom Map") && ContainsCheckmark(cardHtml, "Custom Map");
        bool isOriginalArmies = cardHtml.Contains("Original Armies") && ContainsCheckmark(cardHtml, "Original Armies");
        bool limitSuperweapons = cardHtml.Contains("Limit Superweapons") && ContainsCheckmark(cardHtml, "Limit Superweapons");
        bool trackStats = cardHtml.Contains("Track Stats") && ContainsCheckmark(cardHtml, "Track Stats");
        bool allowObservers = cardHtml.Contains("Allow Observers") && ContainsCheckmark(cardHtml, "Allow Observers");
        bool isPassworded = cardHtml.Contains("fa-lock\"") && !cardHtml.Contains("fa-lock-open");

        // Extract starting cash
        var cashMatch = LobbyStartingCashRegex().Match(cardHtml);
        var startingCash = cashMatch.Success ? cashMatch.Groups[1].Value : "$10000";

        // Extract camera height
        var cameraMatch = LobbyCameraHeightRegex().Match(cardHtml);
        var cameraHeight = cameraMatch.Success ? cameraMatch.Groups[1].Value : "Default";

        return new LobbySettings(
            isCustomMap,
            isOriginalArmies,
            startingCash,
            limitSuperweapons,
            trackStats,
            allowObservers,
            isPassworded,
            cameraHeight);
    }

    private static bool ContainsCheckmark(string html, string settingName)
    {
        // Find the setting row and check if it has fa-square-check (checked) vs fa-square-xmark (unchecked)
        var settingIndex = html.IndexOf(settingName, StringComparison.OrdinalIgnoreCase);
        if (settingIndex < 0)
        {
            return false;
        }

        // Look for check/xmark within the next ~200 chars (reasonable for a row)
        var searchEnd = Math.Min(settingIndex + 200, html.Length);
        var searchSection = html.Substring(settingIndex, searchEnd - settingIndex);
        return searchSection.Contains("fa-square-check", StringComparison.OrdinalIgnoreCase);
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

    private static string StripHtmlTags(string html)
    {
        // Remove HTML tags and return plain text
        // For email protection tags, try to extract the text content
        var stripped = HtmlTagRegex().Replace(html, string.Empty);
        return stripped.Trim();
    }

    // Leaderboard patterns
    [GeneratedRegex(@"There are (\d+) player\(s\) in this ladder")]
    private static partial Regex TotalPlayersRegex();

    // Leaderboard row pattern: parses HTML table rows like:
    // <th scope='row'>1</th>
    // <td>PlayerName</td>
    // <td>1320</td>
    // <td>10</td>
    // <td>1</td>
    [GeneratedRegex(@"<th[^>]*scope='row'[^>]*>(\d+)</th>\s*<td>([^<]+)</td>\s*<td>(\d+)</td>\s*<td>(\d+)</td>\s*<td>(\d+)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LeaderboardRowRegex();

    // Active players patterns
    [GeneratedRegex(@"There are (\d+) online player")]
    private static partial Regex OnlinePlayersRegex();

    [GeneratedRegex(@"Total Lifetime Players:\s*([\d,]+)")]
    private static partial Regex LifetimePlayersRegex();

    // Player row pattern: parses HTML table rows like:
    // <th scope='row'>PlayerName</th>
    // <td>In lobby '[AS] Lobby' - Status</td>
    // <td>GeneralsOnline 60Hz</td>
    // <td>Playing for X minutes, Y seconds</td>
    // Note: Some player names may contain email protection tags, so we use a more flexible pattern
    [GeneratedRegex(@"<th[^>]*scope='row'[^>]*>(.+?)</th>\s*<td>([^<]+)</td>\s*<td>([^<]+)</td>\s*<td>([^<]+)</td>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
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

    // Match history pattern - parses card-based HTML structure:
    // <img src="assets/images/mapthumbnails/map.png" ...>
    // <h1 class="card-title small_h1">[EU] Quickmatch Lobby</h1>
    // <p class="card-text">player1, player2</p>
    // <a ... href="/viewmatch?match=223910" ...>
    [GeneratedRegex(@"<img\s+src=""assets/images/mapthumbnails/([^""]+)""[^>]*>\s*(?:</div>\s*)?(?:<div[^>]*>\s*)?<div[^>]*card-body[^>]*>\s*<h1[^>]*card-title[^>]*>([^<]+)</h1>\s*<p[^>]*card-text[^>]*>([^<]+)</p>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex MatchEntryRegex();

    [GeneratedRegex(@"\[([A-Z]{2})\]")]
    private static partial Regex RegionTagRegex();

    // HTML tag stripping pattern
    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    // Active lobbies patterns
    // Lobby count from nav: Lobbies <span class="badge text-bg-secondary">57</span>
    [GeneratedRegex(@"Lobbies\s*<span[^>]*>(\d+)</span>", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyCountRegex();

    // Each lobby card - ends with </p></div></div></div> pattern
    [GeneratedRegex(@"<div\s+class='card\s+rounded-0\s+w-100\s+mb-1'>.*?</p>\s*</div>\s*</div>\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LobbyCardRegex();

    // Map info: <small>[RANK] Map Name (slots)</small>
    [GeneratedRegex(@"<small>([^<]+)\((\d+)\)</small>", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyMapInfoRegex();

    // Map thumbnail: <img src='assets/images/mapthumbnails/...
    [GeneratedRegex(@"<img\s+src='(assets/images/mapthumbnails/[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyMapThumbnailRegex();

    // Lobby title: <h5 class='card-title'>[AS] Generals Online Lobby</h5>
    [GeneratedRegex(@"<h5\s+class='card-title'>([^<]+)</h5>", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyTitleRegex();

    // Player row in lobby - captures div.row containing player slot with three col divs
    // Structure: <div class="row">...<div class="col-sm-2">color</div><div class="col-sm-2">faction</div><div class="col-sm">name</div></div>
    [GeneratedRegex(@"<div\s+class=""row"">\s*<div\s+class=""col-sm-2"">\s*<i\s+class=""fa-solid\s+fa-square""[^>]*>.*?</div>\s*<div\s+class=""col-sm-2"">.*?</div>\s*<div\s+class=""col-sm"">.*?</div>\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LobbyPlayerRowRegex();

    // Slot color: style="color: #32D7E6;"
    [GeneratedRegex(@"fa-square""\s+style=""color:\s*([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex LobbySlotColorRegex();

    // Faction icon: <img ... src="assets/images/teams/usa_super.webp"
    [GeneratedRegex(@"<img[^>]*src=""(assets/images/teams/[^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyFactionIconRegex();

    // Player name - text inside the last <div class="col-sm"> (not col-sm-2)
    [GeneratedRegex(@"<div\s+class=""col-sm"">\s*([^<]+?)\s*</div>\s*</div>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LobbyPlayerNameRegex();

    // Starting cash: Starting Cash</div> ... <div class="col-sm">$300000</div>
    [GeneratedRegex(@"Starting Cash</div>\s*<div[^>]*>(\$[\d,]+)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyStartingCashRegex();

    // Camera height: Camera Height</div> ... <div ...>Modified (310)</div>
    [GeneratedRegex(@"Camera Height</div>\s*<div[^>]*>([^<]+)</div>", RegexOptions.IgnoreCase)]
    private static partial Regex LobbyCameraHeightRegex();
}
