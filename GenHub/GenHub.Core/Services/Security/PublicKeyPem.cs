using GenHub.Core.Constants;
using GenHub.Core.Models.Results;
using System.Security.Cryptography;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Extracts the single public key block from PEM text, rejecting private keys outright.
/// </summary>
internal static class PublicKeyPem
{
    /// <summary>
    /// Reads the only PEM block in the text.
    /// </summary>
    /// <param name="pem">The PEM text.</param>
    /// <returns>The block label and decoded DER bytes, or a failure.</returns>
    public static OperationResult<(string Label, byte[] Der)> ReadSingleBlock(string pem)
    {
        List<(string Label, byte[] Der)> blocks = [];
        var remaining = pem.AsSpan();
        while (PemEncoding.TryFind(remaining, out var fields))
        {
            var label = remaining[fields.Label].ToString();
            if (label.Contains(PublisherKeyConstants.PrivateKeyPemLabelMarker, StringComparison.Ordinal))
            {
                return OperationResult<(string, byte[])>.CreateFailure(
                    "Private keys are not accepted. Supply the publisher's public key only.");
            }

            var der = new byte[fields.DecodedDataLength];
            if (!Convert.TryFromBase64Chars(remaining[fields.Base64Data], der, out var written))
            {
                return OperationResult<(string, byte[])>.CreateFailure("The PEM block is not valid base64.");
            }

            blocks.Add((label, der[..written]));
            remaining = remaining[fields.Location.End..];
        }

        return blocks.Count switch
        {
            0 => OperationResult<(string, byte[])>.CreateFailure("No PEM-encoded key was found."),
            1 => OperationResult<(string, byte[])>.CreateSuccess(blocks[0]),
            _ => OperationResult<(string, byte[])>.CreateFailure("The PEM text holds more than one block. Supply exactly one public key."),
        };
    }
}
