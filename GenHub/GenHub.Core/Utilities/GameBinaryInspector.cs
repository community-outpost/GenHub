using GenHub.Core.Constants;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.GameClients;
using GenHub.Core.Models.Results;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Utilities;

/// <summary>
/// Heuristic game-type detection from game binaries, empirically verified on real
/// retail, Steam, community, and native-port engines across PE, ELF, and Mach-O.
/// <para>
/// Detection is a cascade: file-name gates exclude tools and installers first (a
/// WorldBuilder binary contains full Zero Hour markers), then magic bytes separate
/// engines from launchers and data, then streamed ANSI plus UTF-16LE sniffing looks
/// for the Zero Hour-exclusive literals and the Generals-exclusive engine token. A
/// packed stub with zero markers resolves to its sibling engine by the caller;
/// conflicting or absent evidence yields Unknown.
/// </para>
/// </summary>
public static class GameBinaryInspector
{
    private static readonly byte[] AnsiTitle = Encoding.ASCII.GetBytes(GameBinaryConstants.ZeroHourTitle);
    private static readonly byte[] Utf16Title = Encoding.Unicode.GetBytes(GameBinaryConstants.ZeroHourTitle);
    private static readonly byte[] ChallengeMarker = Encoding.ASCII.GetBytes(GameBinaryConstants.ChallengeMenuMarker);
    private static readonly byte[] GeneralsMarker = Encoding.ASCII.GetBytes(GameBinaryConstants.GeneralsEngineMarker);
    private static readonly byte[][] DotNetMarkers = DotNetMarkerBytes();
    private static readonly byte[] TextSectionBytes = Encoding.ASCII.GetBytes(GameBinaryConstants.TextSectionName);

