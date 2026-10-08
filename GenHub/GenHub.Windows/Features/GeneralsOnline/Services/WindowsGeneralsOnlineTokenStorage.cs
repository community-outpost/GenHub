using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GeneralsOnline;
using GenHub.Windows.Features.Common.Services;

namespace GenHub.Windows.Features.GeneralsOnline.Services;

/// <summary>
/// Windows-specific Generals Online refresh token storage using DPAPI.
/// </summary>
public class WindowsGeneralsOnlineTokenStorage : DpapiFileTokenStorageBase, IGeneralsOnlineTokenStorage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WindowsGeneralsOnlineTokenStorage"/> class.
    /// </summary>
    /// <param name="configurationProvider">Optional configuration provider service.</param>
    public WindowsGeneralsOnlineTokenStorage(IConfigurationProviderService? configurationProvider = null)
        : base(configurationProvider)
    {
    }

    /// <inheritdoc />
    protected override string TokenFileName => GeneralsOnlineConstants.TokenFileName;
}
