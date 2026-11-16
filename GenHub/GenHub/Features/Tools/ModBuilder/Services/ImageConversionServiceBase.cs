using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ModBuilder.Services;

/// <summary>
/// Base class for image conversion services with shared validation, logging, and DXT format selection.
/// </summary>
/// <param name="logger">Logger.</param>
public abstract class ImageConversionServiceBase(ILogger logger) : IImageConversionService
{
    /// <summary>Gets the logger.</summary>
    protected ILogger Logger => logger;

    /// <inheritdoc />
    public async Task<bool> ConvertImageAsync(
        string sourcePath,
        string targetPath,
        IDictionary<string, object>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                Logger.LogError("Source file does not exist: {SourcePath}", sourcePath);
                return false;
            }

            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            var targetExt = Path.GetExtension(targetPath).ToLowerInvariant();
            var sourceExt = Path.GetExtension(sourcePath).ToLowerInvariant();

            return await ConvertCoreAsync(sourcePath, targetPath, sourceExt, targetExt, parameters, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to convert image from {SourcePath} to {TargetPath}", sourcePath, targetPath);
            return false;
        }
    }

    /// <inheritdoc />
    public abstract Task<bool> HasAlphaChannelAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <inheritdoc />
    public async Task<string> GetRecommendedDxtFormatAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        var hasAlpha = await HasAlphaChannelAsync(imagePath, cancellationToken).ConfigureAwait(false);
        return hasAlpha ? ModBuilderConstants.Dxt5Format : ModBuilderConstants.Dxt1Format;
    }

    /// <summary>
    /// Converts an image after the source and target have been validated.
    /// </summary>
    /// <param name="sourcePath">The source image path.</param>
    /// <param name="targetPath">The target image path.</param>
    /// <param name="sourceExt">The lowercase source file extension.</param>
    /// <param name="targetExt">The lowercase target file extension.</param>
    /// <param name="parameters">Optional conversion parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the conversion succeeded.</returns>
    protected abstract Task<bool> ConvertCoreAsync(
        string sourcePath,
        string targetPath,
        string sourceExt,
        string targetExt,
        IDictionary<string, object>? parameters,
        CancellationToken cancellationToken);
}