    /// <summary>
    /// Inspects a file on disk, streaming it in chunks so large engines never load fully.
    /// </summary>
    /// <param name="path">Absolute path of the file to inspect.</param>
    /// <param name="cancellationToken">Cancels the chunked scan.</param>
    /// <returns>The role and game-type verdict, or a failure when the file cannot be read.</returns>
    public static async Task<OperationResult<GameBinaryVerdict>> InspectAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(path))
        {
            return OperationResult<GameBinaryVerdict>.CreateFailure("A file path is required for binary inspection.");
        }

        var gated = ApplyFileNameGates(path);
        if (gated is not null)
        {
            return OperationResult<GameBinaryVerdict>.CreateSuccess(gated);
        }

        if (Directory.Exists(path))
        {
            return OperationResult<GameBinaryVerdict>.CreateFailure($"'{path}' is a directory, not a file.");
        }

        if (!File.Exists(path))
        {
            return OperationResult<GameBinaryVerdict>.CreateFailure($"File '{path}' does not exist.");
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, GameBinaryConstants.ScanChunkSize, useAsync: true);
            var header = new byte[Math.Min(GameBinaryConstants.PeHeaderRetainSize, stream.Length)];
            var headerRead = await stream.ReadAtLeastAsync(header.AsMemory(), header.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

            var magic = ClassifyMagic(new ReadOnlySpan<byte>(header, 0, headerRead));
            if (!magic.IsExecutable)
            {
                return OperationResult<GameBinaryVerdict>.CreateSuccess(
                    new GameBinaryVerdict(GameBinaryRole.NotExecutable, GameType.Unknown, "No executable magic bytes."));
            }

            var counts = await SniffStreamAsync(stream, cancellationToken).ConfigureAwait(false);
            var verdict = DecideFromBytes(magic.IsPe, header, headerRead, stream, counts);
            return OperationResult<GameBinaryVerdict>.CreateSuccess(verdict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return OperationResult<GameBinaryVerdict>.CreateFailure($"Cannot inspect '{path}': {ex.Message}");
        }
    }

    /// <summary>
    /// Inspects in-memory bytes with the same cascade as <see cref="InspectAsync"/>.
    /// </summary>
    /// <param name="fileName">File name, for the tool/installer gates and role context.</param>
    /// <param name="bytes">The complete file content.</param>
    /// <returns>The role and game-type verdict.</returns>
    public static GameBinaryVerdict InspectBytes(string fileName, ReadOnlySpan<byte> bytes)
    {
        var gated = ApplyFileNameGates(fileName ?? string.Empty);
        if (gated is not null)
        {
            return gated;
        }

        var magic = ClassifyMagic(bytes);
        if (!magic.IsExecutable)
        {
            return new GameBinaryVerdict(GameBinaryRole.NotExecutable, GameType.Unknown, "No executable magic bytes.");
        }

        var counts = SniffWindow(bytes, windowBase: 0, overlap: 0);
        return DecideVerdict(magic.IsPe, bytes, stream: null, counts);
    }

    /// <summary>
    /// Classifies a file name through the tool/installer gates without touching disk.
    /// </summary>
    /// <param name="fileName">The file name to classify.</param>
    /// <returns>
    /// The excluding role, or <c>null</c> when the name passes. Gates apply to Windows
    /// executables only: Unix names such as <c>setup.bin</c> are legitimate binaries.
    /// </returns>
    public static GameBinaryRole? ClassifyFileName(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (MatchesAnyFragment(fileName, GameBinaryConstants.ToolFileNameFragments))
        {
            return GameBinaryRole.Tool;
        }

        if (MatchesAnyFragment(fileName, GameBinaryConstants.InstallerFileNameFragments))
        {
            return GameBinaryRole.Installer;
        }

        return null;
    }

    private static GameBinaryVerdict? ApplyFileNameGates(string path)
    {
        var fileName = Path.GetFileName(path);
        return ClassifyFileName(fileName) switch
        {
            GameBinaryRole.Tool => new GameBinaryVerdict(GameBinaryRole.Tool, GameType.Unknown, $"Tool file '{fileName}' is excluded before sniffing."),
            GameBinaryRole.Installer => new GameBinaryVerdict(GameBinaryRole.Installer, GameType.Unknown, $"Installer file '{fileName}' is excluded before sniffing."),
            _ => null,
        };
    }

    private static (bool IsExecutable, bool IsPe) ClassifyMagic(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0x4D && bytes[1] == 0x5A)
        {
            return (true, true);
        }

        if (bytes.Length >= 4
            && bytes[0] == 0x7F && bytes[1] == (byte)'E' && bytes[2] == (byte)'L' && bytes[3] == (byte)'F')
        {
            return (true, false);
        }

        if (ExecutableFileClassifier.HasNativeExecutableMagicBytes(bytes))
        {
            return (true, false);
        }

        return (false, false);
    }

    private static async Task<(int Title, int ChallengeMenu, int Generals, int DotNet)> SniffStreamAsync(FileStream stream, CancellationToken cancellationToken)
    {
        var title = 0;
        var challenge = 0;
        var generals = 0;
        var dotnet = 0;
        var chunk = new byte[GameBinaryConstants.ScanChunkSize];
        var overlap = new byte[GameBinaryConstants.ScanOverlapSize];
        var window = new byte[GameBinaryConstants.ScanOverlapSize + GameBinaryConstants.ScanChunkSize];
        var overlapUsed = 0;
        long consumed = 0;

        stream.Seek(0, SeekOrigin.Begin);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            Buffer.BlockCopy(overlap, 0, window, 0, overlapUsed);
            Buffer.BlockCopy(chunk, 0, window, overlapUsed, read);

            var windowLength = overlapUsed + read;
            var found = SniffWindow(window.AsSpan(0, windowLength), windowBase: consumed - overlapUsed, overlap: overlapUsed);
            title += found.Title;
            challenge += found.ChallengeMenu;
            generals += found.Generals;
            dotnet += found.DotNet;

            consumed += read;
            overlapUsed = Math.Min(GameBinaryConstants.ScanOverlapSize, overlapUsed + read);
            Buffer.BlockCopy(window, windowLength - overlapUsed, overlap, 0, overlapUsed);
        }

        return (title, challenge, generals, dotnet);
    }

    private static (int Title, int ChallengeMenu, int Generals, int DotNet) SniffWindow(ReadOnlySpan<byte> window, long windowBase, int overlap)
    {
        var title = CountMatches(window, AnsiTitle, windowBase, overlap, bounded: false)
            + CountMatches(window, Utf16Title, windowBase, overlap, bounded: false);
        var challenge = CountMatches(window, ChallengeMarker, windowBase, overlap, bounded: true);
        var generals = CountMatchesFolded(window, windowBase, overlap);
        var dotnet = 0;
        foreach (var marker in DotNetMarkers)
        {
            dotnet += CountMatches(window, marker, windowBase, overlap, bounded: false);
        }

        return (title, challenge, generals, dotnet);
    }

    private static int CountMatches(ReadOnlySpan<byte> window, byte[] pattern, long windowBase, int overlap, bool bounded)
    {
        var count = 0;
        var from = 0;
        while (from <= window.Length - pattern.Length)
        {
            var relative = window[from..].IndexOf(pattern);
            if (relative < 0)
            {
                break;
            }

            var start = from + relative;

            // A match fully inside the overlap region was already evaluated in the
            // previous window; only seam-spanning and fresh matches count here.
            if ((overlap == 0 || start + pattern.Length > overlap)
                && (!bounded || IsBoundaryStart(window, windowBase, start)))
            {
                count++;
            }

            from = start + 1;
        }

        return count;
    }

    private static int CountMatchesFolded(ReadOnlySpan<byte> window, long windowBase, int overlap)
    {
        var count = 0;
        var from = 0;
        while (from <= window.Length - GeneralsMarker.Length)
        {
            var relative = IndexOfFolded(window[from..]);
            if (relative < 0)
            {
                break;
            }

            var start = from + relative;

            // Same overlap rule as CountMatches: only seam-spanning and fresh matches.
            if ((overlap == 0 || start + GeneralsMarker.Length > overlap)
                && IsBoundaryStart(window, windowBase, start))
            {
                count++;
            }

            from = start + 1;
        }

        return count;
    }

    private static int IndexOfFolded(ReadOnlySpan<byte> haystack)
    {
        for (var i = 0; i + GeneralsMarker.Length <= haystack.Length; i++)
        {
            if (MatchesFoldedAt(haystack, i))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool MatchesFoldedAt(ReadOnlySpan<byte> haystack, int offset)
    {
        for (var j = 0; j < GeneralsMarker.Length; j++)
        {
            var value = haystack[offset + j];
            if (value >= (byte)'A' && value <= (byte)'Z')
            {
                value = (byte)(value + ('a' - 'A'));
            }

            if (value != GeneralsMarker[j])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBoundaryStart(ReadOnlySpan<byte> window, long windowBase, int start)
    {
        if (start + windowBase == 0)
        {
            return true;
        }

        return start > 0 && !IsWordByte(window[start - 1]);
    }

    private static GameBinaryVerdict DecideFromBytes(
        bool isPe,
        byte[] headerBytes,
        int headerLength,
        FileStream? stream,
        (int Title, int ChallengeMenu, int Generals, int DotNet) counts)
    {
        return DecideVerdict(isPe, headerBytes.AsSpan(0, headerLength), stream, counts);
    }

    private static GameBinaryVerdict DecideVerdict(
        bool isPe,
        ReadOnlySpan<byte> header,
        FileStream? stream,
        (int Title, int ChallengeMenu, int Generals, int DotNet) counts)
    {
        if (counts.DotNet > 0)
        {
            return new GameBinaryVerdict(GameBinaryRole.DotNetLauncher, GameType.Unknown, ".NET runtime markers; a launcher, never an engine.");
        }

        var hasZeroHourEvidence = counts.Title > 0 && counts.ChallengeMenu > 0;
        var hasAnyZeroHourMarker = counts.Title > 0 || counts.ChallengeMenu > 0;
        if (hasZeroHourEvidence && counts.Generals == 0)
        {
            return new GameBinaryVerdict(GameBinaryRole.Engine, GameType.ZeroHour, $"Zero Hour title x{counts.Title} with ChallengeMenu x{counts.ChallengeMenu}.");
        }

        if (counts.Generals > 0 && !hasAnyZeroHourMarker)
        {
            return new GameBinaryVerdict(GameBinaryRole.Engine, GameType.Generals, $"Generals engine token x{counts.Generals}.");
        }

        if (hasZeroHourEvidence || counts.Generals > 0)
        {
            return new GameBinaryVerdict(GameBinaryRole.Engine, GameType.Unknown, "Conflicting Generals and Zero Hour evidence.");
        }

        if (isPe && !hasAnyZeroHourMarker && counts.Generals == 0 && IsPackedStub(header, stream))
        {
            return new GameBinaryVerdict(GameBinaryRole.PackedStub, GameType.Unknown, "Marker-less 32-bit PE with packed .text; resolve the sibling engine.");
        }

        return new GameBinaryVerdict(GameBinaryRole.Unknown, GameType.Unknown, "No game-type evidence.");
    }

    private static bool IsPackedStub(ReadOnlySpan<byte> header, FileStream? stream)
    {
        if (!TryParsePeTextSection(header, out var rawPointer, out var rawSize))
        {
            return false;
        }

        Span<byte> sample = stackalloc byte[GameBinaryConstants.EntropySampleSize];
        var available = (int)Math.Min(rawSize, sample.Length);
        if (available < sample.Length / 2)
        {
            return false;
        }

        if (stream is not null)
        {
            if (rawPointer < 0 || rawPointer + available > stream.Length)
            {
                return false;
            }

            try
            {
                stream.Seek(rawPointer, SeekOrigin.Begin);
                if (stream.Read(sample[..available]) < available)
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
            {
                return false;
            }
        }
        else
        {
            if (rawPointer + available > header.Length)
            {
                return false;
            }

            header.Slice((int)rawPointer, available).CopyTo(sample);
        }

        return ComputeEntropy(sample[..available]) >= GameBinaryConstants.PackedTextEntropyThreshold;
    }

    private static bool TryParsePeTextSection(ReadOnlySpan<byte> header, out long rawPointer, out long rawSize)
    {
        rawPointer = 0;
        rawSize = 0;

        if (header.Length < 64 || header[0] != 0x4D || header[1] != 0x5A)
        {
            return false;
        }

        var peOffset = BitConverter.ToInt32(header.Slice(0x3C, 4));
        if (peOffset < 0 || (long)peOffset + 26 > header.Length)
        {
            return false;
        }

        var pe = header.Slice(peOffset);
        if (pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0)
        {
            return false;
        }

        var sectionCount = BitConverter.ToUInt16(pe.Slice(6, 2));
        var optionalSize = BitConverter.ToUInt16(pe.Slice(20, 2));
        if (sectionCount is 0 or > GameBinaryConstants.MaxPeSectionCount || optionalSize > GameBinaryConstants.MaxPeOptionalHeaderSize)
        {
            return false;
        }

        if (BitConverter.ToUInt16(pe.Slice(24, 2)) != 0x10B)
        {
            return false;
        }

        var tableOffset = peOffset + 24 + optionalSize;
        if ((long)tableOffset + (sectionCount * GameBinaryConstants.PeSectionHeaderStride) > header.Length)
        {
            return false;
        }

        return FindTextSection(header, tableOffset, sectionCount, out rawPointer, out rawSize);
    }

    private static bool FindTextSection(ReadOnlySpan<byte> header, int tableOffset, ushort sectionCount, out long rawPointer, out long rawSize)
    {
        rawPointer = 0;
        rawSize = 0;

        for (var i = 0; i < sectionCount; i++)
        {
            var entry = header.Slice(tableOffset + (i * GameBinaryConstants.PeSectionHeaderStride), GameBinaryConstants.PeSectionHeaderStride);
            if (entry.Length >= TextSectionBytes.Length && entry.Slice(0, TextSectionBytes.Length).SequenceEqual(TextSectionBytes))
            {
                rawSize = BitConverter.ToUInt32(entry.Slice(16, 4));
                rawPointer = BitConverter.ToUInt32(entry.Slice(20, 4));
                return rawSize > 0 && rawPointer > 0;
            }
        }

        return false;
    }

    private static double ComputeEntropy(ReadOnlySpan<byte> sample)
    {
        Span<int> frequencies = stackalloc int[256];
        foreach (var value in sample)
        {
            frequencies[value]++;
        }

        var entropy = 0.0;
        foreach (var frequency in frequencies)
        {
            if (frequency == 0)
            {
                continue;
            }

            var probability = (double)frequency / sample.Length;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static bool MatchesAnyFragment(string fileName, string[] fragments)
    {
        if (string.IsNullOrEmpty(fileName))
        {
            return false;
        }

        return fragments.Any(fragment => fileName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsWordByte(byte value)
    {
        return (value >= (byte)'A' && value <= (byte)'Z')
            || (value >= (byte)'a' && value <= (byte)'z')
            || (value >= (byte)'0' && value <= (byte)'9')
            || value == (byte)'_';
    }

    private static byte[][] DotNetMarkerBytes()
    {
        var markers = new byte[GameBinaryConstants.DotNetRuntimeMarkers.Length][];
        for (var i = 0; i < markers.Length; i++)
        {
            markers[i] = Encoding.ASCII.GetBytes(GameBinaryConstants.DotNetRuntimeMarkers[i]);
        }

        return markers;
    }
}
