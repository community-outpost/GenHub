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
    /// <para>
    /// Only the two targets that are definitively not user data are listed as such: every other
    /// value, including any added later, is treated as user-writable and therefore copied. That
    /// matches the resolver, whose own default arm places unmapped targets inside the user data
    /// root, and it fails towards an extra copy rather than towards a hard link into Documents.
    /// </para>
    /// </summary>
    /// <param name="installTarget">The install target to inspect.</param>
    /// <returns><c>true</c> when the destination is user-writable; otherwise, <c>false</c>.</returns>
    public static bool IsUserWritableTarget(this ContentInstallTarget installTarget) => installTarget switch
    {
        ContentInstallTarget.Workspace => false,
        ContentInstallTarget.System => false,
        _ => true,
    };
}
