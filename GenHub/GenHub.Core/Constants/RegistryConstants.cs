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

    /// <summary>Registry value name for TFD Version.</summary>
    public const string TfdVersionValue = "1.03";

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

    /// <summary>Registry value name for Origin Client Path.</summary>
    public const string OriginClientPathValue = "ClientPath";
}
