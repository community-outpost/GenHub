using System.Globalization;
using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Helpers;

/// <summary>
/// Tests for <see cref="GameVersionHelper"/> Generals Online manifest ID components.
/// </summary>
public class GameVersionHelperTests
{
    /// <summary>
    /// Pins the manifest ID encoding. These values appear inside the IDs of already-installed
    /// content, so changing any of them would orphan that content.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="expected">The expected manifest ID component.</param>
    [Theory]
    [InlineData("082826", 828260)]
    [InlineData("082826_QFE1", 828261)]
    [InlineData("101525_QFE2", 1_015_252)]
    [InlineData("111825_QFE2", 1_118_252)]
    [InlineData("121525_QFE1", 1_215_251)]
    [InlineData("060526_QFE1", 605261)]
    [InlineData("042826_QFE3", 428263)]
    [InlineData("101525_QFE10", 1_015_260)]
    [InlineData("011526_QFE1_EAC_X86", 11_526_186)]
    public void GetGeneralsOnlineManifestIdComponent_MatchesEstablishedEncoding(string version, int expected)
    {
        Assert.Equal(expected, GameVersionHelper.GetGeneralsOnlineManifestIdComponent(version));
    }

    /// <summary>
    /// Verifies that the current non-numeric EAC build tag retains its established ID.
    /// </summary>
    [Fact]
    public void GetGeneralsOnlineManifestIdComponent_PreservesEstablishedEacBuildId()
    {
        Assert.Equal(
            GameVersionHelper.GetGeneralsOnlineManifestIdComponent("042826_QFE3"),
            GameVersionHelper.GetGeneralsOnlineManifestIdComponent("042826_QFE3_EAC"));
    }

    /// <summary>
    /// Verifies that an empty version yields no component.
    /// </summary>
    /// <param name="version">The version string.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void GetGeneralsOnlineManifestIdComponent_ReturnsZeroForEmptyVersion(string? version)
    {
        Assert.Equal(0, GameVersionHelper.GetGeneralsOnlineManifestIdComponent(version));
    }

    /// <summary>
    /// Verifies that an unrecognized version falls back to digit extraction rather than throwing.
    /// </summary>
    [Fact]
    public void GetGeneralsOnlineManifestIdComponent_FallsBackForUnrecognizedVersion()
    {
        Assert.Equal(20_260_116, GameVersionHelper.GetGeneralsOnlineManifestIdComponent("2026-01-16"));
    }

    /// <summary>
    /// Verifies that malformed, signed, and overflowing QFE values use the established
    /// digit-extraction fallback instead of producing wrapped manifest IDs.
    /// </summary>
    /// <param name="version">The malformed or overflowing version string.</param>
    /// <param name="expected">The expected fallback component.</param>
    [Theory]
    [InlineData("101525_QFE-1", 1_015_251)]
    [InlineData("101525_QFE+1", 1_015_251)]
    [InlineData("101525_QFE2147483647", 1_015_252_147)]
    [InlineData("101525_QFE9999999999", 1_015_259_999)]
    public void GetGeneralsOnlineManifestIdComponent_FallsBackForMalformedOrOverflowingQfe(string version, int expected)
    {
        Assert.Equal(expected, GameVersionHelper.GetGeneralsOnlineManifestIdComponent(version));
    }

    /// <summary>
    /// Verifies that 8-digit date patterns (e.g. 2025-11-07, weekly-2025-11-21, 1.20260116) are correctly parsed.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="expected">The expected integer date representation.</param>
    [Theory]
    [InlineData("2025-11-07", 20_251_107)]
    [InlineData("weekly-2025-11-21", 20_251_121)]
    [InlineData("1.20260116", 20_260_116)]
    public void ExtractVersionFromVersionString_ParsesEightDigitDate(string version, int expected)
    {
        Assert.Equal(expected, GameVersionHelper.ExtractVersionFromVersionString(version));
    }

    /// <summary>
    /// Verifies that StripVersionPrefix removes a single leading 'v' or 'V' character when followed by a digit,
    /// without altering non-version strings or prefixes not followed by a digit.
    /// </summary>
    /// <param name="tag">The version or tag string.</param>
    /// <param name="expected">The expected stripped string.</param>
    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("V2.1", "2.1")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("vv1.0", "vv1.0")]
    [InlineData("vanilla", "vanilla")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void StripVersionPrefix_RemovesLeadingVPrefix(string? tag, string expected)
    {
        Assert.Equal(expected, GameVersionHelper.StripVersionPrefix(tag));
    }
}
