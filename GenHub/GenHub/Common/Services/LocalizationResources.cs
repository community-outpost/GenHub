using System.Globalization;
using System.Resources;

namespace GenHub.Common.Services;

/// <summary>
/// Describes the resource set and deployment location used by localization services.
/// </summary>
/// <param name="ResourceManager">The resource manager used for string lookup.</param>
/// <param name="SatelliteAssemblyFileName">The expected satellite assembly file name.</param>
/// <param name="BaseDirectory">The directory containing culture-specific satellite directories.</param>
/// <param name="DefaultCulture">The neutral fallback culture.</param>
internal sealed record LocalizationResources(
    ResourceManager ResourceManager,
    string SatelliteAssemblyFileName,
    string BaseDirectory,
    CultureInfo DefaultCulture);
