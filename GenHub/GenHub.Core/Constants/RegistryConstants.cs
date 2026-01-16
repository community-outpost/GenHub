namespace GenHub.Core.Constants;

/// <summary>
/// Constants for Registry keys and values.
/// </summary>
public static class RegistryConstants
{
    /// <summary>
    /// The Command &amp; Conquer: The First Decade version value.
    /// </summary>
    public const string TfdVersionValue = "1.03";

    /// <summary>
    /// The registry key for Windows Media Feature Pack components.
    /// </summary>
    public const string WindowsMediaFeaturePackKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\Packages";

    /// <summary>
    /// The registry key for Intel Graphics Driver (Class GUID).
    /// </summary>
    public const string IntelGfxDriverKey = @"SYSTEM\CurrentControlSet\Control\Class\{4D36E968-E325-11CE-BFC1-08002BE10318}\0000";

    /// <summary>
    /// The registry key for Intel MEWiz.
    /// </summary>
    public const string IntelMEWizKey = @"SOFTWARE\Intel\MEWiz1.0";
}
