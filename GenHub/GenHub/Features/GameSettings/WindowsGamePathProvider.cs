using System;

namespace GenHub.Features.GameSettings;

/// <summary>
/// Windows implementation of <see cref="Core.Interfaces.GameSettings.IGamePathProvider"/>.
/// <para>
/// Resolves to <c>Documents/Command and Conquer Generals Zero Hour Data</c>, matching
/// <c>GlobalData::BuildUserDataPathFromRegistry</c> in the game engine, which uses
/// <c>SHGetKnownFolderPath(FOLDERID_Documents)</c> so that OneDrive and Group Policy
/// folder redirection are honoured. <c>SpecialFolder.MyDocuments</c> resolves through
/// the same known-folder mechanism.
/// </para>
/// </summary>
public sealed class WindowsGamePathProvider : GamePathProviderBase
{
    /// <inheritdoc/>
    protected override string GetUserDataBaseDirectory() =>
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
}
