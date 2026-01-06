using System.Globalization;
using GenHub.Core.Models.AppUpdate;
using GenHub.Infrastructure.Converters;

namespace GenHub.Tests.Core.Converters;

/// <summary>
/// Tests for IsSubscribedConverter.
/// </summary>
public class IsSubscribedConverterTests
{
    private readonly IsSubscribedConverter _converter;

    /// <summary>
    /// Initializes a new instance of the <see cref="IsSubscribedConverterTests"/> class.
    /// </summary>
    public IsSubscribedConverterTests()
    {
        _converter = new IsSubscribedConverter();
    }

    /// <summary>
    /// Verifies that Convert returns true when PR matches.
    /// </summary>
    [Fact]
    public void Convert_ReturnsTrue_WhenPrMatches()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 123,
            Title = "PR 123",
            BranchName = "feature/123",
            Author = "user",
            State = "open"
        };
        var subscribedPr = new PullRequestInfo
        {
            Number = 123,
            Title = "PR 123",
            BranchName = "feature/123",
            Author = "user",
            State = "open"
        };
        var values = new List<object?> { pr, subscribedPr, "some-branch" };

        // Act
        var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Verifies that Convert returns false when PR does not match.
    /// </summary>
    [Fact]
    public void Convert_ReturnsFalse_WhenPrDoesNotMatch()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 123,
            Title = "PR 123",
            BranchName = "feature/123",
            Author = "user",
            State = "open"
        };
        var subscribedPr = new PullRequestInfo
        {
            Number = 456,
            Title = "PR 456",
            BranchName = "feature/456",
            Author = "user",
            State = "open"
        };
        var values = new List<object?> { pr, subscribedPr, "some-branch" };

        // Act
        var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Verifies that Convert returns true when branch matches.
    /// </summary>
    [Fact]
    public void Convert_ReturnsTrue_WhenBranchMatches()
    {
        // Arrange
        var branch = "main";
        var subscribedBranch = "main";
        var values = new List<object?> { branch, null, subscribedBranch };

        // Act
        var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.True((bool?)result);
    }

    /// <summary>
    /// Verifies that Convert returns false when values count is less than 3.
    /// </summary>
    [Fact]
    public void Convert_ReturnsFalse_WhenValuesCountTooLow()
    {
        // Arrange
        var values = new List<object?> { "item", null };

        // Act
        var result = _converter.Convert(values, typeof(bool), null, CultureInfo.InvariantCulture);

        // Assert
        Assert.False((bool?)result);
    }

    /// <summary>
    /// Verifies that ConvertBack returns an empty array.
    /// </summary>
    [Fact]
    public void ConvertBack_ReturnsEmptyArray()
    {
        // Act
        var result = _converter.ConvertBack(true, new[] { typeof(object), typeof(object), typeof(object) }, null, CultureInfo.InvariantCulture);

        // Assert
        Assert.Empty(result);
    }
}
