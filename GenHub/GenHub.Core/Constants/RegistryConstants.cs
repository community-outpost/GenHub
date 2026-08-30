namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Windows Registry keys and values.
/// </summary>
public static class RegistryConstants
{
    // ===== EA App / Origin Keys =====

    /// <summary>Registry key path for Generals command and conquer.</summary>
    public const string EAAppGeneralsKeyPath = @"SOFTWARE\Electronic Arts\EA Games\Generals";

    /// <summary>Registry key path for Zero Hour.</summary>
    public const string EAAppZeroHourKeyPath = @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour";

    /// <summary>Registry key path for Generals Ergc (Serial).</summary>
    public const string EAAppGeneralsErgcKeyPath = @"SOFTWARE\Electronic Arts\EA Games\Generals\ergc";

    /// <summary>Registry key path for Zero Hour Ergc (Serial).</summary>
    public const string EAAppZeroHourErgcKeyPath = @"SOFTWARE\Electronic Arts\EA Games\Command and Conquer Generals Zero Hour\ergc";

    // ===== VCRedist Keys =====

    /// <summary>Squished (compressed) GUID for Visual C++ 2005 Redistributable x86.</summary>
    public const string VCRedist2005SquishedGuid = "b25099274a207264182f8181add555d0";

    /// <summary>Registry key for VCRedist 2005 in Installer UserData Products.</summary>
    public const string VCRedist2005InstallerProductsKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products\" + VCRedist2005SquishedGuid;

    /// <summary>Registry key for VCRedist 2005 in WOW6432Node Installer UserData Products.</summary>
    public const string VCRedist2005InstallerProductsKeyWow64 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Installer\UserData\S-1-5-18\Products\" + VCRedist2005SquishedGuid;

    /// <summary>Registry key for VCRedist 2005 in Classes Installer Products.</summary>
    public const string VCRedist2005ClassesKey = @"SOFTWARE\Classes\Installer\Products\" + VCRedist2005SquishedGuid;

    /// <summary>Registry key for VCRedist 2010 x86 (32-bit).</summary>
    public const string VCRedist2010x86Key = @"SOFTWARE\Microsoft\VisualStudio\10.0\VC\VCRedist\x86";

    /// <summary>Registry key for VCRedist 2010 x86 (64-bit environment / WOW6432Node).</summary>
    public const string VCRedist2010x86KeyWow64 = @"SOFTWARE\WOW6432Node\Microsoft\VisualStudio\10.0\VC\VCRedist\x86";

    // ===== Value Names =====

    /// <summary>Registry value name for 'Install Path'.</summary>
    public const string InstallPathValueName = "Install Path";

    /// <summary>Registry value name for 'Version'.</summary>
    public const string VersionValueName = "Version";

    /// <summary>Registry value name for 'Installed'.</summary>
    public const string InstalledValueName = "Installed";

    // ===== Registry Versions (DWORD) =====

    /// <summary>Registry version for Generals 1.08 (0x10008).</summary>
    public const int GeneralsVersionDWord = 0x10008;

    /// <summary>Registry version for Zero Hour 1.04 (0x10004).</summary>
    public const int ZeroHourVersionDWord = 0x10004;

    // ===== Windows System Keys =====

    /// <summary>Registry key path for Windows Compatibility Flags (AppCompatLayers).</summary>
    public const string AppCompatLayersKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    // ===== The First Decade Keys =====

    /// <summary>Registry key path for The First Decade.</summary>
    public const string TheFirstDecadeKeyPath = @"SOFTWARE\EA Games\Command & Conquer The First Decade";

    /// <summary>The First Decade registry version data string ("1.03").</summary>
    public const string TfdVersionData = "1.03";

    /// <summary>Registry value data for TFD Version (alias for backward compatibility).</summary>
    public const string TfdVersionValue = TfdVersionData;

    // ===== C&C Online (Revora) Keys =====

    /// <summary>Registry key path for C&amp;C Online (Root).</summary>
    public const string CncOnlineKeyPath = @"SOFTWARE\Revora\CNCOnline";

