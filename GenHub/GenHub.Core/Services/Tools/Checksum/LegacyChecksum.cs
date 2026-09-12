namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Implements the legacy SAGE engine checksum algorithm (ROL 1 + ADD) used for executable CRC (exeCRC).
/// </summary>
public sealed class LegacyChecksum
{
    private uint _value;

    /// <summary>
    /// Gets the current accumulated 32-bit checksum value.
    /// </summary>
    public uint Value => _value;

    /// <summary>
    /// Adds a byte slice to the checksum.
    /// </summary>
    /// <param name="data">The byte data to accumulate.</param>
    public void Add(ReadOnlySpan<byte> data)
    {
        uint val = _value;
        for (int i = 0; i < data.Length; i++)
        {
            uint hibit = val >> 31;
            val = unchecked((val << 1) + data[i] + hibit);
        }

        _value = val;
    }

    /// <summary>
    /// Resets the accumulated checksum value to zero.
    /// </summary>
    public void Reset() => _value = 0;
}
