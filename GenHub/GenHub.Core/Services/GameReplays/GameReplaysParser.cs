using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenHub.Core.Services.GameReplays;

/// <summary>
/// Parser implementation for GameReplays HTML content.
/// Extracts tournament and forum post data from HTML.
/// </summary>
public partial class GameReplaysParser(ILogger<GameReplaysParser> logger) : IGameReplaysParser
{
    /// <inheritdoc/>
    public OperationResult<GameReplaysTournaments> ParseTournamentBoard(string html)
    {
        try
        {
            logger.LogDebug("Parsing tournament board HTML");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var signupsOpen = new List<Tournament>();
            var upcoming = new List<Tournament>();
            var active = new List<Tournament>();
            var finished = new List<Tournament>();

            // Find main post content
            var mainPost = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'post')]/div[@class='comment_wrapper']/div[@class='comment']/div[@class='comment_display_content']");

            if (mainPost == null)
            {
                return OperationResult<GameReplaysTournaments>.CreateFailure("Could not find main tournament post");
            }

            // Linear scan state machine
            var currentStatus = TournamentStatus.Finished; // Default bucket
            var foundAny = false;

            // Use Descendants to flatten the tree safely
            var allNodes = mainPost.Descendants();

            foreach (var node in allNodes)
            {
                // Only care about Text nodes for headers
                if (node.NodeType == HtmlNodeType.Text)
                {
                    var text = node.InnerText.Trim();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    // Detect Section Headers
                    if (IsHeader(text, "Signups Open"))
                    {
                        currentStatus = TournamentStatus.SignupsOpen;
                        continue;
                    }

                    if (IsHeader(text, "Upcoming"))
                    {
                        currentStatus = TournamentStatus.Upcoming;
                        continue;
                    }

                    if (IsHeader(text, "Active"))
                    {
                        currentStatus = TournamentStatus.Active;
                        continue;
                    }

                    // Handle "Finished/Previous" and specifically "Finished"
                    if (IsHeader(text, "Finished") || IsHeader(text, "Previous"))
                    {
                        currentStatus = TournamentStatus.Finished;
                        continue;
                    }
                }

                // Parse Links (Element nodes)
                if (node.Name == "a")
                {
                    var href = node.GetAttributeValue("href", string.Empty);

                    // Basic validation that it's a topic link
                    if (TopicLinkValidationRegex().IsMatch(href))
                    {
                        var tournament = ParseTournamentLink(node, currentStatus);
                        if (tournament != null)
                        {
                            foundAny = true;

                            // Add to list based on current status
                            switch (currentStatus)
                            {
                                case TournamentStatus.SignupsOpen:
                                    signupsOpen.Add(tournament);
                                    break;
                                case TournamentStatus.Upcoming:
                                    upcoming.Add(tournament);
                                    break;
                                case TournamentStatus.Active:
                                    active.Add(tournament);
                                    break;
                                case TournamentStatus.Finished:
                                    finished.Add(tournament);
                                    break;
                            }
                        }
                    }
                }
            }

            if (!foundAny)
            {
                logger.LogWarning("No tournaments found using linear scan.");
            }

            var result = new GameReplaysTournaments
            {
                SignupsOpen = signupsOpen,
                Upcoming = upcoming,
                Active = active,
                Finished = finished,
            };

            logger.LogDebug(
                "Parsed tournament board: {SignupsOpen} signups open, {Upcoming} upcoming, {Active} active, {Finished} finished",
                result.SignupsOpen.Count(),
                result.Upcoming.Count(),
                result.Active.Count(),
                result.Finished.Count());

