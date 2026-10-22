using GenHub.Core.Interfaces.Common;
using GenHub.Features.GitHub.Services;

namespace GenHub.Linux.Features.GitHub.Services;

/// <summary>
/// Linux GitHub token storage using AES-GCM encrypted file persistence.
/// </summary>
/// <param name="configurationProvider">Optional configuration provider service.</param>
public class LinuxGitHubTokenStorage(IConfigurationProviderService? configurationProvider = null)
    : EncryptedFileGitHubTokenStorage(configurationProvider);
