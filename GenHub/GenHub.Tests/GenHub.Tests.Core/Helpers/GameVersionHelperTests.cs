using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for <see cref="GameVersionHelper"/> Generals Online version parsing.
/// </summary>
public class GameVersionHelperTests
{
    // ==== ParseGeneralsOnlineVersion — legacy format (MMDDYY_QFE#) ====

    /// <summary>
    /// Verifies that legacy two-segment versions parse correctly.
    /// </summary>
    [Fact]
    public void ParseGeneralsOnlineVersion_LegacyFormat_ParsesCorrectly()
    {
        var result = GameVersionHelper.ParseGeneralsOnlineVersion("101525_QFE2");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2025, 10, 15), result.Value.Date);
        Assert.Equal(2, result.Value.Qfe);
    }

    // ==== ParseGeneralsOnlineVersion — extended format (MMDDYY_QFE#_SUFFIX) ====

    /// <summary>
    /// Verifies that extended format with build suffix parses date and QFE correctly.
    /// </summary>
    [Fact]
    public void ParseGeneralsOnlineVersion_ExtendedFormat_ParsesDateAndQfe()
    {
        var result = GameVersionHelper.ParseGeneralsOnlineVersion("042826_QFE3_EAC");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 4, 28), result.Value.Date);
        Assert.Equal(3, result.Value.Qfe);
    }

    /// <summary>
    /// Verifies that versions with multiple suffix segments still parse correctly.
    /// </summary>
    [Fact]
    public void ParseGeneralsOnlineVersion_MultipleSuffixes_ParsesDateAndQfe()
    {
        var result = GameVersionHelper.ParseGeneralsOnlineVersion("011526_QFE1_EAC_X86");

        Assert.NotNull(result);
        Assert.Equal(new DateTime(2026, 1, 15), result.Value.Date);
        Assert.Equal(1, result.Value.Qfe);
    }

    // ==== ParseGeneralsOnlineVersion — edge cases ====

    /// <summary>
    /// Verifies that null or empty versions return null.
    /// </summary>
    /// <param name="version">The version string to test.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseGeneralsOnlineVersion_NullOrEmpty_ReturnsNull(string? version)
    {
        var result = GameVersionHelper.ParseGeneralsOnlineVersion(version);
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that versions missing the underscore separator return null.
    /// </summary>
    [Fact]
    public void ParseGeneralsOnlineVersion_NoUnderscore_ReturnsNull()
    {
        var result = GameVersionHelper.ParseGeneralsOnlineVersion("042826");
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that versions with malformed dates return null.
    /// </summary>
    [Fact]
    public void ParseGeneralsOnlineVersion_InvalidDate_ReturnsNull()
    {
        var result = GameVersionHelper.ParseGeneralsOnlineVersion("ABCDEF_QFE1");
        Assert.Null(result);
    }

    /// <summary>
    /// Verifies that versions with non-numeric QFE return null.
    /// </summary>
    [Fact]
    public void ParseGeneralsOnlineVersion_NonNumericQfe_ReturnsNull()
    {
        var result = GameVersionHelper.ParseGeneralsOnlineVersion("042826_QFEx");
        Assert.Null(result);
    }

    // ==== GetGeneralsOnlineSortableVersion ====

    /// <summary>
    /// Verifies that sortable version is computed correctly for legacy format.
    /// </summary>
    [Fact]
    public void GetGeneralsOnlineSortableVersion_LegacyFormat_ReturnsCorrectValue()
    {
        // "101525_QFE2" -> date=101525, qfe=2 -> 101525*10 + 2 = 1015252
        var result = GameVersionHelper.GetGeneralsOnlineSortableVersion("101525_QFE2");
        Assert.Equal(1015252, result);
    }

    /// <summary>
    /// Verifies that sortable version ignores build suffixes in extended format.
    /// </summary>
    [Fact]
    public void GetGeneralsOnlineSortableVersion_ExtendedFormat_IgnoresSuffix()
    {
        // "042826_QFE3_EAC" -> date=042826, qfe=3 -> 42826*10 + 3 = 428263
        var result = GameVersionHelper.GetGeneralsOnlineSortableVersion("042826_QFE3_EAC");
        Assert.Equal(428263, result);
    }

    /// <summary>
    /// Verifies that legacy and extended formats with same date+QFE produce the same sortable value.
    /// </summary>
    [Fact]
    public void GetGeneralsOnlineSortableVersion_SameVersionDifferentFormat_ProducesEqualValue()
    {
        var legacy = GameVersionHelper.GetGeneralsOnlineSortableVersion("042826_QFE3");
        var extended = GameVersionHelper.GetGeneralsOnlineSortableVersion("042826_QFE3_EAC");

        Assert.Equal(legacy, extended);
    }
}
