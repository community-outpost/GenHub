using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using System.Globalization;

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

    /// <summary>
    /// Verifies that FormatNumericManifestVersion formats versions correctly across different publishers and options.
    /// </summary>
    /// <param name="versionNumber">The integer version number.</param>
    /// <param name="publisherType">The publisher type identifier.</param>
    /// <param name="includePrefix">Whether to include the 'v' prefix.</param>
    /// <param name="expected">The expected formatted version string.</param>
    [Theory]
    [InlineData(104, PublisherTypeConstants.GeneralsOnline, false, "000104")]
    [InlineData(104, "GeneralsOnline", true, "000104")]
    [InlineData(20260821, "TheSuperHackers", false, "20260821")]
    [InlineData(20260821, null, true, "20260821")]
    [InlineData(104, null, false, "1.04")]
    [InlineData(104, null, true, "v1.04")]
    [InlineData(100, "Retail", false, "1.00")]
    [InlineData(100, "Retail", true, "v1.00")]
    [InlineData(106, "CommunityOutpost", false, "1.06")]
    [InlineData(106, "CommunityOutpost", true, "v1.06")]
    [InlineData(4, null, false, "4")]
    [InlineData(4, null, true, "v4")]
    [InlineData(0, null, false, "")]
    [InlineData(0, PublisherTypeConstants.GeneralsOnline, true, "")]
    [InlineData(-1, null, false, "")]
    public void FormatNumericManifestVersion_FormatsAccordingToContract(
        int versionNumber,
        string? publisherType,
        bool includePrefix,
        string expected)
    {
        Assert.Equal(expected, GameVersionHelper.FormatNumericManifestVersion(versionNumber, publisherType, includePrefix));
    }

    /// <summary>
    /// Verifies that TryParseStrictNumericVersion parses numeric formats and rejects alphanumeric/malformed strings.
    /// </summary>
    /// <param name="version">The version string to parse.</param>
    /// <param name="expectedSuccess">Expected parse success.</param>
    /// <param name="expectedNumeric">Expected numeric value.</param>
    [Theory]
    [InlineData("000104", true, 104)]
    [InlineData("104", true, 104)]
    [InlineData("1.04", true, 104)]
    [InlineData("v1.04", true, 104)]
    [InlineData("V1.06", true, 106)]
    [InlineData("1.00", true, 100)]
    [InlineData("20260821", true, 20260821)]
    [InlineData("1.04b", false, 0)]
    [InlineData("1.00alpha", false, 0)]
    [InlineData("1.0.4", false, 0)]
    [InlineData("101525_QFE2", false, 0)]
    [InlineData("0", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    [InlineData("   ", false, 0)]
    public void TryParseStrictNumericVersion_ParsesStrictNumericFormats(
        string? version,
        bool expectedSuccess,
        int expectedNumeric)
    {
        var success = GameVersionHelper.TryParseStrictNumericVersion(version, out var result);
        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedNumeric, result);
    }

    /// <summary>
    /// Verifies that IsDefaultVersion correctly classifies default and non-default versions.
    /// </summary>
    /// <param name="version">The version string.</param>
    /// <param name="expected">Whether the version is considered a default version.</param>
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("0", true)]
    [InlineData("0.0", true)]
    [InlineData("0.00", true)]
    [InlineData("0.000", true)]
    [InlineData("0.0.0", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("1", true)]
    [InlineData("1.0", true)]
    [InlineData("1.00", true)]
    [InlineData("1.000", true)]
    [InlineData("1.0.0", true)]
    [InlineData("v1.0", true)]
    [InlineData("V1.00", true)]
    [InlineData("1.04", false)]
    [InlineData("1.06", false)]
    [InlineData("v2.0", false)]
    [InlineData("20260821", false)]
    public void IsDefaultVersion_CorrectlyIdentifiesDefaultVersions(string? version, bool expected)
    {
        Assert.Equal(expected, GameVersionHelper.IsDefaultVersion(version));
    }

    /// <summary>
    /// Verifies that FormatDisplayVersion correctly suppresses empty, default, and unknown versions,
    /// and formats non-trivial versions with a leading 'v' where appropriate.
    /// </summary>
    /// <param name="version">The raw version string.</param>
    /// <param name="expected">The expected formatted display version.</param>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("0", null)]
    [InlineData("1.0", null)]
    [InlineData("v1.0", null)]
    [InlineData("1.00", null)]
    [InlineData("Unknown", null)]
    [InlineData("unknown", null)]
    [InlineData("Auto-Updated", null)]
    [InlineData("1.04", "v1.04")]
    [InlineData("1.06", "v1.06")]
    [InlineData("v2.5", "v2.5")]
    [InlineData("V3.0", "V3.0")]
    [InlineData("20260821", "v20260821")]
    [InlineData("beta-1", "beta-1")]
    public void FormatDisplayVersion_ReturnsExpectedResults(string? version, string? expected)
    {
        Assert.Equal(expected, GameVersionHelper.FormatDisplayVersion(version));
    }
}
