using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.ViewModels.Catalog;
using GenHub.Tests.Core.Collections;
using GenHub.Tests.Core.Services.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace GenHub.Tests.Core.Features.Content.ViewModels;

/// <summary>
/// Publisher ID collision and key-verified move tests for <see cref="SubscriptionConfirmationViewModel"/>.
/// </summary>
[Collection(PublishShareStaticStateCollection.Name)]
public sealed class SubscriptionConfirmationCollisionTests : IDisposable
{
    private const string PublisherId = "genuine-pub";
    private const string OldDefinitionUrl = "https://example.com/old/publisher.json";
    private const string NewDefinitionUrl = "https://example.com/new/publisher.json";
    private const string OldCatalogUrl = "https://example.com/old/catalog.json";
    private const string NewCatalogUrl = "https://example.com/new/catalog.json";
    private const string OldSignedAt = "2026-01-01T00:00:00Z";
    private const string NewSignedAt = "2026-06-01T00:00:00Z";

    private readonly bool _previousAllowUnresolvableDns;
    private readonly Mock<IPublisherSubscriptionStore> _subscriptionStore = new();
    private readonly Mock<IPublisherCatalogParser> _catalogParser = new();
    private readonly InMemoryPublisherKeyStore _keyStore = new();
    private readonly RSA _genuineKey = RSA.Create(2048);
    private readonly RSA _attackerKey = RSA.Create(2048);
    private PublisherSubscription? _added;
    private PublisherSubscription? _updated;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionConfirmationCollisionTests"/> class.
    /// </summary>
    public SubscriptionConfirmationCollisionTests()
    {
        _previousAllowUnresolvableDns = CatalogDocumentReader.AllowUnresolvableDnsForTesting;
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = true;

        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateSuccess(new PublisherCatalog
            {
                Publisher = new PublisherProfile { Id = PublisherId, Name = "Test Publisher" },
                Content = [],
            }));
        _subscriptionStore
            .Setup(s => s.AddSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherSubscription, CancellationToken>((sub, _) => _added = sub)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .Callback<PublisherSubscription, CancellationToken>((sub, _) => _updated = sub)
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(true));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CatalogDocumentReader.AllowUnresolvableDnsForTesting = _previousAllowUnresolvableDns;
        _genuineKey.Dispose();
        _attackerKey.Dispose();
    }

    /// <summary>
    /// A first subscription saves normally and trusts the definition's proven key on first use.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_FirstSubscriptionWithProof_AddsAndTrustsKeyAsync()
    {
        SetExisting(null);
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt));
        bool? closed = null;
        vm.RequestClose = r => closed = r;

        await vm.InitializeAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
        Assert.False(vm.ShowSourceChangeNotice);
        Assert.True(closed);
        Assert.NotNull(_added);
        Assert.Equal(NewDefinitionUrl, _added.DefinitionUrl);
        Assert.Equal(DateTimeOffset.Parse(NewSignedAt), _added.IdentityProofSignedAt);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// A first subscription without an identity block is unaffected and trusts no key.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_FirstSubscriptionWithoutProof_AddsWithoutKeyAsync()
    {
        SetExisting(null);
        var vm = CreateViewModel(PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, NewCatalogUrl));

        await vm.InitializeAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
        Assert.Equal("Subscribe to Library", vm.ConfirmButtonText);
        Assert.NotNull(_added);
        Assert.Null(_added.IdentityProofSignedAt);
        Assert.Empty(_keyStore.Keys);
    }

    /// <summary>
    /// A different URL for a subscribed publisher ID with no trusted key prompts for replace or skip and saves nothing on confirm.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Initialize_CollisionWithoutTrustedKey_PromptsAndConfirmDoesNotSaveAsync()
    {
        SetExisting(ExistingSubscription());
        var vm = CreateViewModel(PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, NewCatalogUrl));

        await vm.InitializeAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);
        Assert.True(vm.RequiresSourceDecision);
        Assert.True(vm.ShowSourceChangeNotice);
        Assert.Equal("Skip", vm.CancelButtonText);
        Assert.Contains(PublisherId, vm.SourceChangeMessage, StringComparison.Ordinal);
        Assert.Null(_updated);
        Assert.Null(_added);
    }

    /// <summary>
    /// Replace swaps the existing publisher for the new source, resets its trust level, and trusts the new key.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_AfterCollision_ReplacesSubscriptionAndTrustAsync()
    {
        SetExisting(ExistingSubscription());
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));
        bool? closed = null;
        vm.RequestClose = r => closed = r;

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.NotNull(_updated);
        Assert.Equal(NewDefinitionUrl, _updated.DefinitionUrl);
        Assert.Equal(NewCatalogUrl, _updated.CatalogUrl);
        Assert.Equal(TrustLevel.Untrusted, _updated.TrustLevel);
        Assert.Null(_updated.CachedCatalogHash);
        Assert.Equal(TrustOf(_attackerKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// When the trusted key cannot be updated, replace keeps the existing subscription and shows an error.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_KeyStoreWriteFails_KeepsExistingSubscriptionAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        _keyStore.WriteFailure = "locked";
        SetExisting(ExistingSubscription());
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));
        bool? closed = null;
        vm.RequestClose = r => closed = r;

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.Null(closed);
        Assert.Null(_updated);
        Assert.True(vm.ShowActionError);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// Skip closes the dialog without touching the subscription or the trusted key.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Skip_AfterIdentityMismatch_LeavesSubscriptionAndKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        SetExisting(ExistingSubscription());
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));
        bool? closed = null;
        vm.RequestClose = r => closed = r;

        await vm.InitializeAsync();
        vm.CancelCommand.Execute(null);

        Assert.False(closed);
        Assert.Null(_updated);
        Assert.Null(_added);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// A new URL proven with the trusted key moves the subscription without a collision prompt and keeps the user's settings.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_VerifiedMove_MovesSubscriptionWithoutPromptAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        SetExisting(ExistingSubscription(OldSignedAt));
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt));
        bool? closed = null;
        vm.RequestClose = r => closed = r;

        await vm.InitializeAsync();

        Assert.Equal(PublisherSourceChange.VerifiedMove, vm.SourceChange);
        Assert.False(vm.RequiresSourceDecision);
        Assert.True(vm.IsVerifiedMove);
        Assert.Equal("Move Subscription", vm.ConfirmButtonText);
        Assert.Equal("Cancel", vm.CancelButtonText);

        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.NotNull(_updated);
        Assert.Equal(NewDefinitionUrl, _updated.DefinitionUrl);
        Assert.Equal(TrustLevel.Trusted, _updated.TrustLevel);
        Assert.Equal(DateTimeOffset.Parse(NewSignedAt), _updated.IdentityProofSignedAt);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// A new URL signed with another key cannot take over through confirm: it prompts and saves nothing.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_HijackWithOtherKey_PromptsAndDoesNotSaveAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        SetExisting(ExistingSubscription());
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(PublisherSourceChange.IdentityMismatch, vm.SourceChange);
        Assert.True(vm.RequiresSourceDecision);
        Assert.Null(_updated);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// A new URL without any proof cannot take over a publisher whose key is trusted.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_HijackWithoutProof_PromptsAndDoesNotSaveAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        SetExisting(ExistingSubscription());
        var vm = CreateViewModel(PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, NewCatalogUrl));

        await vm.InitializeAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(PublisherSourceChange.IdentityMismatch, vm.SourceChange);
        Assert.Null(_updated);
    }

    /// <summary>
    /// The collision check runs again at confirm, so a subscription added after the dialog opened still prompts.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_SubscriptionAddedAfterInitialize_PromptsAndDoesNotSaveAsync()
    {
        SetExisting(null);
        var vm = CreateViewModel(PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, NewCatalogUrl));

        await vm.InitializeAsync();
        SetExisting(ExistingSubscription());
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);
        Assert.Null(_updated);
        Assert.Null(_added);
    }

    private static PublisherSubscription ExistingSubscription(string? proofSignedAt = null) =>
        new()
        {
            PublisherId = PublisherId,
            PublisherName = "Test Publisher",
            CatalogUrl = OldCatalogUrl,
            DefinitionUrl = OldDefinitionUrl,
            TrustLevel = TrustLevel.Trusted,
            CachedCatalogHash = "old-hash",
            IdentityProofSignedAt = proofSignedAt is null ? null : DateTimeOffset.Parse(proofSignedAt),
        };

    private static PublisherDefinition SignedDefinition(string definitionUrl, RSA key, string signedAt) =>
        PublisherIdentityTestData.SignWith(PublisherIdentityTestData.CreateDefinition(PublisherId, definitionUrl, NewCatalogUrl), key, signedAt);

    private static GenHub.Core.Models.Security.TrustedPublisherKey TrustOf(RSA key) => PublisherIdentityTestData.Trust(PublisherId, key);

    private void SetExisting(PublisherSubscription? existing)
    {
        _subscriptionStore
            .Setup(s => s.IsSubscribedAsync(PublisherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateSuccess(existing != null));
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync(PublisherId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(existing));
    }

    private SubscriptionConfirmationViewModel CreateViewModel(PublisherDefinition definition)
    {
        var responses = new Dictionary<string, string>
        {
            [NewDefinitionUrl] = JsonSerializer.Serialize(definition),
            [NewCatalogUrl] = "catalog-body",
        };

        return new SubscriptionConfirmationViewModel(
            NewDefinitionUrl,
            _subscriptionStore.Object,
            _catalogParser.Object,
            new HttpClient(new MappedHttpMessageHandler(responses)),
            NullLogger<SubscriptionConfirmationViewModel>.Instance,
            identityService: PublisherIdentityTestData.CreateService(_keyStore));
    }

    private sealed class MappedHttpMessageHandler(Dictionary<string, string> responses) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            responses.TryGetValue(request.RequestUri?.AbsoluteUri ?? string.Empty, out var body);
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body ?? "{}"),
            });
        }
    }
}
