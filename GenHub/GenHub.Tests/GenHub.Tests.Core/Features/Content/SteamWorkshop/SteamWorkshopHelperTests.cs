using GenHub.Core.Constants;
using GenHub.Core.Models.Content;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Results.Content;
using GenHub.Features.Content.Services.SteamWorkshop;
using System;
using Xunit;
using ContentType = GenHub.Core.Models.Enums.ContentType;

namespace GenHub.Tests.Core.Features.Content.SteamWorkshop;

/// <summary>
/// Unit tests for <see cref="SteamWorkshopHelper"/>.
/// </summary>
public sealed class SteamWorkshopHelperTests
{
    /// <summary>
    /// Verifies Generals and Zero Hour map to their workshop AppIDs.
    /// </summary>
    /// <param name="game">The target game.</param>
    /// <param name="expected">The expected AppID.</param>
    [Theory]
    [InlineData(GameType.Generals, SteamWorkshopConstants.GeneralsAppId)]
    [InlineData(GameType.ZeroHour, SteamWorkshopConstants.ZeroHourAppId)]
    [InlineData(GameType.Unknown, SteamWorkshopConstants.ZeroHourAppId)]
    [InlineData(null, SteamWorkshopConstants.ZeroHourAppId)]
    public void AppIdForGame_MapsGameToAppId(GameType? game, int expected)
    {
        Assert.Equal(expected, SteamWorkshopHelper.AppIdForGame(game));
    }

    /// <summary>
    /// Verifies workshop AppIDs map back to games.
    /// </summary>
    /// <param name="appId">The workshop AppID.</param>
    /// <param name="expected">The expected game.</param>
    [Theory]
    [InlineData(SteamWorkshopConstants.GeneralsAppId, GameType.Generals)]
    [InlineData(SteamWorkshopConstants.ZeroHourAppId, GameType.ZeroHour)]
    [InlineData(0, GameType.ZeroHour)]
    public void GameForAppId_MapsAppIdToGame(int appId, GameType expected)
    {
        Assert.Equal(expected, SteamWorkshopHelper.GameForAppId(appId));
    }

    /// <summary>
    /// Verifies the default browse sort is trending without a search term.
    /// </summary>
    [Fact]
    public void ResolveBrowseSort_NoSearchTerm_ReturnsTrend()
    {
        var query = new ContentSearchQuery { SearchTerm = string.Empty };

        Assert.Equal(SteamWorkshopConstants.SortTrend, SteamWorkshopHelper.ResolveBrowseSort(query));
    }

    /// <summary>
    /// Verifies free-text searches default to text search.
    /// </summary>
    [Fact]
    public void ResolveBrowseSort_WithSearchTerm_ReturnsTextSearch()
    {
        var query = new ContentSearchQuery { SearchTerm = "desert" };

        Assert.Equal(SteamWorkshopConstants.SortTextSearch, SteamWorkshopHelper.ResolveBrowseSort(query));
    }

    /// <summary>
    /// Verifies an explicit sort wins over the search-term default.
    /// </summary>
    [Fact]
    public void ResolveBrowseSort_ExplicitSort_ReturnsExplicitSort()
    {
        var query = new ContentSearchQuery { SearchTerm = "desert", SteamWorkshopSort = SteamWorkshopConstants.SortTopRated };

        Assert.Equal(SteamWorkshopConstants.SortTopRated, SteamWorkshopHelper.ResolveBrowseSort(query));
    }

    /// <summary>
    /// Verifies unknown sort values fall back to trending.
    /// </summary>
    [Fact]
    public void ResolveBrowseSort_UnknownSort_ReturnsTrend()
    {
        var query = new ContentSearchQuery { SteamWorkshopSort = "bogus" };

        Assert.Equal(SteamWorkshopConstants.SortTrend, SteamWorkshopHelper.ResolveBrowseSort(query));
    }

