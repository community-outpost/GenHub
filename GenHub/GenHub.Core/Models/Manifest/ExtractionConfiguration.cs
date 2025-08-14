using GenHub.Core.Models.Enums;

namespace GenHub.Core.Models.Manifest;

/// <summary>
/// Contains information required to download and extract a content package.
/// </summary>
public class ExtractionConfiguration
{
    /// <summary>
    /// Gets or sets the URL from which to download the package.
    /// </summary>
    public string PackageUrl { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected SHA256 hash of the downloaded package for verification.
    /// </summary>
    public string ExpectedHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the package, which determines the extraction method.
    /// </summary>
    public PackageType PackageType { get; set; } = PackageType.Zip;

    /// <summary>
    /// Gets or sets a specific sub-path within the package to extract from.
    /// If null or empty, the entire package is extracted.
    /// </summary>
    public string? ExtractionPath { get; set; }
}
