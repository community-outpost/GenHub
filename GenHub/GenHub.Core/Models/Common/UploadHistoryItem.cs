using System;

namespace GenHub.Core.Models.Common;

/// <summary>
/// Represents a single item in the upload history.
/// </summary>
/// <param name="Timestamp">The UTC timestamp of the upload.</param>
/// <param name="SizeBytes">The size of the uploaded file in bytes.</param>
/// <param name="Url">The public URL of the upload.</param>
/// <param name="FileName">The name of the uploaded file.</param>
public record UploadHistoryItem(
    DateTime Timestamp,
    long SizeBytes,
    string Url,
    string FileName);
