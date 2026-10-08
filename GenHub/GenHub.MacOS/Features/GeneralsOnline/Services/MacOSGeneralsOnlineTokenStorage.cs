using GenHub.Core.Interfaces.Common;
using GenHub.Features.GeneralsOnline.Services;

namespace GenHub.MacOS.Features.GeneralsOnline.Services;

/// <summary>
/// macOS Generals Online refresh token storage using AES-GCM encrypted file persistence.
/// </summary>
/// <param name="configurationProvider">Optional configuration provider service.</param>
public class MacOSGeneralsOnlineTokenStorage(IConfigurationProviderService? configurationProvider = null)
    : EncryptedFileGeneralsOnlineTokenStorage(configurationProvider);