    /// <summary>Registry key path for C&amp;C Online Generals.</summary>
    public const string CncOnlineGeneralsKeyPath = @"SOFTWARE\Revora\CNCOnline\Generals";

    /// <summary>Registry key path for C&amp;C Online Zero Hour.</summary>
    public const string CncOnlineZeroHourKeyPath = @"SOFTWARE\Revora\CNCOnline\ZeroHour";

    /// <summary>C&amp;C Online Version.</summary>
    public const string CncOnlineVersion = "1.0";

    /// <summary>C&amp;C Online Generals Version.</summary>
    public const string CncOnlineGeneralsVersion = "1.08";

    /// <summary>C&amp;C Online Zero Hour Version.</summary>
    public const string CncOnlineZeroHourVersion = "1.04";

    // ===== Malwarebytes Keys =====

    /// <summary>Registry key path for Uninstall (used for detection).</summary>
    public const string UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    /// <summary>Registry value name for DisplayName.</summary>
    public const string DisplayNameValueName = "DisplayName";

    // ===== Intel Graphics Keys =====

    /// <summary>Registry key path for Intel Graphics Class.</summary>
    public const string IntelGraphicsClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E968-E325-11CE-BFC1-08002BE10318}";

    /// <summary>Registry key path for Intel MEWiz.</summary>
    public const string IntelMEWizKeyPath = @"SOFTWARE\Intel\MEWiz1.0";

    // ===== Windows Media Feature Pack =====

    /// <summary>Registry key path for Windows Media Player Feature.</summary>
    public const string WindowsMediaPlayerFeatureKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\WindowsFeatures\WindowsMediaPlayer";

    // ===== Origin Keys =====

    /// <summary>Registry key path for Origin.</summary>
    public const string OriginKeyPath = @"SOFTWARE\Origin";

    /// <summary>Registry key path for Origin in WOW6432Node.</summary>
    public const string OriginKeyPathWow64 = @"SOFTWARE\WOW6432Node\Origin";

    /// <summary>Registry value name for Origin Client Path.</summary>
    public const string OriginClientPathValue = "ClientPath";

    // ===== WOW64 Uninstall Key =====

    /// <summary>Registry key path for 32-bit Uninstall under WOW64.</summary>
    public const string UninstallKeyPathWow64 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall";

    // ===== TCP/IP IPv6 Parameters =====

    /// <summary>Registry key path for TCPIP6 Parameters.</summary>
    public const string Tcpip6ParametersKeyPath = @"SYSTEM\CurrentControlSet\Services\Tcpip6\Parameters";

    /// <summary>Registry value name for DisabledComponents.</summary>
    public const string DisabledComponentsValueName = "DisabledComponents";

    /// <summary>Registry DWORD value for Prefer IPv4 over IPv6 (0x20 = 32).</summary>
    public const int PreferIPv4DisabledComponentsValue = 32;

    // ===== Fonts =====

    /// <summary>Registry key path for Windows Fonts.</summary>
    public const string FontsKeyPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    /// <summary>Registry font value name for Arial TrueType font.</summary>
    public const string ArialFontValueName = "Arial (TrueType)";

    // ===== Component Based Servicing (CBS) =====

    /// <summary>Registry key path for CBS Packages.</summary>
    public const string CbsPackagesKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages";

    /// <summary>Registry value name for CBS InstallState.</summary>
    public const string InstallStateValueName = "InstallState";

    /// <summary>CBS InstallState: Installed (7).</summary>
    public const int CbsInstallStateInstalled = 7;

    /// <summary>CBS InstallState: Staged (112).</summary>
    public const int CbsInstallStateStaged = 112;

    /// <summary>CBS InstallState: Superseded (128).</summary>
    public const int CbsInstallStateSuperseded = 128;

    // ===== WMI Constants =====

    /// <summary>WMI Scope for CIMV2.</summary>
    public const string WmiScopeCimV2 = @"root\CIMV2";

    /// <summary>WMI Query for Video Controller.</summary>
    public const string WmiQueryVideoController = "SELECT * FROM Win32_VideoController";
}
