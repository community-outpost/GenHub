using System;
using System.Collections.Generic;
using GenHub.Core.Models.Results;
using Xunit;

namespace GenHub.Tests.Core.Models.Results;

/// <summary>
/// Unit tests for <see cref="DetectionResult{T}"/>.
/// </summary>
public class DetectionResultTests
{
    /// <summary>
    /// Verifies that Succeeded sets properties correctly.
    /// </summary>
    [Fact]
    public void Succeeded_SetsPropertiesCorrectly()
    {
        var items = new List<string> { "a", "b" };
        var elapsed = TimeSpan.FromSeconds(1);
        var result = DetectionResult<string>.Succeeded(items, elapsed);
        Assert.True(result.Success);
        Assert.Equal(items, result.Items);
        Assert.Equal(elapsed, result.Elapsed);
        Assert.Empty(result.Errors);
    }

    /// <summary>
    /// Verifies that Failed sets properties correctly.
    /// </summary>
    [Fact]
    public void Failed_SetsPropertiesCorrectly()
    {
        var error = "fail";
        var result = DetectionResult<string>.Failed(error);
        Assert.False(result.Success);
        Assert.Contains(error, result.Errors);
        Assert.Empty(result.Items);
    }
}
