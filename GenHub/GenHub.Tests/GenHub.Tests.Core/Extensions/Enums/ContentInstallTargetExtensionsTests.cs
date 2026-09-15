using System;
using System.Linq;
using GenHub.Core.Extensions.Enums;
using GenHub.Core.Models.Enums;
using Xunit;

namespace GenHub.Tests.Core.Extensions.Enums;

/// <summary>
/// Tests for <see cref="ContentInstallTargetExtensions"/>.
/// </summary>
public class ContentInstallTargetExtensionsTests
{
    /// <summary>
    /// The four user directories must be copied out of CAS rather than hard-linked, because the game
    /// engine writes into them in place and would otherwise rewrite the canonical CAS object.
    /// </summary>
    /// <param name="installTarget">The user-writable target under test.</param>
    [Theory]
    [InlineData(ContentInstallTarget.UserDataDirectory)]
    [InlineData(ContentInstallTarget.UserMapsDirectory)]
    [InlineData(ContentInstallTarget.UserReplaysDirectory)]
    [InlineData(ContentInstallTarget.UserScreenshotsDirectory)]
    public void IsUserWritableTarget_ForUserDirectories_ReturnsTrue(ContentInstallTarget installTarget)
    {
        Assert.True(installTarget.IsUserWritableTarget());
    }

    /// <summary>
    /// Workspace and system installs are managed by GenHub rather than written to by the user, so
    /// they remain eligible for hard links to CAS.
    /// </summary>
    /// <param name="installTarget">The GenHub-managed target under test.</param>
    [Theory]
    [InlineData(ContentInstallTarget.Workspace)]
    [InlineData(ContentInstallTarget.System)]
    public void IsUserWritableTarget_ForGenHubManagedTargets_ReturnsFalse(ContentInstallTarget installTarget)
    {
        Assert.False(installTarget.IsUserWritableTarget());
    }

    /// <summary>
    /// An install target this method has never been taught about must fail towards copying. The path
    /// resolver sends unmapped targets into the user data root, so answering "not user-writable"
    /// would hard-link a CAS object straight into the user's Documents folder.
    /// </summary>
    [Fact]
    public void IsUserWritableTarget_ForAnUnmappedTarget_FailsTowardsCopying()
    {
        var unmapped = Enum.GetValues<ContentInstallTarget>().Cast<int>().Max() + 1;

        Assert.True(((ContentInstallTarget)unmapped).IsUserWritableTarget());
    }
}