            return OperationResult<GameReplaysTournaments>.CreateSuccess(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing tournament board HTML");
            return OperationResult<GameReplaysTournaments>.CreateFailure($"Parse error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public OperationResult<Tournament> ParseTournamentTopic(string html)
    {
        try
        {
            logger.LogDebug("Parsing tournament topic HTML");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var titleNode = doc.DocumentNode.SelectSingleNode("//h2");
            var title = titleNode?.InnerText.Trim() ?? "Unknown Tournament";

            var status = ExtractTournamentStatus(html);
            var (signupsCloseDate, startDate) = ExtractTournamentDates(html);

            var tournament = new Tournament
            {
                Name = title,
                Status = status,
                SignupsCloseDate = signupsCloseDate,
                StartDate = startDate,
                Description = FormatHtmlForDisplay(html),
            };

            return OperationResult<Tournament>.CreateSuccess(tournament);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing tournament topic HTML");
            return OperationResult<Tournament>.CreateFailure($"Parse error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public OperationResult<IEnumerable<ForumPost>> ParseForumPosts(string html)
    {
        try
        {
            logger.LogDebug("Parsing forum posts HTML");

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var posts = new List<ForumPost>();
            var postNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'post')]");

            if (postNodes == null)
            {
                return OperationResult<IEnumerable<ForumPost>>.CreateSuccess(posts);
            }

            foreach (var postNode in postNodes)
            {
                var post = ParsePostNode(postNode);
                if (post != null)
                {
                    posts.Add(post);
                }
            }

            logger.LogDebug("Parsed {Count} forum posts", posts.Count);

            return OperationResult<IEnumerable<ForumPost>>.CreateSuccess(posts);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing forum posts HTML");
            return OperationResult<IEnumerable<ForumPost>>.CreateFailure($"Parse error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public OperationResult<OAuthTokenResponse> ParseOAuthTokenResponse(string json)
    {
        try
        {
            logger.LogDebug("Parsing OAuth token response JSON");

            var options = JsonOptions;

            var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(json, options);

            if (tokenResponse == null)
            {
                return OperationResult<OAuthTokenResponse>.CreateFailure("Failed to deserialize token response");
            }

            return OperationResult<OAuthTokenResponse>.CreateSuccess(tokenResponse);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing OAuth token response JSON");
            return OperationResult<OAuthTokenResponse>.CreateFailure($"Parse error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public OperationResult<OAuthUserInfo> ParseOAuthUserInfo(string json)
    {
        try
        {
            logger.LogDebug("Parsing OAuth user info JSON");

            var options = JsonOptions;

            var userInfo = JsonSerializer.Deserialize<OAuthUserInfo>(json, options);

            if (userInfo == null)
            {
                return OperationResult<OAuthUserInfo>.CreateFailure("Failed to deserialize user info");
            }

            return OperationResult<OAuthUserInfo>.CreateSuccess(userInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing OAuth user info JSON");
            return OperationResult<OAuthUserInfo>.CreateFailure($"Parse error: {ex.Message}");
        }
    }

    /// <inheritdoc/>
    public TournamentStatus ExtractTournamentStatus(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Check for status indicators in main post
        var mainPost = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'post')]/div[@class='comment_wrapper']/div[@class='comment']/div[@class='comment_display_content']");

        if (mainPost == null)
        {
            return TournamentStatus.Finished;
        }

        // Check for signups open (orange)
        if (FindSectionByColor(mainPost, "orange") != null)
        {
            return TournamentStatus.SignupsOpen;
        }

        // Check for upcoming (red)
        if (FindSectionByColor(mainPost, "red") != null)
        {
            return TournamentStatus.Upcoming;
        }

        // Check for active (green)
        if (FindSectionByColor(mainPost, "green") != null)
        {
            return TournamentStatus.Active;
        }

        // Default to finished
        return TournamentStatus.Finished;
    }

    /// <inheritdoc/>
    public (DateTime? SignupsCloseDate, DateTime? StartDate) ExtractTournamentDates(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var mainPost = doc.DocumentNode.SelectSingleNode("//div[contains(@class, 'post')]/div[@class='comment_wrapper']/div[@class='comment']/div[@class='comment_display_content']");

        if (mainPost == null)
        {
            return (null, null);
        }

        DateTime? signupsCloseDate = null;
        DateTime? startDate = null;

        // Look for date patterns in italic text
        var italicNodes = mainPost.SelectNodes(".//i");
        if (italicNodes != null)
        {
            foreach (var italicNode in italicNodes)
            {
                var text = italicNode.InnerText;

                // Try to parse "Signups close: {date}" pattern
                if (text.Contains("Signups close", StringComparison.OrdinalIgnoreCase))
                {
                    signupsCloseDate = ParseDateFromText(text);
                }

                // Try to parse "Started: {date}" pattern
                if (text.Contains("Started", StringComparison.OrdinalIgnoreCase))
                {
                    startDate = ParseDateFromText(text);
                }
            }
        }

        return (signupsCloseDate, startDate);
    }

    /// <inheritdoc/>
    public string FormatHtmlForDisplay(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove unwanted elements
        var nodesToRemove = doc.DocumentNode.SelectNodes(
            "//script | //style | //iframe | //noscript | " +
            "//div[contains(@class, 'signature')] | " +
            "//div[contains(@class, 'post_buttons_primary')] | " +
            "//div[contains(@class, 'comment_authorinfo')]");

        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        // Clean up attributes
        var allNodes = doc.DocumentNode.SelectNodes(".//*");
        if (allNodes != null)
        {
            foreach (var node in allNodes)
            {
                // Keep only safe attributes
                var attributesToRemove = node.Attributes
                    .Where(a => !IsSafeAttribute(a.Name))
                    .ToList();

                foreach (var attr in attributesToRemove)
                {
                    node.Attributes.Remove(attr);
                }
            }
        }

        return doc.DocumentNode.OuterHtml;
    }

    /// <inheritdoc/>
    public string ExtractPlainText(string html)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // Remove script and style elements
        var nodesToRemove = doc.DocumentNode.SelectNodes("//script | //style");
        if (nodesToRemove != null)
        {
            foreach (var node in nodesToRemove)
            {
                node.Remove();
            }
        }

        return doc.DocumentNode.InnerText.Trim();
    }

    private static bool IsHeader(string text, string headerKeyword)
    {
        return text.Contains(headerKeyword, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindDateInfo(HtmlNode linkNode)
    {
        // Strategy 1: Next sibling <i> tag (common structure)
        var nextNode = linkNode.NextSibling;
        while (nextNode != null)
        {
            if (nextNode.Name == "i" || (nextNode.Name == "span" && nextNode.InnerText.Contains("Started")))
            {
                return nextNode.InnerText;
            }

            // Stop if we hit a newline or another link
            if (nextNode.Name == "br" || nextNode.Name == "a")
            {
                break;
            }

            nextNode = nextNode.NextSibling;
        }

        // Strategy 2: Parent's text (if link is inside li or span)
        if (linkNode.ParentNode != null)
        {
            var parentText = linkNode.ParentNode.InnerText;

            // Dates are often in brackets
            if (parentText.Contains('(') && parentText.Contains(')'))
            {
                return parentText;
            }
        }

        return string.Empty;
    }

    private static string ExtractHostName(string text)
    {
        var match = HostNameRegex().Match(text);
        return match.Success ? match.Groups[1].Value.Trim() : "Unknown";
    }

    private static DateTime? ParseDateFromText(string text)
    {
        // Try various date formats
        var formats = new[]
        {
            "MMMM d, yyyy",
            "MMM d, yyyy",
            "MMMM d yyyy",
            "d MMMM yyyy",
            "dd MMMM yyyy",
        };

        // Extract date from text
        var dateMatch = DateFromTextRegex().Match(text);
        if (dateMatch.Success)
        {
            var dateStr = dateMatch.Groups[1].Value;

            // Remove ordinal suffixes
            dateStr = OrdinalSuffixRegex().Replace(dateStr, "$1");
            dateStr = dateStr.Replace(",", string.Empty); // Remove commas for consistent parsing

            foreach (var format in formats)
            {
                // Try to parse with spaces condensed
                if (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return date;
                }

                if (DateTime.TryParseExact(dateStr, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateExact))
                {
                    return dateExact;
                }
            }
        }

        return null;
    }

    private static DateTime ParsePostDate(string text)
    {
        // Format: "Dec 30 2022, 09:03 AM"
        var match = PostDateRegex().Match(text);
        if (match.Success)
        {
            if (DateTime.TryParseExact(match.Value, "MMM dd yyyy, hh:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return DateTime.UtcNow;
    }

    private static DateTime? ParseEditDate(string text)
    {
        // Format: "This post has been edited by <b>Name</b>: Jan 11 2023, 14:00 PM"
        var match = EditDateRegex().Match(text);
        if (match.Success)
        {
            if (DateTime.TryParseExact(match.Value, ": MMM dd yyyy, hh:mm tt", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return date;
            }
        }

        return null;
    }

    private static bool IsSafeAttribute(string attributeName)
    {
        var safeAttributes = new[]
        {
            "href", "src", "alt", "title", "class", "id", "style",
        };

        return safeAttributes.Contains(attributeName.ToLowerInvariant());
    }

    private static HtmlNode? FindSectionByColor(HtmlNode parentNode, string color)
    {
        // Look for span with specific color
        var colorSpan = parentNode.SelectSingleNode($".//span[@style='color:{color}']");
        if (colorSpan == null)
        {
            return null;
        }

        // Get parent ul element
        var ulNode = colorSpan.ParentNode?.ParentNode;
        return ulNode?.Name == "ul" ? ulNode : null;
    }

    private static Tournament? ParseTournamentLink(HtmlNode linkNode, TournamentStatus status)
    {
        try
        {
            var href = linkNode.GetAttributeValue("href", string.Empty);
            var name = linkNode.InnerText.Trim();

            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            // Extract topic ID from URL
            var topicIdMatch = TopicIdRegex().Match(href);
            var topicId = topicIdMatch.Success ? topicIdMatch.Groups[1].Value : string.Empty;

            // Get host name from text (e.g., "hosted by AkRaMjOn")
            var host = ExtractHostName(name);

            // Get dates from nearby italic text
            // We search siblings or parent's siblings for the date info
            var dateInfo = FindDateInfo(linkNode);
            DateTime? signupsCloseDate = null;
            DateTime? startDate = null;

            if (!string.IsNullOrEmpty(dateInfo))
            {
                if (dateInfo.Contains("Signups close", StringComparison.OrdinalIgnoreCase))
                {
                    signupsCloseDate = ParseDateFromText(dateInfo);
                }

                if (dateInfo.Contains("Started", StringComparison.OrdinalIgnoreCase))
                {
                    startDate = ParseDateFromText(dateInfo);
                }

                // Sometimes it just says "May 12th 2025" without prefix for finished
                if (status == TournamentStatus.Finished && startDate == null)
                {
                     startDate = ParseDateFromText(dateInfo);
                }
            }

            return new Tournament
            {
                TopicId = topicId,
                Name = name.Replace(" hosted by " + host, string.Empty, StringComparison.OrdinalIgnoreCase).Trim(),
                Host = host,
                Url = href.StartsWith('/') ? GameReplaysConstants.BaseUrl + href : href,
                Status = status,
                SignupsCloseDate = signupsCloseDate,
                StartDate = startDate,
            };
        }
        catch
        {
            return null;
        }
    }

    private ForumPost? ParsePostNode(HtmlNode postNode)
    {
        try
        {
            var commentWrapper = postNode.SelectSingleNode(".//div[@class='comment_wrapper']");
            if (commentWrapper == null)
            {
                return null;
            }

            var comment = commentWrapper.SelectSingleNode(".//div[@class='comment']");
            if (comment == null)
            {
                return null;
            }

            var header = comment.SelectSingleNode(".//div[@class='comment_header']");
            var content = comment.SelectSingleNode(".//div[@class='comment_display_content']");

            if (header == null || content == null)
            {
                return null;
            }

            // Extract post number
            var postNumberNode = header.SelectSingleNode(".//a[@class='post_number']");
            var postId = postNumberNode?.GetAttributeValue("name", string.Empty) ?? string.Empty;

            // Extract author
            var authorNode = header.SelectSingleNode(".//span[@class='member_name']/a");
            var authorId = authorNode?.GetAttributeValue("href", string.Empty) ?? string.Empty;
            var authorName = authorNode?.InnerText.Trim() ?? "Unknown";

            // Extract author ID from URL
            var authorIdMatch = AuthorIdRegex().Match(authorId);
            var authorIdValue = authorIdMatch.Success ? authorIdMatch.Groups[1].Value : string.Empty;

            // Extract post date
            var dateText = header.InnerText;
            var postedAt = ParsePostDate(dateText);

            // Extract content HTML
            var contentHtml = content.InnerHtml;

            // Check if edited
            var editedNode = content.SelectSingleNode(".//span[@class='edited']");
            bool isEdited = editedNode != null;
            DateTime? editedAt = null;

            if (isEdited && editedNode != null)
            {
                var editedText = editedNode.InnerText;
                editedAt = ParseEditDate(editedText);
            }

            return new ForumPost
            {
                PostId = postId,
                TopicId = string.Empty, // Will be set by caller
                Author = authorIdValue,
                AuthorDisplayName = authorName,
                PostedAt = postedAt,
                ContentHtml = contentHtml,
                Comments = [],
                IsEdited = isEdited,
                EditedAt = editedAt,
            };
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error parsing post node");
            return null;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [GeneratedRegex(@"showtopic=\d+")]
    private static partial Regex TopicLinkValidationRegex();

    [GeneratedRegex(@"showtopic=(\d+)")]
    private static partial Regex TopicIdRegex();

    [GeneratedRegex(@"showuser=(\d+)")]
    private static partial Regex AuthorIdRegex();

    [GeneratedRegex(@"hosted by (.+)", RegexOptions.IgnoreCase)]
    private static partial Regex HostNameRegex();

    [GeneratedRegex(@"(\w+ \d{1,2}(?:st|nd|rd|th)?,? \d{4})")]
    private static partial Regex DateFromTextRegex();

    [GeneratedRegex(@"(\d+)(st|nd|rd|th)")]
    private static partial Regex OrdinalSuffixRegex();

    [GeneratedRegex(@"(\w{3} \d{1,2} \d{4}, \d{1,2}:\d{2} [AP]M)")]
    private static partial Regex PostDateRegex();

    [GeneratedRegex(@": (\w{3} \d{1,2} \d{4}, \d{1,2}:\d{2} [AP]M)")]
    private static partial Regex EditDateRegex();
}
