namespace GenHub.Core.Constants;

/// <summary>
/// Constants used by the application localization infrastructure.
/// </summary>
public static class LocalizationConstants
{
    /// <summary>
    /// The neutral culture embedded in the main application assembly.
    /// </summary>
    public const string DefaultCultureName = "en";

    /// <summary>
    /// The property name used to notify bindings that all indexer values changed.
    /// </summary>
    public const string IndexerPropertyName = "Item";

    /// <summary>
    /// The application resource key used to expose the localization service to XAML.
    /// </summary>
    public const string ResourceServiceKey = "LocalizationService";

    /// <summary>
    /// The fully qualified base name of the application's string resources.
    /// </summary>
    public const string StringResourceBaseName = "GenHub.Resources.Localization.Strings";

    /// <summary>
    /// The suffix used by .NET satellite resource assemblies.
    /// </summary>
    public const string SatelliteAssemblySuffix = ".resources.dll";
}
