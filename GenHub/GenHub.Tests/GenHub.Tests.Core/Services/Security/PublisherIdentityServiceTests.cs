using GenHub.Core.Helpers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Security;
using GenHub.Core.Services.Security;
using System.Security.Cryptography;

namespace GenHub.Tests.Core.Services.Security;

/// <summary>
/// Tests for <see cref="PublisherIdentityService"/> and <see cref="PublisherSourceComparer"/>.
/// </summary>
public sealed class PublisherIdentityServiceTests : IDisposable
{
    private const string PublisherId = "genuine-pub";
    private const string OldDefinitionUrl = "https://old.example.com/publisher.json";
    private const string NewDefinitionUrl = "https://new.example.com/publisher.json";
    private const string AttackerDefinitionUrl = "https://attacker.example.com/publisher.json";
    private const string CatalogUrl = "https://new.example.com/catalog.json";
    private const string OldSignedAt = "2026-01-01T00:00:00Z";
    private const string NewSignedAt = "2026-06-01T00:00:00Z";

    private readonly InMemoryPublisherKeyStore _keyStore = new();
    private readonly RSA _genuineKey = RSA.Create(2048);
    private readonly RSA _attackerKey = RSA.Create(2048);

    /// <inheritdoc />
    public void Dispose()
    {
        _genuineKey.Dispose();
        _attackerKey.Dispose();
    }

