using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace GenHub.Core.Services.Tools.GenHotkeys;

/// <summary>
/// Westwood/EA C&amp;C Generals String Table (.csf) binary parser, modifier, and serializer.
/// </summary>
public class CsfFile
{
    private static readonly byte[] MagicCsf = [(byte)' ', (byte)'F', (byte)'S', (byte)'C'];
    private static readonly byte[] MagicLbl = [(byte)' ', (byte)'L', (byte)'B', (byte)'L'];
    private static readonly byte[] MagicRts = [(byte)' ', (byte)'R', (byte)'T', (byte)'S'];
    private static readonly byte[] MagicWrts = [(byte)'W', (byte)'R', (byte)'T', (byte)'S'];

    private static readonly Regex HotkeyBracketRegex = new(
        @"\[&[A-Za-z0-9]\]|\(&[A-Za-z0-9]\)|^\s*(?:\[[A-Za-z0-9]\]|\([A-Za-z0-9]\))\s*",
        RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _extraValues = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the format version (default 3).</summary>
    public uint Version { get; set; } = 3;

    /// <summary>Gets or sets the language code (0 = US English).</summary>
    public uint LanguageCode { get; set; } = 0;

    /// <summary>Gets or sets the unknown/useless bytes header value (default 0).</summary>
    public uint UselessBytes { get; set; } = 0;

    /// <summary>Gets all label-value pairs.</summary>
    public IReadOnlyDictionary<string, string> Strings => _strings;

    /// <summary>Gets the count of loaded strings.</summary>
    public int Count => _strings.Count;

    /// <summary>
    /// Loads a CSF file from the specified path.
    /// </summary>
    /// <param name="filePath">Absolute path to the .csf file.</param>
    /// <returns>A loaded <see cref="CsfFile"/> instance.</returns>
    public static CsfFile Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var stream = File.OpenRead(filePath);
        return Load(stream);
    }

    /// <summary>
    /// Loads a CSF file from a stream.
    /// </summary>
    /// <param name="stream">The readable stream containing binary CSF data.</param>
    /// <returns>A loaded <see cref="CsfFile"/> instance.</returns>
    /// <remarks>
    /// For labels containing multiple string pairs, only the primary (first) pair is retained
    /// and serialized on save. Secondary pairs are read to maintain stream alignment.
    /// </remarks>
    public static CsfFile Load(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
        var csf = new CsfFile();

        // Read and verify header
        var header = reader.ReadBytes(4);
        if (header.Length < 4 || !header.AsSpan().SequenceEqual(MagicCsf))
        {
            throw new InvalidDataException("Invalid CSF file header.");
        }

        csf.Version = reader.ReadUInt32();
        var numLabels = reader.ReadUInt32();
        _ = reader.ReadUInt32(); // Discard numStrings (recalculated on write)
        csf.UselessBytes = reader.ReadUInt32();
        csf.LanguageCode = reader.ReadUInt32();

        for (uint i = 0; i < numLabels; i++)
        {
            if (!ReadLabel(reader, stream, csf))
            {
                break;
            }
        }

        return csf;
    }

    /// <summary>
    /// Extracts the hotkey character assigned to this string (the character following '&amp;').
    /// </summary>
    /// <param name="text">The CSF localized text.</param>
    /// <returns>Uppercase hotkey character, or null if none.</returns>
    public static char? ExtractHotkey(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var idx = text.IndexOf('&');
        if (idx >= 0 && idx + 1 < text.Length)
        {
            var ch = text[idx + 1];
            if (char.IsLetterOrDigit(ch))
            {
                return char.ToUpperInvariant(ch);
            }
        }

        // Also check "[X]" prefix if no ampersand was found
        if (text.StartsWith('[') && text.Length >= 3 && text[2] == ']')
        {
            var ch = text[1];
            if (char.IsLetterOrDigit(ch))
            {
                return char.ToUpperInvariant(ch);
            }
        }

        return null;
    }