    /// <summary>
    /// Verifies browse URLs carry the AppID, sort, page, and encoded search text.
    /// </summary>
    [Fact]
    public void BuildBrowseUrl_BuildsAbsoluteUrlWithParameters()
    {
        var query = new ContentSearchQuery
        {
            SearchTerm = "shock & awe",
            TargetGame = GameType.Generals,
            Page = 2,
            SteamWorkshopSort = SteamWorkshopConstants.SortMostRecent,
        };

        var url = SteamWorkshopHelper.BuildBrowseUrl(query);

        Assert.StartsWith(SteamWorkshopConstants.BrowseBaseUrl, url, StringComparison.Ordinal);
        Assert.Contains($"appid={SteamWorkshopConstants.GeneralsAppId}", url, StringComparison.Ordinal);
        Assert.Contains("browsesort=mostrecent", url, StringComparison.Ordinal);
        Assert.Contains("p=2", url, StringComparison.Ordinal);
        Assert.Contains("searchtext=shock%20%26%20awe", url, StringComparison.Ordinal);
        Assert.True(Uri.TryCreate(url, UriKind.Absolute, out _));
    }

    /// <summary>
    /// Verifies published file IDs extract from details URLs.
    /// </summary>
    /// <param name="url">The details URL.</param>
    /// <param name="expected">The expected published file ID.</param>
    [Theory]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=3790356853", "3790356853")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=123", "123")]
    public void TryExtractPublishedFileId_ValidUrl_ReturnsId(string url, string expected)
    {
        Assert.True(SteamWorkshopHelper.TryExtractPublishedFileId(url, out var id));
        Assert.Equal(expected, id);
    }

    /// <summary>
    /// Verifies malformed URLs do not produce IDs.
    /// </summary>
    /// <param name="url">The URL under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-url")]
    [InlineData("https://steamcommunity.com/workshop/browse/?appid=2732960")]
    [InlineData("https://steamcommunity.com/sharedfiles/filedetails/?id=abc")]
    public void TryExtractPublishedFileId_InvalidUrl_ReturnsFalse(string? url)
    {
        Assert.False(SteamWorkshopHelper.TryExtractPublishedFileId(url, out _));
    }

    /// <summary>
    /// Verifies published file IDs extract from content IDs.
    /// </summary>
    /// <param name="contentId">The content ID.</param>
    /// <param name="expected">The expected published file ID.</param>
    [Theory]
    [InlineData("steamworkshop.3790356853", "3790356853")]
    [InlineData("STEAMWORKSHOP.123", "123")]
    public void TryExtractPublishedFileIdFromContentId_ValidId_ReturnsId(string contentId, string expected)
    {
        Assert.True(SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(contentId, out var id));
        Assert.Equal(expected, id);
    }

    /// <summary>
    /// Verifies malformed content IDs do not produce IDs.
    /// </summary>
    /// <param name="contentId">The content ID under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("cnclabs.map.1")]
    [InlineData("steamworkshop.")]
    [InlineData("steamworkshop.abc")]
    public void TryExtractPublishedFileIdFromContentId_InvalidId_ReturnsFalse(string? contentId)
    {
        Assert.False(SteamWorkshopHelper.TryExtractPublishedFileIdFromContentId(contentId, out _));
    }

    /// <summary>
    /// Verifies mission keywords classify items as missions.
    /// </summary>
    /// <param name="title">The item title.</param>
    /// <param name="description">The item description.</param>
    [Theory]
    [InlineData("[SUPZH] USA10 Shock And Awe", "Operation: clear the city")]
    [InlineData("Desert Storm", "Single player campaign mission")]
    public void InferContentType_MissionKeywords_ReturnsMission(string title, string description)
    {
        var (type, isInferred) = SteamWorkshopHelper.InferContentType(title, description);

        Assert.Equal(ContentType.Mission, type);
        Assert.True(isInferred);
    }

    /// <summary>
    /// Verifies map pack keywords classify items as map packs.
    /// </summary>
    [Fact]
    public void InferContentType_MapPackKeywords_ReturnsMapPack()
    {
        var (type, _) = SteamWorkshopHelper.InferContentType("Ultimate ZH map pack", "100 maps");

        Assert.Equal(ContentType.MapPack, type);
    }

    /// <summary>
    /// Verifies plain titles default to maps.
    /// </summary>
    [Fact]
    public void InferContentType_PlainTitle_ReturnsMap()
    {
        var (type, isInferred) = SteamWorkshopHelper.InferContentType("Desert duel", "8 player skirmish");

        Assert.Equal(ContentType.Map, type);
        Assert.True(isInferred);
    }

    /// <summary>
    /// Verifies details dates with a year parse exactly.
    /// </summary>
    [Fact]
    public void ParseDetailsDate_WithYear_ParsesExactly()
    {
        var parsed = SteamWorkshopHelper.ParseDetailsDate("26 Aug, 2024 @ 4:25am");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2024, 8, 26, 4, 25, 0, DateTimeKind.Utc), parsed.Value);
    }

    /// <summary>
    /// Verifies details dates without a year assume a plausible year.
    /// </summary>
    [Fact]
    public void ParseDetailsDate_WithoutYear_AssumesCurrentOrPriorYear()
    {
        var parsed = SteamWorkshopHelper.ParseDetailsDate("26 Aug @ 4:25am");

        Assert.NotNull(parsed);
        Assert.Equal(26, parsed.Value.Day);
        Assert.Equal(8, parsed.Value.Month);
        Assert.True(parsed.Value <= DateTime.UtcNow);
    }

    /// <summary>
    /// Verifies unparsable dates return null.
    /// </summary>
    /// <param name="text">The raw date text.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("yesterday")]
    public void ParseDetailsDate_Invalid_ReturnsNull(string? text)
    {
        Assert.Null(SteamWorkshopHelper.ParseDetailsDate(text));
    }

    /// <summary>
    /// Verifies Unix timestamps convert to UTC dates.
    /// </summary>
    [Fact]
    public void FromUnixTime_ValidTimestamp_ReturnsUtcDate()
    {
        Assert.Equal(new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), SteamWorkshopHelper.FromUnixTime(1_704_067_200));
    }

    /// <summary>
    /// Verifies non-positive timestamps return null.
    /// </summary>
    /// <param name="timestamp">The Unix timestamp.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FromUnixTime_InvalidTimestamp_ReturnsNull(long timestamp)
    {
        Assert.Null(SteamWorkshopHelper.FromUnixTime(timestamp));
    }

    /// <summary>
    /// Verifies browse URLs carry required tag filters.
    /// </summary>
    [Fact]
    public void BuildBrowseUrl_WithRequiredTags_AppendsTagParameters()
    {
        var query = new ContentSearchQuery { TargetGame = GameType.ZeroHour, Page = 1 };
        query.SteamWorkshopRequiredTags.Add("Single Player");
        query.SteamWorkshopRequiredTags.Add("coop");

        var url = SteamWorkshopHelper.BuildBrowseUrl(query);

        Assert.Contains("requiredtags[]=Single%20Player", url, StringComparison.Ordinal);
        Assert.Contains("requiredtags[]=Co-op", url, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies tag aliases normalize to canonical filter values.
    /// </summary>
    /// <param name="raw">The raw tag value.</param>
    /// <param name="expected">The expected canonical value.</param>
    [Theory]
    [InlineData("coop", "Co-op")]
    [InlineData("aod", "Art of Defense")]
    [InlineData(" Large ", "Large")]
    [InlineData("1v1", "1v1")]
    [InlineData("Modded", "Modded")]
    public void NormalizeWorkshopTag_Aliases_ReturnsCanonical(string raw, string expected)
    {
        Assert.Equal(expected, SteamWorkshopHelper.NormalizeWorkshopTag(raw));
    }

    /// <summary>
    /// Verifies explicit workshop tags classify items as missions.
    /// </summary>
    [Fact]
    public void InferContentType_CoopTag_ReturnsMission()
    {
        var (type, _) = SteamWorkshopHelper.InferContentType("Desert duel", "8 player skirmish", ["Co-op"]);

        Assert.Equal(ContentType.Mission, type);
    }

    /// <summary>
    /// Verifies player-count tags promote badge metadata.
    /// </summary>
    /// <param name="tag">The workshop tag.</param>
    /// <param name="expectedBadge">The expected player-count badge.</param>
    [Theory]
    [InlineData("1v1", "2 players")]
    [InlineData("2v2", "4 players")]
    [InlineData("Single Player", "1 player")]
    public void ApplyWorkshopTagBadges_PlayerCountTags_PromotesBadge(string tag, string expectedBadge)
    {
        var result = new ContentSearchResult { ContentType = ContentType.Map };

        SteamWorkshopHelper.ApplyWorkshopTagBadges(result, [tag]);

        Assert.Equal(expectedBadge, ContentCardBadgeHelper.GetPlayerCountBadge(result));
    }

    /// <summary>
    /// Verifies required tags are normalized and deduplicated.
    /// </summary>
    [Fact]
    public void GetNormalizedRequiredTags_AliasesAndDuplicates_ReturnsDistinctCanonical()
    {
        var query = new ContentSearchQuery();
        query.SteamWorkshopRequiredTags.Add("coop");
        query.SteamWorkshopRequiredTags.Add("Co-op");
        query.SteamWorkshopRequiredTags.Add("Large");

        var tags = SteamWorkshopHelper.GetNormalizedRequiredTags(query);

        Assert.Equal(["Co-op", "Large"], tags);
    }

    /// <summary>
    /// Verifies Steam thumbnail URLs upgrade to their full-resolution originals.
    /// </summary>
    /// <param name="sized">The sized thumbnail URL.</param>
    /// <param name="expected">The expected full-resolution URL.</param>
    [Theory]
    [InlineData("https://images.steamusercontent.com/ugc/1/abc/?imw=116&imh=65&ima=fit&impolicy=Letterbox&imcolor=%23000000&letterbox=true", "https://images.steamusercontent.com/ugc/1/abc/")]
    [InlineData("https://images.steamusercontent.com/ugc/1/abc/?imw=268&imh=268", "https://images.steamusercontent.com/ugc/1/abc/")]
    [InlineData("https://images.steamusercontent.com/ugc/1/abc/", "https://images.steamusercontent.com/ugc/1/abc/")]
    [InlineData("https://example.com/preview.jpg?imw=116", "https://example.com/preview.jpg?imw=116")]
    [InlineData("https://example.com/preview.jpg", "https://example.com/preview.jpg")]
    public void ToFullResolutionImageUrl_StripsResizeParams(string sized, string expected)
    {
        Assert.Equal(expected, SteamWorkshopHelper.ToFullResolutionImageUrl(sized));
    }

    /// <summary>
    /// Verifies null and empty URLs pass through untouched.
    /// </summary>
    /// <param name="url">The URL under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ToFullResolutionImageUrl_NullOrEmpty_ReturnsInput(string? url)
    {
        Assert.Equal(url, SteamWorkshopHelper.ToFullResolutionImageUrl(url));
    }

    /// <summary>
    /// Verifies that ExtractSteamIdFromToken correctly parses the subject claim from a JWT.
    /// </summary>
    [Fact]
    public void ExtractSteamIdFromToken_ValidJwt_ReturnsSteamId()
    {
        // {"sub":"76561198000000001","iss":"steam"} in base64url
        const string token = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiI3NjU2MTE5ODAwMDAwMDAwMSIsImlzcyI6InN0ZWFtIn0.sig";

        var steamId = SteamWorkshopHelper.ExtractSteamIdFromToken(token);

        Assert.Equal(76_561_198_000_000_001UL, steamId);
    }

    /// <summary>
    /// Verifies that ExtractSteamIdFromToken returns 0 for invalid or malformed tokens.
    /// </summary>
    /// <param name="token">The token under test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("header.not-json.sig")]
    [InlineData("header.e30.sig")]
    public void ExtractSteamIdFromToken_Invalid_ReturnsZero(string? token)
    {
        Assert.Equal(0UL, SteamWorkshopHelper.ExtractSteamIdFromToken(token));
    }
}
