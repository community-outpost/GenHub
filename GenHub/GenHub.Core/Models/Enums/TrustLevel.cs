// Copyright (c) GenHub. All rights reserved.
// Licensed under the MIT license.

namespace GenHub.Core.Models.Enums;

using System.Text.Json.Serialization;

/// <summary>
/// Defines the trust level for a subscribed publisher.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TrustLevel
{
    /// <summary>
    /// Publisher is not explicitly trusted. Prompts user before actions.
    /// </summary>
    Untrusted = 0,

    /// <summary>
    /// Publisher has been explicitly trusted by the user.
    /// </summary>
    Trusted = 1,

    /// <summary>
    /// Publisher is verified by GenHub maintainers (e.g., official community sources).
    /// </summary>
    Verified = 2,
}
