using GenHub.Core.Constants;
using GenHub.Core.Helpers;

namespace GenHub.Tests.Core.Features.GameSettings;

/// <summary>
/// Tests for <see cref="LanNicknameCodec"/>. Vectors mirror the game's
/// quoted-printable codec: UTF-16LE bytes with non-alphanumeric bytes escaped
/// as <c>_HH</c>.
/// </summary>
public class LanNicknameCodecTests
{
    /// <summary>
    /// Alphanumeric names need no escaping apart from their zero high bytes.
    /// </summary>
    [Fact]
    public void Encode_AlphanumericName_ShouldEscapeZeroBytes()
    {
        Assert.Equal("A_00B_00C_001_00", LanNicknameCodec.Encode("ABC1"));
    }

    /// <summary>
    /// Spaces and other non-alphanumeric bytes are escaped as _HH.
    /// </summary>
    [Fact]
    public void Encode_NameWithSpace_ShouldEscapeSpaceAndZeroBytes()
    {
        Assert.Equal("a_00_20_00b_00", LanNicknameCodec.Encode("a b"));
    }

    /// <summary>
    /// Underscores are not alphanumeric, so they are escaped too.
    /// </summary>
    [Fact]
    public void Encode_Underscore_ShouldBeEscaped()
    {
        Assert.Equal("_5F_00", LanNicknameCodec.Encode("_"));
    }

    /// <summary>
    /// Non-ASCII characters round-trip through their UTF-16LE bytes.
    /// </summary>
    [Fact]
    public void EncodeDecode_NonAsciiName_ShouldRoundTrip()
    {
        const string Nickname = "Müller_寿";
        var encoded = LanNicknameCodec.Encode(Nickname);

        Assert.Equal(Nickname, LanNicknameCodec.Decode(encoded));
    }

    /// <summary>
    /// Decoding reverses encoding for plain, spaced, and unicode names.
    /// </summary>
    /// <param name="nickname">The nickname to round-trip.</param>
    [Theory]
    [InlineData("Commander")]
    [InlineData("a b")]
    [InlineData("Player_01")]
    [InlineData("Zéro_Héroe_寿")]
    public void Decode_EncodedName_ShouldReturnOriginal(string nickname)
    {
        Assert.Equal(nickname, LanNicknameCodec.Decode(LanNicknameCodec.Encode(nickname)));
    }

    /// <summary>
    /// A trailing escape without hex digits ends the value like the game decoder.
    /// </summary>
    [Fact]
    public void Decode_TrailingEscape_ShouldStopDecoding()
    {
        Assert.Equal("A", LanNicknameCodec.Decode("A_00_"));
    }

    /// <summary>
    /// Blank input normalizes to empty, signalling that the file must be left alone.
    /// </summary>
    /// <param name="nickname">The raw nickname.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankNickname_ShouldReturnEmpty(string? nickname)
    {
        Assert.Equal(string.Empty, LanNicknameCodec.Normalize(nickname));
    }

    /// <summary>
    /// Surrounding whitespace is trimmed.
    /// </summary>
    [Fact]
    public void Normalize_PaddedNickname_ShouldTrim()
    {
        Assert.Equal("Ace", LanNicknameCodec.Normalize("  Ace  "));
    }

    /// <summary>
    /// Names longer than the game's limit are truncated to it.
    /// </summary>
    [Fact]
    public void Normalize_OverlongNickname_ShouldTruncateToGameLimit()
    {
        var overlong = new string('A', OnlineConstants.MaxNicknameLength + 4);

        var normalized = LanNicknameCodec.Normalize(overlong);

        Assert.Equal(OnlineConstants.MaxNicknameLength, normalized.Length);
        Assert.Equal(new string('A', OnlineConstants.MaxNicknameLength), normalized);
    }
}
