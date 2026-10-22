using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Windows.Features.Common.Services;

namespace GenHub.Windows.Features.GitHub.Services;

/// <summary>
/// Windows-specific GitHub token storage using DPAPI.
/// </summary>
public class WindowsGitHubTokenStorage : DpapiFileTokenStorageBase, IGitHubTokenStorage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsGitHubTokenStorage"/> class.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider service.</param>
    public WindowsGitHubTokenStorage(IConfigurationProviderService? configurationProvider = null)
        : base(configurationProvider)
    {
    }

    /// <inheritdoc />
    protected override string TokenFileName => AppConstants.TokenFileName;
}
