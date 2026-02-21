using GenHub.Core.Constants;
using GenHub.Core.Interfaces.GameReplays;
using GenHub.Core.Models.GameReplays;
using GenHub.Core.Models.Results;
using GenHub.Core.Services.GameReplays;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Services.GameReplays;

/// <summary>
/// Tests for the <see cref="GameReplaysCommentService"/> class.
/// </summary>
public class GameReplaysCommentServiceTests
{
    private readonly Mock<IGameReplaysHttpClient> _httpClientMock;
    private readonly Mock<IGameReplaysParser> _parserMock;
    private readonly Mock<ILogger<GameReplaysCommentService>> _loggerMock;
    private readonly GameReplaysCommentService _sut;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameReplaysCommentServiceTests"/> class.
    /// </summary>
    public GameReplaysCommentServiceTests()
    {
        _httpClientMock = new Mock<IGameReplaysHttpClient>();
        _parserMock = new Mock<IGameReplaysParser>();
        _loggerMock = new Mock<ILogger<GameReplaysCommentService>>();

        _sut = new GameReplaysCommentService(
            _httpClientMock.Object,
            _parserMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Tests that GetCommentsAsync successfully fetches and parses comments.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCommentsAsync_SuccessfullyFetchesAndParsesComments()
    {
        // Arrange
        var topicId = "12345";
        var htmlContent = "<html>some content</html>";
        var posts = new List<ForumPost>
        {
            new()
            {
                PostId = "1",
                Author = "100",
                AuthorDisplayName = "User1",
                ContentHtml = "Comment 1",
                PostedAt = DateTime.UtcNow,
            },
            new()
            {
                PostId = "2",
                Author = "101",
                AuthorDisplayName = "User2",
                ContentHtml = "Comment 2",
                PostedAt = DateTime.UtcNow,
            },
        };

        _httpClientMock.Setup(x => x.GetHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(htmlContent));

        _parserMock.Setup(x => x.ParseForumPosts(htmlContent))
            .Returns(OperationResult<IEnumerable<ForumPost>>.CreateSuccess(posts));

        // Act
        var result = await _sut.GetCommentsAsync(topicId);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(2, result.Data.Count());

        var firstComment = result.Data.First();
        Assert.Equal("1", firstComment.Id);
        Assert.Equal("100", firstComment.AuthorId);
        Assert.Equal("User1", firstComment.AuthorName);
        Assert.Equal("Comment 1", firstComment.Content);

        _httpClientMock.Verify(
            x => x.GetHtmlAsync(
                It.Is<string>(url => url.Contains(topicId) && url.Contains(GameReplaysConstants.BaseUrl)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>
    /// Tests that GetCommentsAsync returns failure when HTTP request fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCommentsAsync_ReturnsFailure_WhenHttpFails()
    {
        // Arrange
        var topicId = "12345";
        _httpClientMock.Setup(x => x.GetHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateFailure("Network error"));

        // Act
        var result = await _sut.GetCommentsAsync(topicId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Network error", result.FirstError);
    }

    /// <summary>
    /// Tests that GetCommentsAsync returns failure when parsing fails.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [Fact]
    public async Task GetCommentsAsync_ReturnsFailure_WhenParsingFails()
    {
        // Arrange
        var topicId = "12345";
        var htmlContent = "<html>some content</html>";

        _httpClientMock.Setup(x => x.GetHtmlAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<string>.CreateSuccess(htmlContent));

        _parserMock.Setup(x => x.ParseForumPosts(htmlContent))
            .Returns(OperationResult<IEnumerable<ForumPost>>.CreateFailure("Parse error"));

        // Act
        var result = await _sut.GetCommentsAsync(topicId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Parse error", result.FirstError);
    }
}
