using GenHub.Features.AppUpdate.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Unit tests for <see cref="SemVerComparator"/>.
/// </summary>
public class SemVerComparatorTests
{
    private readonly SemVerComparator _comparator = new(Mock.Of<ILogger<SemVerComparator>>());

    /// <summary>
    /// Tests version comparison for various version string pairs.
    /// </summary>
    /// <param name="a">First version string (non-null).</param>
    /// <param name="b">Second version string (non-null).</param>
    /// <param name="expected">Expected comparison result.</param>
    [Theory]
    [InlineData("1.0.0", "1.0.1", -1)]
    [InlineData("1.0.1", "1.0.0", 1)]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("v1.2.3", "1.2.4", -1)]
    [InlineData("1.2.3", "v1.2.3", 0)]
    [InlineData("1.2.3-alpha", "1.2.3", -1)]
    [InlineData("1.2.3", "1.2.3-alpha", 1)]
    [InlineData("1.2.3-beta", "1.2.3-alpha", 1)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.10.0", "1.9.9", 1)]
    [InlineData("1.0.0", "0.0.0", 1)]
    [InlineData("0.0.0", "1.0.0", -1)]
    [InlineData("0.0.0", "0.0.0", 0)]
    [InlineData("1.0.0-alpha", "1.0.0", -1)]
    [InlineData("1.0.0", "1.0.0-alpha", 1)]
    [InlineData("v2.0.0", "2.0.0", 0)]
    public void Compare_Versions_ReturnsExpected(string a, string b, int expected)
    {
        var result = _comparator.Compare(a, b);
        if (expected == 0)
            Assert.Equal(0, result);
        else if (expected < 0)
            Assert.True(result < 0);
        else
            Assert.True(result > 0);
    }

    /// <summary>
    /// Tests IsNewer for various version string pairs.
    /// </summary>
    /// <param name="current">Current version string (non-null).</param>
    /// <param name="candidate">Candidate version string (non-null).</param>
    /// <param name="expected">Expected result.</param>
    [Theory]
    [InlineData("1.0.0", "1.0.1", true)]
    [InlineData("1.0.1", "1.0.0", false)]
    [InlineData("1.0.0", "1.0.0", false)]
    [InlineData("1.2.3-alpha", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.3-alpha", false)]
    public void IsNewer_WorksCorrectly(string current, string candidate, bool expected)
    {
        Assert.Equal(expected, _comparator.IsNewer(current, candidate));
    }
}