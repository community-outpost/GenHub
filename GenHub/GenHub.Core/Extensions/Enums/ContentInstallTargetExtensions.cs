using GenHub.Core.Models.Enums;

namespace GenHub.Core.Extensions.Enums;

/// <summary>
/// Provides extension methods for the <see cref="ContentInstallTarget"/> enum.
/// </summary>
public static class ContentInstallTargetExtensions
{
    /// <summary>
    /// Determines whether the target resolves to a directory the user and the game engine
    /// write to directly, which means deployed content must never share storage with the
    /// content-addressable object it originated from.
    /// </summary>
    /// <param name="installTarget">The install target to inspect.</param>
    /// <returns><c>true</c> when the destination is user-writable; otherwise, <c>false</c>.</returns>
    public static bool IsUserWritableTarget(this ContentInstallTarget installTarget) => installTarget switch
    {
        ContentInstallTarget.UserDataDirectory => true,
        ContentInstallTarget.UserMapsDirectory => true,
        ContentInstallTarget.UserReplaysDirectory => true,
        ContentInstallTarget.UserScreenshotsDirectory => true,
        _ => false,
    };
}
