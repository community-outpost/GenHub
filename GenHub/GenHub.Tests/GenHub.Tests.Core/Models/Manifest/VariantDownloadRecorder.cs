using GenHub.Core.Interfaces.Common;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Results;
using Moq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace GenHub.Tests.Core.Models.Manifest;

/// <summary>
/// Records the URLs a deliverer asks to download, then fails every download so delivery
/// stops after file selection.
/// </summary>
internal static class VariantDownloadRecorder
{
    /// <summary>
    /// Gets the download URL of the host variant's file in delivery tests.
    /// </summary>
    public const string HostUrl = "https://example.invalid/releases/variant-host.zip";

    /// <summary>
    /// Gets the download URL of the foreign variant's file in delivery tests.
    /// </summary>
    public const string ForeignUrl = "https://example.invalid/releases/variant-foreign.zip";

    /// <summary>
    /// Sets up the download service to record each requested URL and fail.
    /// </summary>
    /// <param name="downloadService">The download service mock.</param>
    /// <returns>The URLs requested, in order.</returns>
    public static List<string> Record(Mock<IDownloadService> downloadService)
    {
        var requested = new List<string>();
        downloadService
            .Setup(d => d.DownloadFileAsync(
                It.IsAny<DownloadConfiguration>(),
                It.IsAny<IProgress<DownloadProgress>?>(),
                It.IsAny<CancellationToken>()))
            .Callback<DownloadConfiguration, IProgress<DownloadProgress>?, CancellationToken>((config, _, _) => requested.Add(config.Url.ToString()))
            .ReturnsAsync(DownloadResult.CreateFailure("stop after selection"));
        return requested;
    }
}
