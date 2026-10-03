using GenHub.Common.Services.SecureStorage;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GeneralsOnline;

namespace GenHub.Features.GeneralsOnline.Services;

/// <summary>
/// Generals Online refresh token storage that persists the token AES-GCM encrypted
/// into a user-only file. Serves as the shared base for the Linux and macOS implementations.
/// </summary>
public class EncryptedFileGeneralsOnlineTokenStorage : EncryptedFileTokenStorageBase, IGeneralsOnlineTokenStorage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EncryptedFileGeneralsOnlineTokenStorage"/> class.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider service.</param>
    public EncryptedFileGeneralsOnlineTokenStorage(IConfigurationProviderService? configurationProvider = null)
        : base(configurationProvider)
    {
    }

    /// <inheritdoc />
    protected override string TokenFileName => GeneralsOnlineConstants.TokenFileName;

    /// <inheritdoc />
    protected override string KeySalt => GeneralsOnlineConstants.TokenFileKeySalt;
}
