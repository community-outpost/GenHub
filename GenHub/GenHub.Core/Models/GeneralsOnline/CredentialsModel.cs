using System.Text.Json.Serialization;

namespace GenHub.Core.Models.GeneralsOnline;

/// <summary>
/// Model representing the structure of the credentials.json file for GeneralsOnline authentication.
/// This file is encrypted with DPAPI and located at: %MyDocuments%\Command and Conquer Generals Zero Hour Data\GeneralsOnlineData\credentials.json.
/// </summary>
public class CredentialsModel
{
    /// <summary>
    /// Gets or sets the refresh token used to obtain session tokens.
    /// </summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshToken { get; set; } = string.Empty;

    /// <summary>
    /// Validates that the refresh token is not empty.
    /// </summary>
    /// <returns>True if the refresh token has a value, otherwise false.</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(RefreshToken);
    }
}