    /// <summary>
    /// Sets or updates the hotkey in the CSF text without mutating inline words.
    /// </summary>
    /// <param name="text">Existing CSF text.</param>
    /// <param name="hotkey">New hotkey character.</param>
    /// <returns>Updated CSF string with formatted hotkey.</returns>
    public static string SetHotkey(string? text, char hotkey)
    {
        var letter = char.ToUpperInvariant(hotkey);
        if (string.IsNullOrWhiteSpace(text))
        {
            return $"[&{letter}]";
        }

        // 1. If text already has a bracketed indicator like [&X] or (&X), update that indicator
        var bracketMatch = HotkeyBracketRegex.Match(text);
        if (bracketMatch.Success)
        {
            var matchValue = bracketMatch.Value;
            var ampIndex = matchValue.IndexOf('&');
            if (ampIndex >= 0 && ampIndex + 1 < matchValue.Length)
            {
                var replaced = matchValue[..(ampIndex + 1)] + letter + matchValue[(ampIndex + 2)..];
                return text.Remove(bracketMatch.Index, bracketMatch.Length).Insert(bracketMatch.Index, replaced);
            }

            var openBracketIndex = matchValue.IndexOfAny(['[', '(']);
            var closeBracketIndex = matchValue.LastIndexOfAny([']', ')']);
            if (openBracketIndex >= 0 && closeBracketIndex > openBracketIndex)
            {
                var openBracket = matchValue[openBracketIndex];
                var closeBracket = matchValue[closeBracketIndex];
                var replaced = matchValue[..openBracketIndex] + openBracket + $"&{letter}" + closeBracket + matchValue[(closeBracketIndex + 1)..];
                return text.Remove(bracketMatch.Index, bracketMatch.Length).Insert(bracketMatch.Index, replaced);
            }
        }

        // 2. If text had an inline unbracketed '&' (e.g. "Dozer") or no hotkey,
        // strip any existing accelerator marker to avoid word mutation (e.g. "&Dozer" -> "Dozer"),
        // then prefix with the standard bracketed indicator.
        var clean = StripHotkey(text);
        return $"[&{letter}] {clean}";
    }

    /// <summary>
    /// Strips hotkey markers like "[&amp;F]", "(&amp;F)", or "&amp;" from display text.
    /// </summary>
    /// <param name="text">Raw localized string.</param>
    /// <returns>Clean readable title.</returns>
    public static string StripHotkey(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var clean = HotkeyBracketRegex.Replace(text, string.Empty);
        return clean.Replace("&", string.Empty).Trim();
    }

    /// <summary>
    /// Gets the primary string value by its label.
    /// </summary>
    /// <param name="label">CSF label name.</param>
    /// <returns>The primary string value or empty string if not found.</returns>
    public string GetString(string label)
    {
        return _strings.TryGetValue(label, out var val) ? val : string.Empty;
    }

    /// <summary>
    /// Sets or adds a label and value pair in the string table.
    /// </summary>
    /// <param name="label">CSF label name.</param>
    /// <param name="value">String value to assign.</param>
    public void SetString(string label, string value)
    {
        _strings[label] = value;
    }

    /// <summary>
    /// Writes the CSF file to the specified destination path.
    /// </summary>
    /// <param name="filePath">Target destination file path.</param>
    public void Save(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var stream = File.Create(filePath);
        Save(stream);
    }

    /// <summary>
    /// Writes the CSF file to a stream.
    /// </summary>
    /// <param name="stream">Writable output stream.</param>
    public void Save(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);

        // Header: ' FSC', Version (3), NumLabels, NumStrings, UselessBytes (0), LanguageCode
        writer.Write(MagicCsf);
        writer.Write(Version);
        writer.Write((uint)_strings.Count);
        writer.Write((uint)_strings.Count);
        writer.Write(UselessBytes);
        writer.Write(LanguageCode);