    /// <summary>
    /// A first subscription is never a collision, and a valid proof yields the key to trust on first use.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_FirstSubscriptionWithValidProof_ReturnsNoneWithProvenKeyAsync()
    {
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(null, Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.None, assessment.Change);
        Assert.False(assessment.RequiresDecision);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, assessment.ProvenKey?.Fingerprint);
        Assert.Equal(DateTimeOffset.Parse(NewSignedAt), assessment.ProofSignedAt);
    }

    /// <summary>
    /// A first subscription without an identity block is unaffected.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_FirstSubscriptionWithoutProof_ReturnsNoneAsync()
    {
        var definition = PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, CatalogUrl);

        var assessment = await CreateService().AssessAsync(null, Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.None, assessment.Change);
        Assert.Null(assessment.ProvenKey);
        Assert.Null(assessment.ProofSignedAt);
    }

    /// <summary>
    /// Subscribing again from the same definition URL is not a collision.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_SameDefinitionUrl_ReturnsNoneAsync()
    {
        var definition = PublisherIdentityTestData.CreateDefinition(PublisherId, OldDefinitionUrl, CatalogUrl);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(OldDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.None, assessment.Change);
    }

    /// <summary>
    /// A different definition URL with no trusted key is a collision the user must resolve.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_DifferentUrlWithoutTrustedKey_ReturnsCollisionAsync()
    {
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.Collision, assessment.Change);
        Assert.True(assessment.RequiresDecision);
    }

    /// <summary>
    /// A new URL whose proof verifies with the trusted key is a verified move.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_DifferentUrlProvenWithTrustedKey_ReturnsVerifiedMoveAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl, OldSignedAt), Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.VerifiedMove, assessment.Change);
        Assert.False(assessment.RequiresDecision);
        Assert.Equal(DateTimeOffset.Parse(NewSignedAt), assessment.ProofSignedAt);
    }

    /// <summary>
    /// ECDSA keys prove a move the same way as RSA keys.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_EcdsaProvenMove_ReturnsVerifiedMoveAsync()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await _keyStore.SaveKeyAsync(PublisherIdentityTestData.Trust(PublisherId, ecdsa));
        var definition = PublisherIdentityTestData.SignWith(
            PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, CatalogUrl),
            ecdsa,
            NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.VerifiedMove, assessment.Change);
    }

    /// <summary>
    /// A new URL without a proof cannot take over a publisher whose key is trusted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_DifferentUrlWithoutProof_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = PublisherIdentityTestData.CreateDefinition(PublisherId, AttackerDefinitionUrl, CatalogUrl);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(AttackerDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
        Assert.True(assessment.RequiresDecision);
    }

    /// <summary>
    /// A new URL signed with another key is the hijack case and is not a verified move.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_DifferentUrlSignedWithOtherKey_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(AttackerDefinitionUrl, _attackerKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(AttackerDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
        Assert.Equal(TrustOf(_attackerKey).PublicKey.Fingerprint, assessment.ProvenKey?.Fingerprint);
    }

    /// <summary>
    /// A genuine definition copied verbatim to another URL is rejected, because the proof is bound to the URL it names.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_GenuineProofServedFromOtherUrl_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(AttackerDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
        Assert.Null(assessment.ProvenKey);
    }

    /// <summary>
    /// Rewriting the signed definition URL to the attacker's URL breaks the signature.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_SignedDefinitionUrlRewritten_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);
        definition.DefinitionUrl = AttackerDefinitionUrl;

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(AttackerDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
        Assert.Null(assessment.ProvenKey);
    }

    /// <summary>
    /// A proof for another publisher ID does not prove this one.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_ProofSignedForOtherPublisherId_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = PublisherIdentityTestData.SignWith(
            PublisherIdentityTestData.CreateDefinition("other-pub", NewDefinitionUrl, CatalogUrl),
            _genuineKey,
            NewSignedAt);
        definition.Publisher.Id = PublisherId;

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
    }

    /// <summary>
    /// A genuine proof older than the last accepted one is a replay from a former URL and is rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_ReplayedOlderProof_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(OldDefinitionUrl, _genuineKey, OldSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(NewDefinitionUrl, NewSignedAt), Candidate(OldDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
    }

    /// <summary>
    /// A proof signed at the same time as the last accepted one is not newer and is rejected.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_ProofNotNewerThanLastAccepted_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl, NewSignedAt), Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
    }

    /// <summary>
    /// A proof signed far in the future is rejected, so it cannot become a freshness floor that blocks later moves.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_FutureDatedProof_IsNotProvenAsync()
    {
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, "2100-01-01T00:00:00Z");

        var assessment = await CreateService().AssessAsync(null, Candidate(NewDefinitionUrl, definition));

        Assert.Null(assessment.ProvenKey);
        Assert.Null(assessment.ProofSignedAt);
    }

    /// <summary>
    /// A corrupted signature is not a proof.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_TamperedSignature_ReturnsIdentityMismatchAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);
        var signature = Convert.FromBase64String(definition.Identity!.Signature!);
        signature[0] ^= 0xFF;
        definition.Identity.Signature = Convert.ToBase64String(signature);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
        Assert.Null(assessment.ProvenKey);
    }

    /// <summary>
    /// An unreadable key store fails closed: a new source is treated as unproven.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_UnreadableKeyStore_ReturnsIdentityMismatchAsync()
    {
        _keyStore.ReadFailure = "corrupt";
        var definition = SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.IdentityMismatch, assessment.Change);
    }

    /// <summary>
    /// A same-source definition that now carries a different key does not offer that key for trust.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_SameSourceWithDifferentKey_DoesNotProveKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var definition = SignedDefinition(OldDefinitionUrl, _attackerKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(Existing(OldDefinitionUrl), Candidate(OldDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.None, assessment.Change);
        Assert.Null(assessment.ProvenKey);
        Assert.Null(assessment.ProofSignedAt);
    }

    /// <summary>
    /// A first subscription trusts its proven key and replaces a stale key left by a removed subscription.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RecordTrustAsync_ReplaceWithProvenKey_SavesKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_attackerKey));
        var assessment = new PublisherSourceAssessment(PublisherSourceChange.None, TrustOf(_genuineKey).PublicKey, null);

        var result = await CreateService().RecordTrustAsync(PublisherId, assessment, replaceTrust: true);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// Replacing a publisher with a source that has no proven key removes the previous trusted key.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RecordTrustAsync_ReplaceWithoutProvenKey_RemovesTrustedKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var assessment = new PublisherSourceAssessment(PublisherSourceChange.Collision, null, null);

        var result = await CreateService().RecordTrustAsync(PublisherId, assessment, replaceTrust: true);

        Assert.True(result.Success, result.FirstError);
        Assert.Empty(_keyStore.Keys);
    }

    /// <summary>
    /// Without a replace, an existing trusted key is kept even when another key was proven.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RecordTrustAsync_NoReplace_KeepsTrustedKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var assessment = new PublisherSourceAssessment(PublisherSourceChange.None, TrustOf(_attackerKey).PublicKey, null);

        var result = await CreateService().RecordTrustAsync(PublisherId, assessment, replaceTrust: false);

        Assert.True(result.Success, result.FirstError);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// Without a replace and with no trusted key, the proven key is trusted on first use.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RecordTrustAsync_NoReplaceAndNoTrustedKey_TrustsProvenKeyAsync()
    {
        var assessment = new PublisherSourceAssessment(PublisherSourceChange.None, TrustOf(_genuineKey).PublicKey, null);

        var result = await CreateService().RecordTrustAsync(PublisherId, assessment, replaceTrust: false);

        Assert.True(result.Success, result.FirstError);
        Assert.True(_keyStore.Keys.ContainsKey(PublisherId));
    }

    /// <summary>
    /// An unverified source can only change trust through an explicit replace.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RecordTrustAsync_UnverifiedWithoutReplace_FailsAndKeepsTrustedKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var assessment = new PublisherSourceAssessment(PublisherSourceChange.IdentityMismatch, TrustOf(_attackerKey).PublicKey, null);

        var result = await CreateService().RecordTrustAsync(PublisherId, assessment, replaceTrust: false);

        Assert.True(result.Failed);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// The proof payload rejects line breaks so fields cannot be shifted between lines.
    /// </summary>
    [Fact]
    public void BuildProofPayload_FieldWithLineBreak_ReturnsNull()
    {
        Assert.Null(PublisherIdentityService.BuildProofPayload("pub\nhttps://x", "https://y", NewSignedAt));
        Assert.Null(PublisherIdentityService.BuildProofPayload(PublisherId, " ", NewSignedAt));
        Assert.NotNull(PublisherIdentityService.BuildProofPayload(PublisherId, NewDefinitionUrl, NewSignedAt));
    }

    /// <summary>
    /// Source comparison ignores scheme and host case but not path case, and compares catalog URLs for catalog-direct candidates.
    /// </summary>
    [Fact]
    public void IsDifferentSource_ComparesTheRightUrls()
    {
        var existing = new PublisherSubscription
        {
            PublisherId = PublisherId,
            CatalogUrl = "https://example.com/catalog.json",
            DefinitionUrl = "https://example.com/Publisher.json",
        };

        Assert.False(PublisherSourceComparer.IsDifferentSource(existing, new PublisherSourceCandidate(PublisherId, "HTTPS://EXAMPLE.com/Publisher.json", "https://other/catalog.json", null)));
        Assert.True(PublisherSourceComparer.IsDifferentSource(existing, new PublisherSourceCandidate(PublisherId, "https://example.com/publisher.json", "https://example.com/catalog.json", null)));
        Assert.False(PublisherSourceComparer.IsDifferentSource(existing, new PublisherSourceCandidate(PublisherId, null, "https://example.com/catalog.json", null)));
        Assert.True(PublisherSourceComparer.IsDifferentSource(existing, new PublisherSourceCandidate(PublisherId, null, "https://example.com/other.json", null)));
        var catalogDirect = new PublisherSubscription { PublisherId = PublisherId, CatalogUrl = "https://example.com/catalog.json" };
        Assert.False(PublisherSourceComparer.IsDifferentSource(catalogDirect, new PublisherSourceCandidate(PublisherId, "https://example.com/publisher.json", "https://example.com/catalog.json", null)));
        Assert.True(PublisherSourceComparer.IsDifferentSource(catalogDirect, new PublisherSourceCandidate(PublisherId, "https://example.com/publisher.json", "https://example.com/other.json", null)));
    }

    /// <summary>
    /// A definition sharing a catalog-direct subscription's catalog is the same source, but its key is not offered for trust.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task AssessAsync_DefinitionSharingCatalogDirectCatalog_ReturnsNoneWithoutKeyAsync()
    {
        var existing = new PublisherSubscription { PublisherId = PublisherId, CatalogUrl = CatalogUrl };
        var definition = SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt);

        var assessment = await CreateService().AssessAsync(existing, Candidate(NewDefinitionUrl, definition));

        Assert.Equal(PublisherSourceChange.None, assessment.Change);
        Assert.Null(assessment.ProvenKey);
        Assert.Null(assessment.ProofSignedAt);
    }

    /// <summary>
    /// Restoring trust puts back a captured key, or removes the key when none was captured.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task RestoreTrustAsync_RestoresCapturedKeyOrRemovesAsync()
    {
        var service = CreateService();
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var captured = await service.GetTrustedKeyAsync(PublisherId);
        await _keyStore.SaveKeyAsync(TrustOf(_attackerKey));

        Assert.True((await service.RestoreTrustAsync(PublisherId, captured.Data)).Success);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);

        Assert.True((await service.RestoreTrustAsync(PublisherId, null)).Success);
        Assert.Empty(_keyStore.Keys);
    }

    private static PublisherSubscription Existing(string definitionUrl, string? proofSignedAt = null) =>
        new()
        {
            PublisherId = PublisherId,
            PublisherName = "Test Publisher",
            CatalogUrl = "https://old.example.com/catalog.json",
            DefinitionUrl = definitionUrl,
            IdentityProofSignedAt = proofSignedAt is null ? null : DateTimeOffset.Parse(proofSignedAt),
        };

    private static PublisherSourceCandidate Candidate(string fetchedUrl, PublisherDefinition definition) =>
        new(PublisherId, fetchedUrl, CatalogUrl, definition);

    private static PublisherDefinition SignedDefinition(string definitionUrl, RSA key, string signedAt) =>
        PublisherIdentityTestData.SignWith(PublisherIdentityTestData.CreateDefinition(PublisherId, definitionUrl, CatalogUrl), key, signedAt);

    private static TrustedPublisherKey TrustOf(RSA key) => PublisherIdentityTestData.Trust(PublisherId, key);

    private PublisherIdentityService CreateService() => PublisherIdentityTestData.CreateService(_keyStore);
}
