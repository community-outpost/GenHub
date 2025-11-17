namespace GenHub.Tests.Core.Features.AppUpdate.Services;

/// <summary>
/// Minimal stub interface for testing.
/// </summary>
public interface IReleasesClient
{
    /// <summary>
    /// Gets the latest release for the specified owner and repo.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <returns>The latest <see cref="Release"/>.</returns>
    Task<Release> GetLatest(string owner, string repo);

    /// <summary>
    /// Gets all releases for the specified owner and repo.
    /// </summary>
    /// <param name="owner">The repository owner.</param>
    /// <param name="repo">The repository name.</param>
    /// <returns>A list of <see cref="Release"/>.</returns>
    Task<IReadOnlyList<Release>> GetAll(string owner, string repo);
}

/// <summary>
/// Stub implementation for IReleasesClient for testing.
/// </summary>
public class ReleasesClientStub : IReleasesClient
{
    private readonly Release? _release;

    /// <summary>
    /// Initializes a new instance of the <see cref="ReleasesClientStub"/> class.
    /// </summary>
    /// <param name="release">The release to return from stub methods.</param>
    public ReleasesClientStub(Release? release)
    {
        _release = release;
    }

    /// <inheritdoc/>
    public Task<Release> GetLatest(string owner, string repo)
    {
        if (_release == null)
        {
            throw new System.Exception("NotFound");
        }

        return Task.FromResult(_release);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<Release>> GetAll(string owner, string repo)
    {
        if (_release == null)
        {
            return Task.FromResult((IReadOnlyList<Release>)new List<Release>());
        }

        return Task.FromResult((IReadOnlyList<Release>)new List<Release> { _release });
    }
}