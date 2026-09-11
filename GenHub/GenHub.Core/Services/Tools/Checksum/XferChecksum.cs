using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Implements the SAGE network transfer checksum algorithm used for configuration INI CRC (iniCRC).
/// </summary>
public sealed class XferChecksum
{
    private uint _value;

    /// <summary>
    /// Gets the finalized 32-bit checksum value with endianness reversed to match network wire format.
    /// </summary>
    public uint Value => BinaryPrimitives.ReverseEndianness(_value);

    /// <summary>
    /// Adds byte data to the checksum. Four-byte chunks are read big-endian; partial tails (1-3 bytes) are read little-endian.
    /// </summary>
    /// <param name="data">The byte data to accumulate.</param>
    public void Add(ReadOnlySpan<byte> data)
    {
        while (data.Length >= 4)
        {
            uint chunk = ((uint)data[0] << 24) |
                         ((uint)data[1] << 16) |
                         ((uint)data[2] << 8) |
                         data[3];
            AddBe(chunk);
            data = data[4..];
        }

        switch (data.Length)
        {
            case 1:
                AddBe(data[0]);
                break;
            case 2:
                AddBe((uint)data[0] | ((uint)data[1] << 8));
                break;
            case 3:
                AddBe((uint)data[0] | ((uint)data[1] << 8) | ((uint)data[2] << 16));
                break;
        }
    }

    /// <summary>
    /// Resets the accumulated checksum value to zero.
    /// </summary>
    public void Reset() => _value = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddBe(uint val)
    {
        uint hibit = _value >> 31;
        _value = unchecked((_value << 1) + val + hibit);
    }
}
