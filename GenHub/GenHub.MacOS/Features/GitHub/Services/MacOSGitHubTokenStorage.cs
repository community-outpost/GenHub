using GenHub.Core.Interfaces.Common;
using GenHub.Features.GitHub.Services;

namespace GenHub.MacOS.Features.GitHub.Services;

/// <summary>
/// macOS GitHub token storage using AES-GCM encrypted file persistence.
/// </summary>
/// <param name="configurationProvider">Optional configuration provider service.</param>
public class MacOSGitHubTokenStorage(IConfigurationProviderService? configurationProvider = null)
    : EncryptedFileGitHubTokenStorage(configurationProvider);
