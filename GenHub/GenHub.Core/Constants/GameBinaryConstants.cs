namespace GenHub.Core.Constants;

/// <summary>
/// Byte markers and thresholds for heuristic game-binary inspection.
/// <para>
/// Verified against real engine binaries (retail, Steam, community, native ports) on
/// Windows PE, Linux ELF, and macOS Mach-O: the Zero Hour engine carries exclusive
/// string literals, but installs also contain packed stubs, .NET launchers, tools, and
/// installers that naive string matching misclassifies. Consumers must classify the
/// file role first and sniff only engine candidates.
/// </para>
/// <para>
/// Traps that must never become markers: bare <c>ccgenerals</c> and <c>ccgenzh</c>
/// (both appear contiguously inside Zero Hour binaries via EA FTP patch code and
/// GameSpy URLs), <c>GeneralsOnline</c> (the original in-game GameSpy feature, present
/// in the retail engine), and the short <c>Command and Conquer Generals</c> title (the
/// shared game-window caption, present in both engines). Only the ampersand form
/// <c>c&amp;cgenerals</c> is Generals-exclusive: observed in real Generals engines,
/// absent from every scanned Zero Hour binary.
/// </para>
/// </summary>
public static class GameBinaryConstants
{
    /// <summary>
    /// Zero Hour-exclusive window/registry title, ANSI or UTF-16LE. UTF-16 must be
    /// OR-never-required: some retail and Steam engines carry only the ANSI copy.
    /// </summary>
    public const string ZeroHourTitle = "Command and Conquer Generals Zero Hour";

    /// <summary>
    /// Zero Hour-exclusive menu-system token from the engine function-lexicon and GUI
    /// callback tables. Match only when not preceded by an ASCII letter or digit;
    /// trailing capitals (System, Input, Init) are part of the real table entries.
    /// </summary>
    public const string ChallengeMenuMarker = "ChallengeMenu";

    /// <summary>
    /// Generals-exclusive engine token, matched ASCII case-insensitively with the same
    /// word-boundary rule as <see cref="ChallengeMenuMarker"/>. The bare forms without
    /// the ampersand appear inside Zero Hour binaries and must never match.
    /// </summary>
    public const string GeneralsEngineMarker = "c&cgenerals";

    /// <summary>
    /// Shannon entropy at or above which a 32-bit <c>.text</c> section with zero markers
    /// reads as packed/encrypted stub code. Real engines measure near 5.1; packed DRM
    /// stubs measure near 8.0.
    /// </summary>
    public const double PackedTextEntropyThreshold = 7.5;

    /// <summary>PE section sampled for packed-stub detection.</summary>
    public const string TextSectionName = ".text";

    /// <summary>Bytes of <c>.text</c> sampled for the entropy check.</summary>
    public const int EntropySampleSize = 4096;

    /// <summary>Streaming scan chunk size for marker sniffing.</summary>
    public const int ScanChunkSize = 65536;

    /// <summary>
    /// Overlap between scan chunks. Exceeds every marker length so chunk seams are exact.
    /// </summary>
    public const int ScanOverlapSize = 256;

    /// <summary>Header bytes retained for PE structure parsing.</summary>
    public const int PeHeaderRetainSize = 8192;

    /// <summary>
    /// Section header table entry stride in bytes for PE files.
    /// </summary>
    public const int PeSectionHeaderStride = 40;

    /// <summary>
    /// Maximum allowed PE optional header size in bytes during section parsing sanity checks.
    /// </summary>
    public const int MaxPeOptionalHeaderSize = 1024;

    /// <summary>Sanity bound for the PE section-table walk.</summary>
    public const int MaxPeSectionCount = 96;

    /// <summary>
    /// Markers identifying a .NET apphost launcher (never an engine), matched anywhere.
    /// </summary>
    public static readonly string[] DotNetRuntimeMarkers =
    [
        "mscorlib",
        "mscoree",
        "_CorExeMain",
    ];

    /// <summary>
    /// File-name fragments identifying modding tools, matched case-insensitively. Tools
    /// contain full engine markers and must be excluded before sniffing.
    /// </summary>
    public static readonly string[] ToolFileNameFragments =
    [
        "worldbuilder",
    ];

    /// <summary>
    /// File-name fragments identifying installers, matched case-insensitively. Installers
    /// carry branding strings only and are never game clients.
    /// </summary>
    public static readonly string[] InstallerFileNameFragments =
    [
        "setup",
        "installer",
    ];
}
