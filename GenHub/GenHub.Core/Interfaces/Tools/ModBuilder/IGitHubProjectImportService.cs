using GenHub.Core.Models.Results;
using GenHub.Core.Models.Tools.ModBuilder;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Tools.ModBuilder;

/// <summary>
/// Imports a GitHub repository branch as a ModBuilder project.
/// </summary>
public interface IGitHubProjectImportService
{
    /// <summary>
    /// Downloads a repository branch archive and materializes it as a ModBuilder project.
    /// Repositories that already contain a project file are linked as-is; other
    /// repositories get a generated project that builds their contents.
    /// </summary>
    /// <param name="reference">The repository branch to import.</param>
    /// <param name="targetDirectory">The directory the project is materialized into.</param>
    /// <param name="progress">Optional progress reporter for status messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the imported project file path.</returns>
    Task<OperationResult<string>> ImportRepositoryAsync(
        GitHubRepositoryReference reference,
        string targetDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
