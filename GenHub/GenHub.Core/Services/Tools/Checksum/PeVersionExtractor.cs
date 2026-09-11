using System.Diagnostics;

namespace GenHub.Core.Services.Tools.Checksum;

/// <summary>
/// Extracts the engine version (major and minor) from SAGE game executables using PE bytecode pattern matching.
/// </summary>
public static class PeVersionExtractor
{
    /// <summary>
    /// Attempts to extract major and minor engine versions from executable bytes.
    /// </summary>
    /// <param name="data">The raw bytes of the executable.</param>
    /// <param name="major">The extracted major version number.</param>
    /// <param name="minor">The extracted minor version number.</param>
    /// <returns><c>true</c> if a unique Version::setVersion sequence was identified; otherwise, <c>false</c>.</returns>
    public static bool TryExtract(ReadOnlySpan<byte> data, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        int foundMajor = -1;
        int foundMinor = -1;
        int matchCount = 0;

        for (int offset = 0; offset < data.Length - 16; offset++)
        {
            ReadOnlySpan<byte> slice = data[offset..];
            if (MatchMsvc(slice, out int maj, out int min) ||
                MatchVc6(slice, out maj, out min) ||
                MatchOptimizedMsvc(slice, out maj, out min))
            {
                matchCount++;
                foundMajor = maj;
                foundMinor = min;
                if (matchCount > 1)
                {
                    // Ambiguous match
                    return false;
                }
            }
        }

        if (matchCount == 1)
        {
            major = foundMajor;
            minor = foundMinor;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Attempts to extract or resolve engine version from an executable file path.
    /// </summary>
    /// <param name="executablePath">The path to the game executable.</param>
    /// <param name="major">The resolved major version number.</param>
    /// <param name="minor">The resolved minor version number.</param>
    /// <returns><c>true</c> if version was successfully resolved; otherwise, <c>false</c>.</returns>
    public static bool TryExtractFromFile(string executablePath, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (!File.Exists(executablePath))
        {
            return false;
        }

        try
        {
            byte[] data = File.ReadAllBytes(executablePath);
            if (TryExtract(data, out major, out minor))
            {
                return true;
            }
        }
        catch
        {
            // Fall through to FileVersionInfo
        }

        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            if (info.ProductMajorPart > 0)
            {
                major = info.ProductMajorPart;
                minor = info.ProductMinorPart;
                return true;
            }
        }
        catch
        {
            // Ignore
        }

        return false;
    }

    private static bool ConsumePush(ref ReadOnlySpan<byte> data)
    {
        if (data.Length >= 2 && data[0] == 0x6A)
        {
            data = data[2..];
            return true;
        }

        if (data.Length >= 5 && data[0] == 0x68)
        {
            data = data[5..];
            return true;
        }

        return false;
    }

    private static bool MatchMsvc(ReadOnlySpan<byte> data, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (!ConsumePush(ref data))
        {
            return false;
        }

        if (!ConsumePush(ref data))
        {
            return false;
        }

        if (data.Length < 2 || data[0] != 0x6A)
        {
            return false;
        }

        minor = data[1];
        data = data[2..];

        if (data.Length < 2 || data[0] != 0x6A)
        {
            return false;
        }

        major = data[1];
        data = data[2..];

        if (data.Length < 12 ||
            data[0] != 0xC6 || data[1] != 0x45 || data[3] != 0 ||
            data[4] != 0x8B || data[5] != 0x0D || data[10] != 0xE8)
        {
            return false;
        }

        return true;
    }

    private static bool MatchVc6(ReadOnlySpan<byte> data, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (data.Length < 7 || data[0] != 0x8B || data[1] != 0x0D || data[6] < 0x50 || data[6] > 0x57)
        {
            return false;
        }

        data = data[7..];
        if (!ConsumePush(ref data) || data.Length < 4 || data[0] != 0x6A || data[2] != 0x6A)
        {
            return false;
        }

        minor = data[1];
        major = data[3];
        data = data[4..];

        if (data.Length < 8 || data[0] != 0xC6 || data[1] != 0x45 || data[3] != 0 || data[4] != 0xE8)
        {
            return false;
        }

        return true;
    }

    private static bool MatchOptimizedMsvc(ReadOnlySpan<byte> data, out int major, out int minor)
    {
        major = 0;
        minor = 0;

        if (!ConsumePush(ref data))
        {
            return false;
        }

        if (!ConsumePush(ref data))
        {
            return false;
        }

        if (data.Length < 2 || data[0] != 0x6A)
        {
            return false;
        }

        minor = data[1];
        data = data[2..];

        if (data.Length < 17 ||
            data[0] != 0xC6 || data[1] != 0x45 || data[3] != 0 ||
            data[4] != 0x8B || data[5] != 0x0D || data[10] != 0x6A || data[12] != 0xE8)
        {
            return false;
        }

        major = data[11];
        return true;
    }
}
