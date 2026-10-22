using GenHub.Core.Interfaces.Common;
using GenHub.Features.GeneralsOnline.Services;

namespace GenHub.Linux.Features.GeneralsOnline.Services;

/// <summary>
/// Linux Generals Online refresh token storage using AES-GCM encrypted file persistence.
/// </summary>
/// <param name="configurationProvider">Optional configuration provider service.</param>
public class LinuxGeneralsOnlineTokenStorage(IConfigurationProviderService? configurationProvider = null)
    : EncryptedFileGeneralsOnlineTokenStorage(configurationProvider);
