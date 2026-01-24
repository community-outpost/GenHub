using System.Threading.Tasks;

namespace GenHub.Features.Info.ViewModels;

/// <summary>
/// Interface for a section within the Info tab.
/// </summary>
public interface IInfoSectionViewModel
{
    /// <summary>
    /// Gets the title of the section.
    /// </summary>
    string Title { get; }

    /// <summary>
    /// Gets the unique identifier for this section.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Gets the sort order of the section.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// Initializes the section asynchronously.
    /// </summary>
    /// <returns>A task representing the initialization operation.</returns>
    Task InitializeAsync();
}
