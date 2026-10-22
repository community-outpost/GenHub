using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Interfaces.Security;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Providers;
using GenHub.Core.Models.Results;
using GenHub.Core.Models.Security;
using GenHub.Features.Content.Services.Catalog;
using GenHub.Features.Content.ViewModels.Catalog;
using GenHub.Tests.Core.Collections;
using GenHub.Tests.Core.Services.Security;
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
    private readonly Mock<INotificationService> _notifications = new();
    private readonly CapturingLogger<SubscriptionConfirmationViewModel> _logger = new();
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
        Assert.Contains("could not be updated", vm.ErrorMessage, StringComparison.Ordinal);
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

    /// <summary>
    /// When the subscription update fails after Replace changed trust, the previous trusted key is put back.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_UpdateFails_RestoresPreviousTrustedKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        SetExisting(ExistingSubscription());
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("disk full"));
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.True(vm.ShowActionError);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// When the subscription update throws after Replace trusted a new key, and no key was trusted before, the new key is removed again.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_UpdateThrows_RemovesNewlyTrustedKeyAsync()
    {
        SetExisting(ExistingSubscription());
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("locked"));
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.True(vm.ShowActionError);
        Assert.Empty(_keyStore.Keys);
    }

    /// <summary>
    /// A trust write that lands and then throws is rolled back, so the old subscription keeps its key.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_TrustWriteLandsThenThrows_RestoresPreviousKeyAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        _keyStore.ThrowAfterNextSave = new IOException("interrupted");
        SetExisting(ExistingSubscription());
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.True(vm.ShowActionError);
        Assert.Null(_updated);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// When the update fails and the previous key cannot be restored, the dialog reports that distinctly.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_RestoreFails_ReportsRestoreFailureAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        _keyStore.RemainingWrites = 1;
        SetExisting(ExistingSubscription());
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("disk full"));
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.Contains("could not be restored", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, e => e.Contains("disk full", StringComparison.Ordinal));
    }

    /// <summary>
    /// When no key was trusted before and the replacement key cannot be removed again, the message says so.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_RemoveFailsWithoutPreviousKey_ReportsRemoveFailureAsync()
    {
        _keyStore.RemainingWrites = 1;
        SetExisting(ExistingSubscription());
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<bool>.CreateFailure("disk full"));
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.Contains("could not be removed", vm.ErrorMessage, StringComparison.Ordinal);
    }

    /// <summary>
    /// An older re-check that fails after a newer one finished does not clear the newer decision.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectDefinitionCatalog_OlderCheckFailsLate_KeepsNewerDecisionAsync()
    {
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var vm = CreateViewModel(TwoCatalogDefinition());
        await vm.InitializeAsync();

        var slowRead = new TaskCompletionSource<OperationResult<PublisherSubscription?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new Queue<Task<OperationResult<PublisherSubscription?>>>(
        [
            slowRead.Task,
            Task.FromResult(OperationResult<PublisherSubscription?>.CreateSuccess(CatalogDirectSubscription(OldCatalogUrl))),
        ]);
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync(PublisherId, It.IsAny<CancellationToken>()))
            .Returns(() => reads.Dequeue());

        var olderCheck = vm.SelectDefinitionCatalogAsync("maps");
        await vm.SelectDefinitionCatalogAsync("main");
        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);

        slowRead.SetException(new IOException("locked"));
        await olderCheck;

        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);
        Assert.Null(vm.ErrorMessage);
    }

    /// <summary>
    /// A check still running from before a reload cannot write its result into the reloaded dialog.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Initialize_Retried_InvalidatesOlderCheckAsync()
    {
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var vm = CreateViewModel(TwoCatalogDefinition());
        await vm.InitializeAsync();

        var slowRead = new TaskCompletionSource<OperationResult<PublisherSubscription?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync(PublisherId, It.IsAny<CancellationToken>()))
            .Returns(slowRead.Task);
        var olderCheck = vm.SelectDefinitionCatalogAsync("maps");

        _catalogParser
            .Setup(p => p.ParseCatalogAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<PublisherCatalog>.CreateFailure("offline"));
        await vm.InitializeAsync();

        slowRead.SetResult(OperationResult<PublisherSubscription?>.CreateSuccess(CatalogDirectSubscription(NewCatalogUrl)));
        await olderCheck;

        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
    }

    /// <summary>
    /// When the update throws and the previous key cannot be restored, the rollback failure is reported and the update error logged.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_UpdateThrowsAndRestoreFails_ReportsRestoreFailureAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        _keyStore.RemainingWrites = 1;
        SetExisting(ExistingSubscription());
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("locked"));
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.Contains("could not be restored", vm.ErrorMessage, StringComparison.Ordinal);
        Assert.Contains(_logger.Entries, e => e.Contains("locked", StringComparison.Ordinal));
    }

    /// <summary>
    /// A re-check whose store read throws, fails, or finds no subscription clears the stale decision, and every failure shows an error.
    /// </summary>
    /// <param name="outcome">The store outcome for the re-check.</param>
    /// <param name="expectError">Whether the dialog should show an error.</param>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Theory]
    [InlineData("io-exception", true)]
    [InlineData("json-exception", true)]
    [InlineData("failed-read", true)]
    [InlineData("stray-cancel", true)]
    [InlineData("removed", false)]
    public async Task SelectDefinitionCatalog_StoreOutcome_ResetsDecisionAsync(string outcome, bool expectError)
    {
        var vm = await CreateViewModelWithCollisionAsync();

        var read = _subscriptionStore.Setup(s => s.GetSubscriptionAsync(PublisherId, It.IsAny<CancellationToken>()));
        switch (outcome)
        {
            case "io-exception":
                read.ThrowsAsync(new IOException("locked"));
                break;
            case "json-exception":
                read.ThrowsAsync(new JsonException("corrupt"));
                break;
            case "stray-cancel":
                read.ThrowsAsync(new OperationCanceledException("timeout"));
                break;
            case "failed-read":
                read.ReturnsAsync(OperationResult<PublisherSubscription?>.CreateFailure("corrupt"));
                break;
            default:
                read.ReturnsAsync(OperationResult<PublisherSubscription?>.CreateSuccess(null));
                break;
        }

        await vm.SelectDefinitionCatalogAsync("main");

        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
        Assert.Equal(expectError, vm.ShowActionError);
    }

    /// <summary>
    /// A successful re-check clears the error left by an earlier failed re-check.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectDefinitionCatalog_SucceedsAfterFailure_ClearsSourceCheckErrorAsync()
    {
        var vm = await CreateViewModelWithCollisionAsync();
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync(PublisherId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("locked"));
        await vm.SelectDefinitionCatalogAsync("main");
        Assert.True(vm.ShowActionError);

        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        await vm.SelectDefinitionCatalogAsync("maps");

        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);
        Assert.Null(vm.ErrorMessage);
    }

    /// <summary>
    /// Cancelling a re-check while the identity check is pending clears the notice left by the previous catalog.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectDefinitionCatalog_CancelledDuringAssessment_ClearsPreviousNoticeAsync()
    {
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var pending = new TaskCompletionSource<PublisherSourceAssessment>(TaskCreationOptions.RunContinuationsAsynchronously);
        var identity = new Mock<IPublisherIdentityService>();
        identity
            .Setup(i => i.AssessAsync(It.IsAny<PublisherSubscription?>(), It.IsAny<PublisherSourceCandidate>(), It.IsAny<CancellationToken>()))
            .Returns<PublisherSubscription?, PublisherSourceCandidate, CancellationToken>((_, candidate, ct) =>
            {
                if (candidate.CatalogUrl == OldCatalogUrl)
                {
                    return Task.FromResult(new PublisherSourceAssessment(PublisherSourceChange.Collision, null, null));
                }

                return ct.CanBeCanceled
                    ? pending.Task.WaitAsync(ct)
                    : Task.FromResult(new PublisherSourceAssessment(PublisherSourceChange.None, null, null));
            });
        var vm = CreateViewModel(TwoCatalogDefinition(), identity.Object);
        await vm.InitializeAsync();
        await vm.SelectDefinitionCatalogAsync("maps");
        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);

        using var cts = new CancellationTokenSource();
        var check = vm.SelectDefinitionCatalogAsync("main", cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
    }

    /// <summary>
    /// The source check error is cleared after a language change, because clearing compares against the text that was shown.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectDefinitionCatalog_LanguageChangedAfterFailure_StillClearsSourceCheckErrorAsync()
    {
        var localization = new Mock<ILocalizationService>();
        localization
            .SetupSequence(l => l.GetString("Downloads.Subscription.ErrorMessage.SourceCheckFailed"))
            .Returns("Erreur de verification")
            .Returns("Check failed");
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var vm = CreateViewModel(TwoCatalogDefinition(), localizationService: localization.Object);
        await vm.InitializeAsync();
        await vm.SelectDefinitionCatalogAsync("maps");

        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync(PublisherId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("locked"));
        await vm.SelectDefinitionCatalogAsync("main");
        Assert.Equal("Erreur de verification", vm.ErrorMessage);

        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        await vm.SelectDefinitionCatalogAsync("maps");

        Assert.Null(vm.ErrorMessage);
    }

    /// <summary>
    /// A re-check that finishes after a newer one does not overwrite the newer result.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectDefinitionCatalog_OutOfOrderChecks_KeepsNewestResultAsync()
    {
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var vm = CreateViewModel(TwoCatalogDefinition());
        await vm.InitializeAsync();

        var slowRead = new TaskCompletionSource<OperationResult<PublisherSubscription?>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = new Queue<Task<OperationResult<PublisherSubscription?>>>(
        [
            slowRead.Task,
            Task.FromResult(OperationResult<PublisherSubscription?>.CreateSuccess(CatalogDirectSubscription(NewCatalogUrl))),
        ]);
        _subscriptionStore
            .Setup(s => s.GetSubscriptionAsync(PublisherId, It.IsAny<CancellationToken>()))
            .Returns(() => reads.Dequeue());

        var olderCheck = vm.SelectDefinitionCatalogAsync("maps");
        await vm.SelectDefinitionCatalogAsync("main");
        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);

        slowRead.SetResult(OperationResult<PublisherSubscription?>.CreateSuccess(CatalogDirectSubscription(OldCatalogUrl)));
        await olderCheck;

        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
    }

    /// <summary>
    /// Two dialogs cannot interleave their saves: the second waits until the first has finished.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_TwoDialogs_SavesDoNotInterleaveAsync()
    {
        SetExisting(ExistingSubscription());
        var release = new TaskCompletionSource<OperationResult<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var updateCalls = 0;
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .Returns(() => Interlocked.Increment(ref updateCalls) == 1 ? release.Task : Task.FromResult(OperationResult<bool>.CreateSuccess(true)));
        var first = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));
        var second = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));
        await first.InitializeAsync();
        await second.InitializeAsync();

        // The mocked stores complete synchronously, so the first save runs until it waits on its update inside the lock,
        // and the second runs until it waits on the lock. Without the lock the second would finish its update here.
        var firstSave = first.ReplaceExistingCommand.ExecuteAsync(null);
        var secondSave = Task.CompletedTask;
        try
        {
            Assert.Equal(1, Volatile.Read(ref updateCalls));
            Assert.False(firstSave.IsCompleted);

            secondSave = second.ReplaceExistingCommand.ExecuteAsync(null);

            Assert.Equal(1, Volatile.Read(ref updateCalls));
            Assert.False(secondSave.IsCompleted);
        }
        finally
        {
            // Release the shared save lock even when an assertion fails, so later tests cannot hang on it.
            // WhenAny waits without throwing, so a failed assertion above is not replaced by a save fault.
            release.TrySetResult(OperationResult<bool>.CreateSuccess(true));
            await Task.WhenAny(Task.WhenAll(firstSave, secondSave));
        }

        await Task.WhenAll(firstSave, secondSave);

        Assert.Equal(2, updateCalls);
    }

    /// <summary>
    /// A subscription that saves but whose key cannot be stored still closes, and warns the user.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_TrustWriteFails_SavesAndWarnsAsync()
    {
        SetExisting(null);
        _keyStore.WriteFailure = "locked";
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _genuineKey, NewSignedAt));
        bool? closed = null;
        vm.RequestClose = r => closed = r;

        await vm.InitializeAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.True(closed);
        Assert.NotNull(_added);
        _notifications.Verify(
            n => n.ShowWarning("Publisher Key Not Saved", It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<bool>()),
            Times.Once);
    }

    /// <summary>
    /// A Replace click when the save-time check finds the same source updates normally and keeps the user's settings and trust.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task ReplaceExisting_WhenNoDecisionNeeded_KeepsSettingsAndTrustAsync()
    {
        await _keyStore.SaveKeyAsync(TrustOf(_genuineKey));
        var added = new DateTime(2025, 3, 4, 5, 6, 7, DateTimeKind.Utc);
        var existing = ExistingSubscription();
        existing.DefinitionUrl = NewDefinitionUrl;
        existing.AutoUpdate = false;
        existing.NotifyNewReleases = false;
        existing.Added = added;
        SetExisting(existing);
        var vm = CreateViewModel(PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, NewCatalogUrl));

        await vm.InitializeAsync();
        await vm.ReplaceExistingCommand.ExecuteAsync(null);

        Assert.NotNull(_updated);
        Assert.Equal(TrustLevel.Trusted, _updated.TrustLevel);
        Assert.Equal("old-hash", _updated.CachedCatalogHash);
        Assert.False(_updated.AutoUpdate);
        Assert.False(_updated.NotifyNewReleases);
        Assert.Equal(added, _updated.Added);
        Assert.Equal(TrustOf(_genuineKey).PublicKey.Fingerprint, _keyStore.Keys[PublisherId].PublicKey.Fingerprint);
    }

    /// <summary>
    /// Confirm and Replace cannot run at the same time.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_WhileSaving_DisablesConfirmAndReplaceAsync()
    {
        var existing = ExistingSubscription();
        existing.DefinitionUrl = NewDefinitionUrl;
        SetExisting(existing);
        var release = new TaskCompletionSource<OperationResult<bool>>(TaskCreationOptions.RunContinuationsAsynchronously);
        _subscriptionStore
            .Setup(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()))
            .Returns(release.Task);
        var vm = CreateViewModel(PublisherIdentityTestData.CreateDefinition(PublisherId, NewDefinitionUrl, NewCatalogUrl));
        await vm.InitializeAsync();

        var confirm = vm.ConfirmCommand.ExecuteAsync(null);
        try
        {
            Assert.True(vm.IsSaving);
            Assert.False(vm.ConfirmCommand.CanExecute(null));
            Assert.False(vm.ReplaceExistingCommand.CanExecute(null));
            await vm.ReplaceExistingCommand.ExecuteAsync(null);
        }
        finally
        {
            release.TrySetResult(OperationResult<bool>.CreateSuccess(true));
            await Task.WhenAny(confirm);
        }

        await confirm;

        _subscriptionStore.Verify(s => s.UpdateSubscriptionAsync(It.IsAny<PublisherSubscription>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    /// <summary>
    /// A definition that lists the catalog of an existing catalog-direct subscription is the same source. It updates without a
    /// prompt but is not attached, and its key is not trusted, so it cannot redirect the subscription later.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task Confirm_DefinitionSharingCatalogDirectCatalog_UpdatesWithoutAttachingAsync()
    {
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var vm = CreateViewModel(SignedDefinition(NewDefinitionUrl, _attackerKey, NewSignedAt));

        await vm.InitializeAsync();
        await vm.ConfirmCommand.ExecuteAsync(null);

        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
        Assert.NotNull(_updated);
        Assert.Null(_updated.DefinitionUrl);
        Assert.Equal(NewCatalogUrl, _updated.CatalogUrl);
        Assert.Equal(TrustLevel.Trusted, _updated.TrustLevel);
        Assert.Empty(_keyStore.Keys);
    }

    /// <summary>
    /// Selecting another definition catalog re-runs the collision check for a catalog-direct subscription.
    /// </summary>
    /// <returns>A task representing the asynchronous unit test.</returns>
    [Fact]
    public async Task SelectDefinitionCatalog_ChangesCatalog_ReassessesCollisionAsync()
    {
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var vm = CreateViewModel(TwoCatalogDefinition());

        await vm.InitializeAsync();
        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);

        await vm.SelectDefinitionCatalogAsync("maps");
        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);

        await vm.SelectDefinitionCatalogAsync("main");
        Assert.Equal(PublisherSourceChange.None, vm.SourceChange);
    }

    private static PublisherSubscription CatalogDirectSubscription(string catalogUrl) =>
        new()
        {
            PublisherId = PublisherId,
            PublisherName = "Test Publisher",
            CatalogUrl = catalogUrl,
            TrustLevel = TrustLevel.Trusted,
        };

    private static PublisherDefinition TwoCatalogDefinition() =>
        new()
        {
            Publisher = new PublisherProfile { Id = PublisherId, Name = "Test Publisher" },
            Catalogs =
            [
                new CatalogEntry { Id = "main", Name = "Main", Url = NewCatalogUrl },
                new CatalogEntry { Id = "maps", Name = "Maps", Url = OldCatalogUrl },
            ],
            DefinitionUrl = NewDefinitionUrl,
        };

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

    private async Task<SubscriptionConfirmationViewModel> CreateViewModelWithCollisionAsync()
    {
        SetExisting(CatalogDirectSubscription(NewCatalogUrl));
        var vm = CreateViewModel(TwoCatalogDefinition());
        await vm.InitializeAsync();
        await vm.SelectDefinitionCatalogAsync("maps");
        Assert.Equal(PublisherSourceChange.Collision, vm.SourceChange);
        return vm;
    }

    private SubscriptionConfirmationViewModel CreateViewModel(
        PublisherDefinition definition,
        IPublisherIdentityService? identityService = null,
        ILocalizationService? localizationService = null)
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
            _logger,
            localizationService: localizationService,
            identityService: identityService ?? PublisherIdentityTestData.CreateService(_keyStore),
            notificationService: _notifications.Object);
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
