using GenHub.Core.Helpers;
using System;
using Xunit;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Unit tests for <see cref="ChatTimestampHelper"/>.
/// </summary>
public class ChatTimestampHelperTests
{
    /// <summary>
    /// Verifies that UTC timestamps are converted to local time correctly.
    /// </summary>
    [Fact]
    public void ToLocalTime_UtcTimestamp_ConvertsToLocalTime()
    {
        var utc = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);
        var expected = utc.ToLocalTime();

        var result = ChatTimestampHelper.ToLocalTime(utc);

        Assert.Equal(expected, result);
        Assert.Equal(DateTimeKind.Local, result.Kind);
    }

    /// <summary>
    /// Verifies that unspecified timestamps are treated as UTC and converted to local time.
    /// </summary>
    [Fact]
    public void ToLocalTime_UnspecifiedTimestamp_AssumesUtcAndConvertsToLocalTime()
    {
        var unspecified = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Unspecified);
        var expected = DateTime.SpecifyKind(unspecified, DateTimeKind.Utc).ToLocalTime();

        var result = ChatTimestampHelper.ToLocalTime(unspecified);

        Assert.Equal(expected, result);
        Assert.Equal(DateTimeKind.Local, result.Kind);
    }

    /// <summary>
    /// Verifies that local timestamps are returned unchanged.
    /// </summary>
    [Fact]
    public void ToLocalTime_LocalTimestamp_ReturnsUnchanged()
    {
        var local = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Local);

        var result = ChatTimestampHelper.ToLocalTime(local);

        Assert.Equal(local, result);
        Assert.Equal(DateTimeKind.Local, result.Kind);
    }
}
