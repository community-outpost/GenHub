namespace GenHub.Core.Models.Enums;

/// <summary>
/// Role of a scanned file, from pure managed byte inspection (no Win32 API).
/// </summary>
public enum GameBinaryRole
{
    /// <summary>Unclassified: executable-shaped but role and type are undetermined.</summary>
    Unknown,

    /// <summary>Engine binary carrying game-type markers.</summary>
    Engine,

    /// <summary>Packed/DRM launcher stub: native PE with encrypted sections and no markers.</summary>
    PackedStub,

    /// <summary>.NET apphost launcher: carries runtime markers, never an engine.</summary>
    DotNetLauncher,

    /// <summary>Modding tool excluded by file name before sniffing.</summary>
    Tool,

    /// <summary>Installer excluded by file name before sniffing.</summary>
    Installer,

    /// <summary>Not executable bytes at all (script, data, text).</summary>
    NotExecutable,
}
