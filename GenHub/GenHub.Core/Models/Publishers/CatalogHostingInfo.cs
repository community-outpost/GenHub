namespace GenHub.Core.Models.Publishers;

/// <summary>
/// Hosting info for a catalog file.
/// </summary>
public class CatalogHostingInfo : HostedFileInfo
{
    /// <summary>
    /// Gets or sets the catalog ID this hosting info corresponds to.
    /// </summary>
    public string CatalogId { get; set; } = string.Empty;
}
