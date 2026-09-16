using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Models.Manifest;
using System;
using System.IO;

namespace GenHub.Features.GameProfiles.ViewModels;

/// <summary>
/// ViewModel representing an individual file entry inside a manifest for security inspection.
/// </summary>
/// <param name="file">The manifest file.</param>
public sealed class ManifestFileItemViewModel(ManifestFile file)
{
    /// <summary>
    /// Gets the relative path of the file within the package.
    /// </summary>
    public string RelativePath => file.RelativePath;

    /// <summary>
    /// Gets the file name.
    /// </summary>
    public string FileName => Path.GetFileName(file.RelativePath);

    /// <summary>
    /// Gets the size in bytes.
    /// </summary>
    public long Size => file.Size;

    /// <summary>
    /// Gets the formatted file size string.
    /// </summary>
    public string FormattedSize => ByteFormatHelper.FormatBytes(file.Size);

    /// <summary>
    /// Gets the SHA-256 hash string.
    /// </summary>
    public string? Hash => file.Hash;

    /// <summary>
    /// Gets a value indicating whether this file is an executable binary or script.
    /// </summary>
    public bool IsExecutable
    {
        get
        {
            if (string.IsNullOrWhiteSpace(file.RelativePath))
            {
                return false;
            }

            var ext = Path.GetExtension(file.RelativePath);
            return ProfileSharingConstants.ExecutableFileExtensions.Contains(ext);
        }
    }
}
