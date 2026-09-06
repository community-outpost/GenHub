using System;

namespace GenHub.Core.Models.Common;

/// <summary>
/// Represents usage information for upload limits.
/// </summary>
/// <param name="UsedBytes">The number of bytes used in the current period.</param>
/// <param name="LimitBytes">The maximum allowed bytes per period.</param>
/// <param name="ResetDate">The date and time when the usage resets.</param>
public readonly record struct UsageInfo(long UsedBytes, long LimitBytes, DateTime ResetDate);
