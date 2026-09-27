namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// Result data when saving catalog details in the Edit Catalog dialog.
/// </summary>
/// <param name="Name">The updated catalog name.</param>
/// <param name="IconUrl">The optional updated catalog icon URL or asset path.</param>
public record RenameCatalogResult(string Name, string? IconUrl);
