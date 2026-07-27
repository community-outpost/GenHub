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
    [InlineData("101525_QFE2", 1015252)]
    [InlineData("111825_QFE2", 1118252)]
    [InlineData("121525_QFE1", 1215251)]
    [InlineData("060526_QFE1", 605261)]
    [InlineData("042826_QFE3", 428263)]
    [InlineData("101525_QFE10", 1015260)]
    [InlineData("011526_QFE1_EAC_X86", 11526186)]
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
        Assert.Equal(20260116, GameVersionHelper.GetGeneralsOnlineManifestIdComponent("2026-01-16"));
    }

    /// <summary>
    /// Verifies that malformed, signed, and overflowing QFE values use the established
    /// digit-extraction fallback instead of producing wrapped manifest IDs.
    /// </summary>
    /// <param name="version">The malformed or overflowing version string.</param>
    /// <param name="expected">The expected fallback component.</param>
    [Theory]
    [InlineData("101525_QFE-1", 1015251)]
    [InlineData("101525_QFEQFE-2", 1015252)]
    [InlineData("101525_QFE2147483647", 1015252147)]
    public void GetGeneralsOnlineManifestIdComponent_FallsBackForInvalidQfe(string version, int expected)
    {
        Assert.Equal(expected, GameVersionHelper.GetGeneralsOnlineManifestIdComponent(version));
    }

    /// <summary>
    /// Verifies that signed and whitespace-padded date components use the fallback
    /// rather than being accepted by permissive integer parsing.
    /// </summary>
    /// <param name="version">The malformed version string.</param>
    [Theory]
    [InlineData("01+225_QFE2")]
    [InlineData("01 225_QFE2")]
    [InlineData("0102+5_QFE2")]
    public void GetGeneralsOnlineManifestIdComponent_FallsBackForNonDigitDate(string version)
    {
        Assert.Equal(
            GameVersionHelper.ExtractVersionFromVersionString(version),
            GameVersionHelper.GetGeneralsOnlineManifestIdComponent(version));
    }
}
