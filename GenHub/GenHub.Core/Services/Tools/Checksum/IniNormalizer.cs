using System;
using System.Buffers;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Delegate for visiting a span of bytes for a normalized INI line.
/// </summary>
/// <param name="lineSpan">The line span.</param>
public delegate void LineSpanVisitor(ReadOnlySpan<byte> lineSpan);

/// <summary>
/// Normalizes SAGE INI lines by stripping comments and replacing ASCII control characters with spaces.
/// </summary>
public static class IniNormalizer
{
    /// <summary>
    /// Processes INI content line by line, stripping comments and replacing control characters.
    /// </summary>
    /// <param name="data">The raw INI file bytes.</param>
    /// <param name="lineVisitor">Callback invoked for each non-empty normalized line.</param>
    public static void ProcessLines(ReadOnlySpan<byte> data, LineSpanVisitor lineVisitor)
    {
        ArgumentNullException.ThrowIfNull(lineVisitor);

        Span<byte> stackBuffer = stackalloc byte[1024];

        while (!data.IsEmpty)
        {
            ReadOnlySpan<byte> line = GetNextLine(ref data);
            if (!line.IsEmpty)
            {
                NormalizeAndVisit(line, stackBuffer, lineVisitor);
            }
        }
    }

    private static ReadOnlySpan<byte> GetNextLine(ref ReadOnlySpan<byte> data)
    {
        int newlineIndex = data.IndexOf((byte)'\n');
        ReadOnlySpan<byte> line = newlineIndex >= 0 ? data[..newlineIndex] : data;
        data = newlineIndex >= 0 ? data[(newlineIndex + 1)..] : ReadOnlySpan<byte>.Empty;

        int commentIndex = line.IndexOf((byte)';');
        return commentIndex >= 0 ? line[..commentIndex] : line;
    }

    private static void NormalizeAndVisit(ReadOnlySpan<byte> line, Span<byte> stackBuffer, LineSpanVisitor lineVisitor)
    {
        byte[]? rented = null;
        Span<byte> normalized;
        if (line.Length <= stackBuffer.Length)
        {
            normalized = stackBuffer[..line.Length];
        }
        else
        {
            rented = ArrayPool<byte>.Shared.Rent(line.Length);
            normalized = rented.AsSpan(0, line.Length);
        }

        try
        {
            for (int i = 0; i < line.Length; i++)
            {
                byte b = line[i];
                normalized[i] = (b > 0 && b < 32) ? (byte)' ' : b;
            }

            lineVisitor(normalized);
        }
        finally
        {
            if (rented != null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
