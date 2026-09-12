namespace GenHub.Features.Downloads.ViewModels.Filters;

/// <summary>
/// Represents the available ModDB sections.
/// </summary>
public enum ModDBSection
{
    /// <summary>Downloads/Files section with category + categoryaddon filters.</summary>
    Downloads,

    /// <summary>Addons section with addon category + licence filters.</summary>
    Addons,

    /// <summary>Mods section.</summary>
    Mods,
}