        foreach (var (label, val) in _strings)
        {
            // ' LBL', 1 string pair, label length, ASCII label
            writer.Write(MagicLbl);
            writer.Write(1u);
            var labelBytes = Encoding.ASCII.GetBytes(label);
            writer.Write((uint)labelBytes.Length);
            writer.Write(labelBytes);

            bool hasExtra = _extraValues.TryGetValue(label, out var extraVal) && !string.IsNullOrEmpty(extraVal);
            if (hasExtra)
            {
                // 'WRTS', length in 16-bit characters, inverted UTF-16 chars, extra string length, extra ASCII string
                writer.Write(MagicWrts);
                writer.Write((uint)val.Length);
                for (int c = 0; c < val.Length; c++)
                {
                    var raw = (ushort)val[c];
                    writer.Write((ushort)(~raw));
                }

                var extraBytes = Encoding.ASCII.GetBytes(extraVal!);
                writer.Write((uint)extraBytes.Length);
                writer.Write(extraBytes);
            }
            else
            {
                // ' RTS', length in 16-bit characters, inverted UTF-16 chars
                writer.Write(MagicRts);
                writer.Write((uint)val.Length);

                for (int c = 0; c < val.Length; c++)
                {
                    var raw = (ushort)val[c];
                    writer.Write((ushort)(~raw));
                }
            }
        }
    }

    private static bool ReadLabel(BinaryReader reader, Stream stream, CsfFile csf)
    {
        var lblMagic = reader.ReadBytes(4);
        if (lblMagic.Length < 4 || !lblMagic.AsSpan().SequenceEqual(MagicLbl))
        {
            return false;
        }

        var numStringPairs = reader.ReadUInt32();
        var labelLen = reader.ReadUInt32();
        ValidateStreamRemaining(stream, labelLen, "label length");
        var labelBytes = reader.ReadBytes((int)labelLen);
        var labelName = Encoding.ASCII.GetString(labelBytes);

        for (uint s = 0; s < numStringPairs; s++)
        {
            ReadStringPair(reader, stream, csf, labelName, isPrimary: s == 0);
        }

        return true;
    }

    private static void ReadStringPair(BinaryReader reader, Stream stream, CsfFile csf, string labelName, bool isPrimary)
    {
        var rtsMagic = reader.ReadBytes(4);
        if (rtsMagic.Length < 4)
        {
            return;
        }

        var numChars = reader.ReadUInt32();
        ValidateStreamRemaining(stream, (long)numChars * 2, "string characters");

        // Characters are 16-bit UTF-16 inverted with bitwise NOT (~)
        var chars = new char[numChars];
        for (uint c = 0; c < numChars; c++)
        {
            var raw = reader.ReadUInt16();
            chars[c] = (char)~raw;
        }

        var stringValue = new string(chars);
        string? extraValue = null;

        // Handle WRTS extra string if present
        if (rtsMagic[0] == (byte)'W')
        {
            var extraLength = reader.ReadUInt32();
            ValidateStreamRemaining(stream, extraLength, "extra string length");
            var extraBytes = reader.ReadBytes((int)extraLength);
            extraValue = Encoding.ASCII.GetString(extraBytes);
        }

        // Secondary string pairs are discarded for hotkey editing; only primary is retained
        if (isPrimary)
        {
            csf._strings[labelName] = stringValue;
            if (!string.IsNullOrEmpty(extraValue))
            {
                csf._extraValues[labelName] = extraValue;
            }
        }
    }

    private static void ValidateStreamRemaining(Stream stream, long requiredBytes, string fieldName)
    {
        if (requiredBytes < 0 || requiredBytes > int.MaxValue)
        {
            throw new InvalidDataException($"Invalid CSF {fieldName} size: {requiredBytes}");
        }

        if (stream.CanSeek && stream.Length - stream.Position < requiredBytes)
        {
            throw new InvalidDataException($"Unexpected end of stream while reading CSF {fieldName}. Expected {requiredBytes} bytes.");
        }
    }
}
