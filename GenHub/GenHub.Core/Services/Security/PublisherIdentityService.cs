using GenHub.Core.Constants;
using GenHub.Core.Helpers;
using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text;

namespace GenHub.Core.Services.Security;

/// <summary>
/// Detects publisher ID collisions and verifies key-proven publisher moves.
/// </summary>
/// <remarks>
/// Trust is established on first use: the key of the first subscription whose identity proof verifies
/// is stored in <see cref="IPublisherKeyStore"/>. A later subscription from a different source is a
/// verified move only when its proof verifies with that stored key, is bound to the URL it was
/// fetched from, and was signed after the last accepted proof.
/// </remarks>
/// <param name="keyStore">The trusted publisher key store.</param>
/// <param name="verifiers">The public key verifiers, one per algorithm.</param>
/// <param name="logger">The logger.</param>
public sealed class PublisherIdentityService(
    IPublisherKeyStore keyStore,
    IEnumerable<IPublicKeyVerifier> verifiers,
    ILogger<PublisherIdentityService> logger) : IPublisherIdentityService
{
    private readonly IReadOnlyList<IPublicKeyVerifier> _verifiers = verifiers.ToList();

    /// <summary>
    /// Builds the bytes a publisher identity proof signs.
    /// </summary>
    /// <param name="publisherId">The publisher ID as written in the definition.</param>
    /// <param name="definitionUrl">The definition URL as written in the definition.</param>
    /// <param name="signedAt">The signing time as written in the definition.</param>
    /// <returns>The payload bytes, or null when a field is blank or contains a line break.</returns>
    public static byte[]? BuildProofPayload(string? publisherId, string? definitionUrl, string? signedAt)
    {
        string?[] fields = [publisherId, definitionUrl, signedAt];
        if (fields.Any(f => string.IsNullOrWhiteSpace(f) || f.Contains('\n') || f.Contains('\r')))
        {
            return null;
        }

        return Encoding.UTF8.GetBytes(string.Join('\n', PublisherKeyConstants.IdentityProofDomain, publisherId, definitionUrl, signedAt));
    }

    /// <inheritdoc />
    public async Task<PublisherSourceAssessment> AssessAsync(
        PublisherSubscription? existing,
        PublisherSourceCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var proof = ReadProof(candidate);
        var proven = proof is null ? null : VerifyWithOwnKey(proof);

        if (existing is null)
        {
            return new PublisherSourceAssessment(PublisherSourceChange.None, proven?.Key, proven?.SignedAt);
        }

        var trusted = await keyStore.GetKeyAsync(candidate.PublisherId, cancellationToken);
        if (!PublisherSourceComparer.IsDifferentSource(existing, candidate))
        {
            return AssessSameSource(candidate.PublisherId, proven, trusted);
        }

        if (trusted.Failed)
        {
            logger.LogWarning(
                "Trusted key store could not be read while checking a new source for publisher {PublisherId}: {Reason}",
                candidate.PublisherId,
                trusted.FirstError);
            return new PublisherSourceAssessment(PublisherSourceChange.IdentityMismatch, proven?.Key, proven?.SignedAt);
        }

        if (trusted.Data is null)
        {
            logger.LogWarning("Publisher ID collision for {PublisherId}: no trusted key to verify the new source", candidate.PublisherId);
            return new PublisherSourceAssessment(PublisherSourceChange.Collision, proven?.Key, proven?.SignedAt);
        }

        if (IsVerifiedMove(proof, proven, trusted.Data.PublicKey, existing))
        {
            logger.LogInformation("Publisher {PublisherId} moved to a new source with a valid identity proof", candidate.PublisherId);
            return new PublisherSourceAssessment(PublisherSourceChange.VerifiedMove, proven!.Key, proven.SignedAt);
        }

        logger.LogWarning("New source for publisher {PublisherId} does not prove the trusted key", candidate.PublisherId);
        return new PublisherSourceAssessment(PublisherSourceChange.IdentityMismatch, proven?.Key, proven?.SignedAt);
    }

    /// <inheritdoc />
    public async Task<OperationResult> RecordTrustAsync(
        string publisherId,
        PublisherSourceAssessment assessment,
        bool replaceTrust,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publisherId);
        ArgumentNullException.ThrowIfNull(assessment);

        if (assessment.RequiresDecision && !replaceTrust)
        {
            return OperationResult.CreateFailure("A source that was not verified cannot keep the trusted key.");
        }

        if (replaceTrust)
        {
            if (assessment.ProvenKey is null)
            {
                var removed = await keyStore.RemoveKeyAsync(publisherId, cancellationToken);
                return removed.Success ? OperationResult.CreateSuccess() : OperationResult.CreateFailure(removed.Errors);
            }

            return await keyStore.SaveKeyAsync(new TrustedPublisherKey(publisherId, assessment.ProvenKey, DateTimeOffset.UtcNow), cancellationToken);
        }

        if (assessment.ProvenKey is null)
        {
            return OperationResult.CreateSuccess();
        }

        var trusted = await keyStore.GetKeyAsync(publisherId, cancellationToken);
        if (trusted.Failed)
        {
            return OperationResult.CreateFailure(trusted.Errors);
        }

        if (trusted.Data is not null)
        {
            if (!string.Equals(trusted.Data.PublicKey.Fingerprint, assessment.ProvenKey.Fingerprint, StringComparison.Ordinal))
            {
                logger.LogWarning("Publisher {PublisherId} now presents a different key; the trusted key is kept", publisherId);
            }

            return OperationResult.CreateSuccess();
        }

        logger.LogInformation("Trusting the identity key of publisher {PublisherId} on first use", publisherId);
        return await keyStore.SaveKeyAsync(new TrustedPublisherKey(publisherId, assessment.ProvenKey, DateTimeOffset.UtcNow), cancellationToken);
    }

    private static ParsedProof? ReadProof(PublisherSourceCandidate candidate)
    {
        var definition = candidate.Definition;
        var identity = definition?.Identity;
        if (definition is null || identity is null || string.IsNullOrWhiteSpace(identity.PublicKey))
        {
            return null;
        }

        if (!string.Equals(definition.Publisher?.Id, candidate.PublisherId, StringComparison.Ordinal) ||
            !PublisherSourceComparer.UrlsMatch(definition.DefinitionUrl, candidate.DefinitionUrl))
        {
            return null;
        }

        var payload = BuildProofPayload(definition.Publisher?.Id, definition.DefinitionUrl, identity.SignedAt);
        if (payload is null ||
            !DateTimeOffset.TryParse(identity.SignedAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var signedAt) ||
            !TryDecodeBase64(identity.Signature, out var signature))
        {
            return null;
        }

        return new ParsedProof(identity.PublicKey, payload, signature, signedAt.ToUniversalTime());
    }

    private static bool TryDecodeBase64(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value.Trim());
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private PublisherSourceAssessment AssessSameSource(
        string publisherId,
        ProvenIdentity? proven,
        OperationResult<TrustedPublisherKey?> trusted)
    {
        if (proven is null)
        {
            return new PublisherSourceAssessment(PublisherSourceChange.None, null, null);
        }

        if (trusted.Failed)
        {
            logger.LogWarning("Trusted key store could not be read for publisher {PublisherId}: {Reason}", publisherId, trusted.FirstError);
            return new PublisherSourceAssessment(PublisherSourceChange.None, null, null);
        }

        if (trusted.Data is not null &&
            !string.Equals(trusted.Data.PublicKey.Fingerprint, proven.Key.Fingerprint, StringComparison.Ordinal))
        {
            logger.LogWarning("Publisher {PublisherId} now presents a different key; the trusted key is kept", publisherId);
            return new PublisherSourceAssessment(PublisherSourceChange.None, null, null);
        }

        return new PublisherSourceAssessment(PublisherSourceChange.None, proven.Key, proven.SignedAt);
    }

    private bool IsVerifiedMove(ParsedProof? proof, ProvenIdentity? proven, PublisherPublicKey trustedKey, PublisherSubscription existing)
    {
        if (proof is null || proven is null ||
            !string.Equals(proven.Key.Fingerprint, trustedKey.Fingerprint, StringComparison.Ordinal))
        {
            return false;
        }

        if (existing.IdentityProofSignedAt is { } lastAccepted && proof.SignedAt <= lastAccepted)
        {
            logger.LogWarning("Identity proof for publisher {PublisherId} is not newer than the last accepted proof", existing.PublisherId);
            return false;
        }

        var verifier = _verifiers.FirstOrDefault(v => v.Algorithm == trustedKey.Algorithm);
        return verifier is not null && verifier.Verify(proof.Payload, proof.Signature, trustedKey).Success;
    }

    private ProvenIdentity? VerifyWithOwnKey(ParsedProof proof)
    {
        foreach (var verifier in _verifiers)
        {
            var imported = verifier.ImportPublicKey(proof.PublicKeyPem);
            if (imported.Failed || imported.Data is null)
            {
                continue;
            }

            var verified = verifier.Verify(proof.Payload, proof.Signature, imported.Data);
            if (verified.Success)
            {
                return new ProvenIdentity(imported.Data, proof.SignedAt);
            }

            logger.LogWarning("Publisher identity proof does not verify with its own {Algorithm} key", verifier.Algorithm);
            return null;
        }

        logger.LogWarning("Publisher identity key could not be imported by any verifier");
        return null;
    }

    private sealed record ParsedProof(string PublicKeyPem, byte[] Payload, byte[] Signature, DateTimeOffset SignedAt);

    private sealed record ProvenIdentity(PublisherPublicKey Key, DateTimeOffset SignedAt);
}
