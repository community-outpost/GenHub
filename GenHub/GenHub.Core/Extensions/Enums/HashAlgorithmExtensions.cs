using GenHub.Core.Models.Enums;

namespace GenHub.Core.Extensions.Enums;

/// <summary>
/// Provides extension methods for the <see cref="HashAlgorithm"/> enum.
/// </summary>
public static class HashAlgorithmExtensions
{
    /// <summary>
    /// Converts the specified <see cref="HashAlgorithm"/> to a friendly string representation.
    /// </summary>
    /// <param name="alg">The hash algorithm to convert.</param>
    /// <returns>A friendly string representation of the hash algorithm.</returns>
    public static string ToFriendlyString(this HashAlgorithm alg)
    {
        switch (alg)
        {
            case HashAlgorithm.Sha256:
                {
                    return "SHA-256";
                }

            default:
                {
                    return alg.ToString();
                }
        }
    }
}
