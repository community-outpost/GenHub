using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using System;
using System.Linq;
using Xunit;
using static GenHub.Core.Helpers.ProfileSortHelper;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for <see cref="ProfileSortHelper"/>.
/// </summary>
public class ProfileSortHelperTests
{
    /// <summary>
    /// Verifies that last-played sorting puts recently played profiles first and never-played profiles last.
    /// </summary>
    [Fact]
    public void SortByProfile_LastPlayed_OrdersByRecencyWithUnplayedLast()
    {
        // Arrange
        var unplayed = ("unplayed", new ProfileSortKeys("Unplayed", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0));
        var older = ("older", new ProfileSortKeys("Older", new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 1, 2, 0, 0, 0, DateTimeKind.Utc), 0));
        var newer = ("newer", new ProfileSortKeys("Newer", new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2024, 1, 3, 0, 0, 0, DateTimeKind.Utc), 0));

        // Act
        var sorted = SortByProfile<(string Id, ProfileSortKeys Keys)>([unplayed, older, newer], item => item.Keys, ProfileSortMode.LastPlayed);

        // Assert
        Assert.Equal(["newer", "older", "unplayed"], sorted.Select(item => item.Id));
    }

    /// <summary>
    /// Verifies that date-created sorting puts the newest profiles first.
    /// </summary>
    [Fact]
    public void SortByProfile_DateCreated_OrdersNewestFirst()
    {
        // Arrange
        var first = ("first", new ProfileSortKeys("First", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0));
        var second = ("second", new ProfileSortKeys("Second", default, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc), 0));

        // Act
        var sorted = SortByProfile<(string Id, ProfileSortKeys Keys)>([first, second], item => item.Keys, ProfileSortMode.DateCreated);

        // Assert
        Assert.Equal(["second", "first"], sorted.Select(item => item.Id));
    }

    /// <summary>
    /// Verifies alphabetical sorting in both directions.
    /// </summary>
    /// <param name="sortMode">The sort mode to apply.</param>
    /// <param name="expected">The expected profile ID order.</param>
    [Theory]
    [InlineData(ProfileSortMode.Alphabetical, new[] { "a", "b" })]
    [InlineData(ProfileSortMode.AlphabeticalDesc, new[] { "b", "a" })]
    public void SortByProfile_Alphabetical_OrdersByName(ProfileSortMode sortMode, string[] expected)
    {
        // Arrange
        var bravo = ("b", new ProfileSortKeys("Bravo", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0));
        var alpha = ("a", new ProfileSortKeys("Alpha", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0));

        // Act
        var sorted = SortByProfile<(string Id, ProfileSortKeys Keys)>([bravo, alpha], item => item.Keys, sortMode);

        // Assert
        Assert.Equal(expected, sorted.Select(item => item.Id));
    }

    /// <summary>
    /// Verifies that free sorting follows the persisted display order.
    /// </summary>
    [Fact]
    public void SortByProfile_Free_OrdersByDisplayOrder()
    {
        // Arrange
        var first = ("first", new ProfileSortKeys("First", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1));
        var zeroth = ("zeroth", new ProfileSortKeys("Zeroth", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0));

        // Act
        var sorted = SortByProfile<(string Id, ProfileSortKeys Keys)>([first, zeroth], item => item.Keys, ProfileSortMode.Free);

        // Assert
        Assert.Equal(["zeroth", "first"], sorted.Select(item => item.Id));
    }

    /// <summary>
    /// Verifies that defined sort modes pass normalization through unchanged.
    /// </summary>
    /// <param name="sortMode">The sort mode to normalize.</param>
    [Theory]
    [InlineData(ProfileSortMode.LastPlayed)]
    [InlineData(ProfileSortMode.DateCreated)]
    [InlineData(ProfileSortMode.Alphabetical)]
    [InlineData(ProfileSortMode.AlphabeticalDesc)]
    [InlineData(ProfileSortMode.Free)]
    public void NormalizeSortMode_DefinedMode_ReturnsMode(ProfileSortMode sortMode)
    {
        Assert.Equal(sortMode, NormalizeSortMode(sortMode));
    }

    /// <summary>
    /// Verifies that out-of-range sort modes fall back to last-played order.
    /// </summary>
    [Fact]
    public void NormalizeSortMode_UndefinedMode_FallsBackToLastPlayed()
    {
        Assert.Equal(ProfileSortMode.LastPlayed, NormalizeSortMode((ProfileSortMode)99));
    }

    /// <summary>
    /// Verifies that unknown sort modes fall back to display order instead of throwing.
    /// </summary>
    [Fact]
    public void SortByProfile_UnknownMode_FallsBackToDisplayOrder()
    {
        // Arrange
        var first = ("first", new ProfileSortKeys("First", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1));
        var zeroth = ("zeroth", new ProfileSortKeys("Zeroth", default, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc), 0));

        // Act
        var sorted = SortByProfile<(string Id, ProfileSortKeys Keys)>([first, zeroth], item => item.Keys, (ProfileSortMode)99);

        // Assert
        Assert.Equal(["zeroth", "first"], sorted.Select(item => item.Id));
    }
}
