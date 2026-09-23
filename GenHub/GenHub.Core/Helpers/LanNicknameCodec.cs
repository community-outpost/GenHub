using GenHub.Core.Constants;
using System.Globalization;
using System.Text;

namespace GenHub.Core.Helpers;

/// <summary>
/// Encodes and decodes LAN player nicknames for the game's <c>Network.ini</c>
/// <c>UserName</c> value. Mirrors <c>UnicodeStringToQuotedPrintable</c> and
/// <c>QuotedPrintableToUnicodeString</c> from the GeneralsGameCode tree: the
/// UTF-16LE bytes of the name are written out, with every non-ASCII-alphanumeric
/// byte escaped as <c>_HH</c> (uppercase hex). The game truncates names to
/// <see cref="OnlineConstants.MaxNicknameLength"/> wide characters.
/// </summary>
public static class LanNicknameCodec
{
    private const char EscapeChar = '_';

    private const int EncodedByteLength = 3;

    private const int HexBase = 16;

    private const int HighNibbleShift = 4;

    private const int LowNibbleMask = 0xF;

    /// <summary>
    /// Trims surrounding whitespace and truncates the nickname to the length
    /// the game accepts. An empty result means there is nothing to sync and
    /// the game's file must be left untouched.
    /// </summary>
    /// <param name="nickname">The raw nickname text.</param>
    /// <returns>The normalized nickname.</returns>
    public static string Normalize(string? nickname)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return string.Empty;
        }

        var trimmed = nickname.Trim();
        return trimmed.Length > OnlineConstants.MaxNicknameLength
            ? trimmed.Substring(0, OnlineConstants.MaxNicknameLength)
            : trimmed;
    }

    /// <summary>
    /// Encodes a normalized nickname to the game's quoted-printable form.
    /// </summary>
    /// <param name="nickname">The normalized nickname.</param>
    /// <returns>The encoded value for <c>Network.ini</c>.</returns>
    public static string Encode(string nickname)
    {
        ArgumentNullException.ThrowIfNull(nickname);

        var bytes = Encoding.Unicode.GetBytes(nickname);
        var encoded = new StringBuilder(bytes.Length);
        foreach (var b in bytes)
        {
            if (IsAsciiAlphanumeric(b))
            {
                encoded.Append((char)b);
            }
            else
            {
                encoded.Append(EscapeChar);
                encoded.Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
        }

        return encoded.ToString();
    }

    /// <summary>
    /// Decodes a quoted-printable <c>UserName</c> value back to display text.
    /// A trailing escape character with no hex digits ends the value, matching
    /// the game's decoder.
    /// </summary>
    /// <param name="encoded">The raw <c>Network.ini</c> value.</param>
    /// <returns>The decoded nickname.</returns>
    public static string Decode(string encoded)
    {
        ArgumentNullException.ThrowIfNull(encoded);

        var bytes = new List<byte>(encoded.Length);
        var index = 0;
        while (index < encoded.Length)
        {
            if (encoded[index] == EscapeChar)
            {
                if (index + 2 >= encoded.Length)
                {
                    break;
                }

                var high = HexValue(encoded[index + 1]);
                var low = HexValue(encoded[index + 2]);
                if (high < 0 || low < 0)
                {
                    break;
                }

                bytes.Add((byte)((high << HighNibbleShift) | low));
                index += EncodedByteLength;
            }
            else
            {
                bytes.Add((byte)encoded[index]);
                index++;
            }
        }

        var aligned = bytes.Count - (bytes.Count % 2);
        return Encoding.Unicode.GetString(bytes.ToArray(), 0, aligned);
    }

    private static bool IsAsciiAlphanumeric(byte value) =>
        (value >= '0' && value <= '9') ||
        (value >= 'A' && value <= 'Z') ||
        (value >= 'a' && value <= 'z');

    private static int HexValue(char digit)
    {
        if (digit >= '0' && digit <= '9')
        {
            return digit - '0';
        }

        if (digit >= 'a' && digit <= 'f')
        {
            return digit - 'a' + 10;
        }

        if (digit >= 'A' && digit <= 'F')
        {
            return digit - 'A' + 10;
        }

        return -1;
    }
}
