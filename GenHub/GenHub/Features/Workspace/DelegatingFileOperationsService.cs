using GenHub.Core.Interfaces.Storage;
using GenHub.Core.Interfaces.Workspace;
using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Workspace;

/// <summary>
/// Base class for platform file operations services that delegate shared behavior to
/// <see cref="FileOperationsService"/> and only override platform-specific linking.
/// </summary>
/// <param name="baseService">The shared implementation everything else delegates to.</param>
/// <param name="casService">Content-addressable store, used to resolve hashes to paths.</param>
/// <param name="logger">Logger.</param>
public abstract class DelegatingFileOperationsService(
    FileOperationsService baseService,
    ICasService casService,
    ILogger logger) : IFileOperationsService
{
    /// <summary>Gets the shared file operations implementation.</summary>
    protected FileOperationsService BaseService => baseService;

    /// <summary>Gets the content-addressable store.</summary>
    protected ICasService CasService => casService;

    /// <summary>Gets the logger.</summary>
    protected ILogger Logger => logger;

    /// <inheritdoc/>
    public Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken = default)
        => BaseService.CopyFileAsync(sourcePath, destinationPath, cancellationToken);

    /// <inheritdoc/>
    public Task CreateSymlinkAsync(string linkPath, string targetPath, bool allowFallback = true, CancellationToken cancellationToken = default)
        => BaseService.CreateSymlinkAsync(linkPath, targetPath, allowFallback, cancellationToken);

    /// <inheritdoc/>
    public abstract Task CreateHardLinkAsync(string linkPath, string targetPath, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public Task<bool> VerifyFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
        => BaseService.VerifyFileHashAsync(filePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public Task<FileHashVerification> CheckFileHashAsync(string filePath, string expectedHash, CancellationToken cancellationToken = default)
        => BaseService.CheckFileHashAsync(filePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public Task DownloadFileAsync(Uri url, string destinationPath, IProgress<DownloadProgress>? progress = null, CancellationToken cancellationToken = default)
        => BaseService.DownloadFileAsync(url, destinationPath, progress, cancellationToken);

    /// <inheritdoc/>
    public Task ApplyPatchAsync(string targetPath, string patchPath, CancellationToken cancellationToken = default)
        => BaseService.ApplyPatchAsync(targetPath, patchPath, cancellationToken);

    /// <inheritdoc/>
    public Task<string?> StoreInCasAsync(string sourcePath, string? expectedHash = null, CancellationToken cancellationToken = default)
        => BaseService.StoreInCasAsync(sourcePath, expectedHash, cancellationToken);

    /// <inheritdoc/>
    public abstract Task<bool> CopyFromCasAsync(string hash, string destinationPath, ContentType? contentType = null, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public abstract Task<bool> LinkFromCasAsync(
        string hash,
        string destinationPath,
        bool useHardLink = false,
        ContentType? contentType = null,
        CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public Task<Stream?> OpenCasContentAsync(string hash, CancellationToken cancellationToken = default)
        => BaseService.OpenCasContentAsync(hash, cancellationToken);
}
