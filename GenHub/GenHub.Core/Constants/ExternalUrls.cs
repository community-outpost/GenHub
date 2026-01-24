namespace GenHub.Core.Constants;

/// <summary>
/// Constants for external URLs used for downloading dependencies or tools.
/// </summary>
public static class ExternalUrls
{
    /// <summary>
    /// Download URL for Visual C++ 2010 Redistributable Package (x86).
    /// Required for Generals and Zero Hour to run.
    /// </summary>
    public const string VCRedist2010DownloadUrl = "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe";

    /// <summary>
    /// Gets the primary download URL for DirectX runtime (Microsoft Official).
    /// </summary>
    public const string DirectXRuntimeDownloadUrlPrimary = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";

    /// <summary>
    /// Gets the secondary download URL for DirectX runtime (Gentool).
    /// </summary>
    public const string DirectXRuntimeDownloadUrlMirror1 = "https://gentool.net/program_data/genpatcher/drtx.dat";

    /// <summary>
    /// Download URL for Generals 1.08 official patch.
    /// </summary>
    public const string Generals108PatchUrl = "https://gentool.net/program_data/genpatcher/10gn.dat";

    /// <summary>
    /// Gets the primary download URL for Zero Hour 1.04 patch (CNCNZ).
    /// </summary>
    public const string ZeroHour104PatchUrlPrimary = "http://http.cncnz.com/patches/GeneralsZH-104-english.exe";

    /// <summary>
    /// Gets the secondary download URL for Zero Hour 1.04 patch (Gentool).
    /// </summary>
    public const string ZeroHour104PatchUrlMirror1 = "https://gentool.net/program_data/genpatcher/10zh.dat";

    /// <summary>
    /// Gets the primary download URL for GenTool (Gentool).
    /// </summary>
    public const string GenToolDownloadUrlPrimary = "https://gentool.net/program_data/genpatcher/gent.dat";

    /// <summary>
    /// Gets the secondary download URL for GenTool (Legi.cc).
    /// </summary>
    public const string GenToolDownloadUrlMirror1 = "https://legi.cc/gp2/f/gent.dat";

    /// <summary>
    /// Gets the primary download URL for Visual C++ 2005 Redistributable (Gentool).
    /// </summary>
    public const string VCRedist2005DownloadUrlPrimary = "https://gentool.net/program_data/genpatcher/vcredist_x86-2005.exe";

    /// <summary>
    /// Gets the secondary download URL for Visual C++ 2005 Redistributable (Legi.cc).
    /// </summary>
    public const string VCRedist2005DownloadUrlMirror1 = "https://legi.cc/gp2/f/vc05.dat";

    /// <summary>
    /// Gets the primary download URL for Visual C++ 2008 Redistributable (Gentool).
    /// </summary>
    public const string VCRedist2008DownloadUrlPrimary = "https://gentool.net/program_data/genpatcher/vcredist_x86-2008.exe";

    /// <summary>
    /// Gets the secondary download URL for Visual C++ 2008 Redistributable (Legi.cc).
    /// </summary>
    public const string VCRedist2008DownloadUrlMirror1 = "https://legi.cc/gp2/f/vc08.dat";

    // Legacy support

    /// <summary>
    /// Legacy download URL for DirectX runtime.
    /// </summary>
    public const string DirectXRuntimeDownloadUrl = DirectXRuntimeDownloadUrlPrimary;

    /// <summary>
    /// Legacy download URL for Zero Hour 1.04 patch.
    /// </summary>
    public const string ZeroHour104PatchUrl = ZeroHour104PatchUrlPrimary;

    /// <summary>
    /// Download URL for Intel Graphics Drivers.
    /// </summary>
    public const string IntelDriverDownloadUrl = "https://www.intel.com/content/www/us/en/download-center/home";
}
