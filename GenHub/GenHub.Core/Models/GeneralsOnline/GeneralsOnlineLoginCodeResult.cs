using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Login code payload of the backend GET LoginCode endpoint.
/// </summary>
public sealed class GeneralsOnlineLoginCodeResult
{
    /// <summary>
    /// Gets or sets a value indicating whether the code was issued.
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets the issued login code.
    /// </summary>
    [JsonPropertyName("login_code")]
    public string LoginCode { get; set; } = string.Empty;
}
