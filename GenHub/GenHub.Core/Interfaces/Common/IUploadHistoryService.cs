using GenHub.Core.Models.Common;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Tools;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace GenHub.Core.Interfaces.Common;

/// <summary>
/// Interface for managing upload history.
/// </summary>
public interface IUploadHistoryService
{
    /// <summary>
    /// Occurs when upload history or storage quota usage is updated.
    /// </summary>
    event EventHandler? UploadHistoryChanged;

    /// <summary>
    /// Gets the default maximum upload bytes per period.
    /// </summary>
    long MaxUploadBytesPerPeriod { get; }

    /// <summary>
    /// Checks if an upload of the specified size is allowed, optionally within a category quota.
    /// </summary>
    /// <param name="fileSizeBytes">The file size in bytes.</param>
    /// <param name="category">Optional category to check quota against.</param>
    /// <returns>A task representing the asynchronous operation, with a boolean indicating if the upload is allowed.</returns>
    Task<bool> CanUploadAsync(long fileSizeBytes, string? category = null);

    /// <summary>
    /// Checks if an upload of the specified size is allowed, optionally within a category quota.
    /// </summary>
    /// <param name="fileSizeBytes">The file size in bytes.</param>
    /// <param name="category">Optional category to check quota against.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with a boolean indicating if the upload is allowed.</returns>
    Task<bool> CanUploadAsync(long fileSizeBytes, string? category, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the usage info, optionally filtered by category.
    /// </summary>
    /// <param name="category">Optional category to evaluate usage for.</param>
    /// <returns>A task representing the asynchronous operation, with the usage info.</returns>
    Task<UsageInfo> GetUsageInfoAsync(string? category = null);

    /// <summary>
    /// Gets the usage info, optionally filtered by category.
    /// </summary>
    /// <param name="category">Optional category to evaluate usage for.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with the usage info.</returns>
    Task<UsageInfo> GetUsageInfoAsync(string? category, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the usage info with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with the usage info.</returns>
    Task<UsageInfo> GetUsageInfoAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records an upload.
    /// </summary>
    /// <param name="fileSizeBytes">The file size in bytes.</param>
    /// <param name="url">The URL.</param>
    /// <param name="fileName">The file name.</param>
    /// <param name="fileKey">Optional file key in cloud storage.</param>
    /// <param name="deleteToken">Optional cryptographic deletion token.</param>
    /// <param name="fileHash">Optional SHA-256 hash of the uploaded file for deduplication.</param>
    /// <param name="category">Optional tool or content category (e.g. "replays", "maps").</param>
    /// <param name="game">Optional game selected when the upload began.</param>
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Upload history records optional tool metadata including game association while preserving existing parameter order.")]
    void RecordUpload(long fileSizeBytes, string url, string fileName, string? fileKey = null, string? deleteToken = null, string? fileHash = null, string? category = null, GameType? game = null);

    /// <summary>
    /// Finds an existing active upload record matching the specified file hash, category, and game.
    /// </summary>
    /// <param name="fileHash">The SHA-256 hex string of the file.</param>
    /// <param name="category">Optional category the existing record must belong to. Null matches any category.</param>
    /// <param name="game">Optional game the existing record must belong to. Null matches any game.</param>
    /// <returns>A task representing the asynchronous operation, returning the matching <see cref="UploadRecord"/> if found.</returns>
    Task<UploadRecord?> FindExistingUploadAsync(string fileHash, string? category = null, GameType? game = null);

    /// <summary>
    /// Finds an existing active upload record matching the specified file hash, category, and game.
    /// </summary>
    /// <param name="fileHash">The SHA-256 hex string of the file.</param>
    /// <param name="category">Optional category the existing record must belong to. Null matches any category.</param>
    /// <param name="game">Optional game the existing record must belong to. Null matches any game.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, returning the matching <see cref="UploadRecord"/> if found.</returns>
    Task<UploadRecord?> FindExistingUploadAsync(string fileHash, string? category, GameType? game, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the upload history, optionally filtered by category.
    /// </summary>
    /// <param name="category">Optional category filter (e.g. "replays", "maps"). If null, returns all history.</param>
    /// <returns>A task representing the asynchronous operation, with the history items.</returns>
    Task<IReadOnlyList<UploadHistoryItem>> GetUploadHistoryAsync(string? category = null);

    /// <summary>
    /// Gets the upload history, optionally filtered by category.
    /// </summary>
    /// <param name="category">Optional category filter (e.g. "replays", "maps"). If null, returns all history.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with the history items.</returns>
    Task<IReadOnlyList<UploadHistoryItem>> GetUploadHistoryAsync(string? category, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the upload history with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, with the history items.</returns>
    Task<IReadOnlyList<UploadHistoryItem>> GetUploadHistoryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes an item from upload history and deletes the hosted file from cloud storage if a delete token is present.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="deleteFromCloud">Whether to delete the file from cloud storage. Defaults to true.</param>
    /// <returns>A task representing the asynchronous operation, returning true if removal succeeded.</returns>
    Task<bool> RemoveHistoryItemAsync(string url, bool deleteFromCloud = true);

    /// <summary>
    /// Removes an item from upload history and deletes the hosted file from cloud storage if a delete token is present.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="deleteFromCloud">Whether to delete the file from cloud storage.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, returning true if removal succeeded.</returns>
    Task<bool> RemoveHistoryItemAsync(string url, bool deleteFromCloud, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an item from upload history and deletes the hosted file from cloud storage with cancellation support.
    /// </summary>
    /// <param name="url">The URL.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation, returning true if removal succeeded.</returns>
    Task<bool> RemoveHistoryItemAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Clears upload history and deletes all hosted files from cloud storage if delete tokens are present.
    /// </summary>
    /// <param name="deleteFromCloud">Whether to delete all files from cloud storage. Defaults to true.</param>
    /// <param name="category">Optional category filter (e.g. "replays", "maps"). If null, clears all history.</param>
    /// <returns>A task representing the asynchronous operation returning the count of deleted and failed cloud deletions.</returns>
    Task<(int Deleted, int Failed)> ClearHistoryAsync(bool deleteFromCloud = true, string? category = null);

    /// <summary>
    /// Clears upload history and deletes all hosted files from cloud storage if delete tokens are present.
    /// </summary>
    /// <param name="deleteFromCloud">Whether to delete all files from cloud storage.</param>
    /// <param name="category">Optional category filter (e.g. "replays", "maps"). If null, clears all history.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation returning the count of deleted and failed cloud deletions.</returns>
    Task<(int Deleted, int Failed)> ClearHistoryAsync(bool deleteFromCloud, string? category, CancellationToken cancellationToken);

    /// <summary>
    /// Clears upload history and deletes all hosted files from cloud storage with cancellation support.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task representing the asynchronous operation returning the count of deleted and failed cloud deletions.</returns>
    Task<(int Deleted, int Failed)> ClearHistoryAsync(CancellationToken cancellationToken);
}
