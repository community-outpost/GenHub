using System.Collections.Generic;
using GenHub.Core.Extensions;

namespace GenHub.Tests.Core.Extensions;

/// <summary>
/// Tests for <see cref="DictionaryExtensions"/>.
/// </summary>
public class DictionaryExtensionsTests
{
    /// <summary>
    /// Verifies that looking up a key with exact casing returns true and the value.
    /// </summary>
    [Fact]
    public void TryGetCaseInsensitive_ExactKey_ReturnsTrueAndValue()
    {
        var dict = new Dictionary<string, string>
        {
            ["ArchiveReplays"] = "yes",
        };

        var result = dict.TryGetCaseInsensitive("ArchiveReplays", out var value);

        Assert.True(result);
        Assert.Equal("yes", value);
    }

    /// <summary>
    /// Verifies that looking up a key with different casing returns true and the value.
    /// </summary>
    [Fact]
    public void TryGetCaseInsensitive_DifferentCasing_ReturnsTrueAndValue()
    {
        var dict = new Dictionary<string, string>
        {
            ["archivereplays"] = "yes",
        };

        var result = dict.TryGetCaseInsensitive("ArchiveReplays", out var value);

        Assert.True(result);
        Assert.Equal("yes", value);
    }

    /// <summary>
    /// Verifies that looking up a non-existent key returns false and an empty string.
    /// </summary>
    [Fact]
    public void TryGetCaseInsensitive_NonExistentKey_ReturnsFalseAndEmptyString()
    {
        var dict = new Dictionary<string, string>
        {
            ["ExistingKey"] = "value",
        };

        var result = dict.TryGetCaseInsensitive("MissingKey", out var value);

        Assert.False(result);
        Assert.Equal(string.Empty, value);
    }

    /// <summary>
    /// Verifies that searching an empty dictionary returns false and an empty string.
    /// </summary>
    [Fact]
    public void TryGetCaseInsensitive_EmptyDictionary_ReturnsFalseAndEmptyString()
    {
        var dict = new Dictionary<string, string>();

        var result = dict.TryGetCaseInsensitive("AnyKey", out var value);

        Assert.False(result);
        Assert.Equal(string.Empty, value);
    }
}
