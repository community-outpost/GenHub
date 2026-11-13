using GenHub.Common.Services.SecureStorage;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GitHub;

namespace GenHub.Features.GitHub.Services;

/// <summary>
/// GitHub token storage that persists the token AES-GCM encrypted into a user-only file.
/// The encryption key is derived from a machine-bound secret, so a copied file alone is useless.
/// Serves as the shared base for the Linux and macOS implementations.
/// </summary>
public class EncryptedFileGitHubTokenStorage : EncryptedFileTokenStorageBase, IGitHubTokenStorage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedFileGitHubTokenStorage"/> class.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider service.</param>
    public EncryptedFileGitHubTokenStorage(IConfigurationProviderService? configurationProvider = null)
        : base(configurationProvider)
    {
    }

    /// <inheritdoc />
    protected override string TokenFileName => AppConstants.TokenFileName;

    /// <inheritdoc />
    protected override string KeySalt => GitHubConstants.TokenFileKeySalt;
}
