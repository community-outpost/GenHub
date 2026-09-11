using System.Buffers;

namespace GenHub.Core.Services.Tools.Checksum;

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
    public static void ProcessLines(ReadOnlySpan<byte> data, Action<ReadOnlySpan<byte>> lineVisitor)
    {
        ArgumentNullException.ThrowIfNull(lineVisitor);

        Span<byte> stackBuffer = stackalloc byte[1024];

        while (!data.IsEmpty)
        {
            int newlineIndex = data.IndexOf((byte)'\n');
            ReadOnlySpan<byte> line = newlineIndex >= 0 ? data[..newlineIndex] : data;
            data = newlineIndex >= 0 ? data[(newlineIndex + 1)..] : ReadOnlySpan<byte>.Empty;

            // Strip comment starting with ';'
            int commentIndex = line.IndexOf((byte)';');
            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            if (line.IsEmpty)
            {
                continue;
            }

            byte[]? rented = null;
            Span<byte> normalized = line.Length <= stackBuffer.Length
                ? stackBuffer[..line.Length]
                : (rented = ArrayPool<byte>.Shared.Rent(line.Length)).AsSpan(0, line.Length);

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
}
