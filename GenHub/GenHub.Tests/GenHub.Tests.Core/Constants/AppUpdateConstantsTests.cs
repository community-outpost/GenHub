using System;
using GenHub.Core.Constants;
using Xunit;

namespace GenHub.Tests.Core.Constants;

/// <summary>
/// Unit tests for <see cref="AppUpdateConstants"/>.
/// </summary>
public class AppUpdateConstantsTests
{
    /// <summary>
    /// Tests that tab index constants have expected values.
    /// </summary>
    [Fact]
    public void TabIndex_Constants_ShouldHaveExpectedValues()
    {
        Assert.Equal(0, AppUpdateConstants.UpdateTabIndex);
        Assert.Equal(1, AppUpdateConstants.BrowseBuildsTabIndex);
        Assert.Equal(1, AppUpdateConstants.MaxTabIndex);
    }

    /// <summary>
    /// Tests that platform and artifact prefix constants have expected values.
    /// </summary>
    [Fact]
    public void ArtifactAndPlatform_Constants_ShouldHaveExpectedValues()
    {
        Assert.Equal("velopack", AppUpdateConstants.VelopackDirectory);
        Assert.Equal("genhub-velopack-windows-", AppUpdateConstants.ArtifactPrefixWindows);
        Assert.Equal("genhub-velopack-linux-", AppUpdateConstants.ArtifactPrefixLinux);
        Assert.Equal("GenHub-Release", AppUpdateConstants.ArtifactNameRelease);
        Assert.Equal("windows", AppUpdateConstants.PlatformWindows);
        Assert.Equal("linux", AppUpdateConstants.PlatformLinux);
    }

    /// <summary>
    /// Tests that periodic update check interval constants have expected values.
    /// </summary>
    [Fact]
    public void PeriodicUpdateCheckInterval_Constants_ShouldHaveExpectedValues()
    {
        Assert.Equal(30, AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes);
        Assert.Equal(5, AppUpdateConstants.MinPeriodicUpdateCheckIntervalMinutes);
        Assert.Equal(10080, AppUpdateConstants.MaxPeriodicUpdateCheckIntervalMinutes);
        Assert.Equal(5, AppUpdateConstants.PeriodicUpdateCheckIntervalIncrementMinutes);
        Assert.True(AppUpdateConstants.MinPeriodicUpdateCheckIntervalMinutes <= AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes);
        Assert.True(AppUpdateConstants.DefaultPeriodicUpdateCheckIntervalMinutes <= AppUpdateConstants.MaxPeriodicUpdateCheckIntervalMinutes);
    }

    /// <summary>
    /// Tests that timespan constants have expected durations.
    /// </summary>
    [Fact]
    public void TimeSpan_Constants_ShouldHaveExpectedValues()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), AppUpdateConstants.PostUpdateExitDelay);
        Assert.Equal(TimeSpan.FromHours(1), AppUpdateConstants.CacheDuration);
        Assert.Equal(3, AppUpdateConstants.MaxHttpRetries);
    }

    /// <summary>
    /// Tests that notification title and format constants are non-empty strings.
    /// </summary>
    [Fact]
    public void NotificationAndFormat_Constants_ShouldBeValid()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.UpdateAvailableNotificationTitle));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.BranchUpdateAvailableNotificationTitle));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.PrUpdateAvailableNotificationTitle));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.PrMergedUpdateAvailableNotificationTitle));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.BranchStaleUpdateAvailableNotificationTitle));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.UpdatingAppNotificationTitle));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.UpdateFailedNotificationTitle));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.UpdateAction));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.ViewUpdatesAction));
        Assert.Equal("development", AppUpdateConstants.DevelopmentBranch);
        Assert.Equal("main", AppUpdateConstants.MainBranch);
        Assert.Contains("{0}", AppUpdateConstants.ReleaseUpdateNotificationFormat);
        Assert.Contains("{0}", AppUpdateConstants.BranchUpdateNotificationFormat);
        Assert.Contains("{1}", AppUpdateConstants.BranchUpdateNotificationFormat);
        Assert.Contains("{0}", AppUpdateConstants.PrUpdateNotificationFormat);
        Assert.Contains("{1}", AppUpdateConstants.PrUpdateNotificationFormat);
        Assert.Contains("{0}", AppUpdateConstants.PrMergedUpdateNotificationFormat);
        Assert.Contains("{1}", AppUpdateConstants.PrMergedUpdateNotificationFormat);
        Assert.Contains("{0}", AppUpdateConstants.PrMergedReleaseNotificationFormat);
        Assert.Contains("{1}", AppUpdateConstants.PrMergedReleaseNotificationFormat);
        Assert.Contains("{0}", AppUpdateConstants.BranchStaleUpdateNotificationFormat);
        Assert.Contains("{1}", AppUpdateConstants.BranchStaleUpdateNotificationFormat);
        Assert.Contains("{0}", AppUpdateConstants.BranchStaleReleaseNotificationFormat);
        Assert.Contains("{1}", AppUpdateConstants.BranchStaleReleaseNotificationFormat);
        Assert.Contains("{0}", AppUpdateConstants.PrMergedStatusMessageFormat);
        Assert.Contains("{0}", AppUpdateConstants.BranchStaleStatusMessageFormat);
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.PatRequiredForArtifactsMessage));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.PrDedupePrefix));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.PrFallbackDedupePrefix));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.BranchDedupePrefix));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.BranchFallbackDedupePrefix));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.ReleaseDedupePrefix));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.GitHubFallbackDedupePrefix));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.NotificationAlreadyShownLogFormat));
        Assert.Contains("{0}", AppUpdateConstants.UpdateFailedNotificationFormat);
    }

    /// <summary>
    /// Tests that sort option constants are distinct non-empty strings.
    /// </summary>
    [Fact]
    public void SortOption_Constants_ShouldBeDistinctAndNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.SortOptionLastUpdated));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.SortOptionPrNumberDesc));
        Assert.False(string.IsNullOrWhiteSpace(AppUpdateConstants.SortOptionPrNumberAsc));
        Assert.NotEqual(AppUpdateConstants.SortOptionLastUpdated, AppUpdateConstants.SortOptionPrNumberDesc);
        Assert.NotEqual(AppUpdateConstants.SortOptionPrNumberDesc, AppUpdateConstants.SortOptionPrNumberAsc);
    }

    /// <summary>
    /// Tests that parallel download constants have valid positive values.
    /// </summary>
    [Fact]
    public void ParallelDownload_Constants_ShouldHaveExpectedValues()
    {
        Assert.Equal(131072, AppUpdateConstants.DefaultStreamBufferSize);
        Assert.Equal(2 * 1024 * 1024, AppUpdateConstants.DownloadChunkSizeBytes);
        Assert.Equal(8, AppUpdateConstants.ParallelDownloadConcurrency);
        Assert.Equal(4 * 1024 * 1024, AppUpdateConstants.ParallelDownloadThresholdBytes);
        Assert.True(AppUpdateConstants.ParallelDownloadConcurrency > 0);
        Assert.True(AppUpdateConstants.DownloadChunkSizeBytes > 0);
    }
}
