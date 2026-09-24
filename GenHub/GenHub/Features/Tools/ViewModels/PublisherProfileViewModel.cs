using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GenHub.Common.Validation;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Providers;
using GenHub.Core.Messages;
using GenHub.Core.Models.Publishers;
using GenHub.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels;

/// <summary>
/// ViewModel for the Publisher Profile tab.
/// </summary>
public partial class PublisherProfileViewModel(
    PublisherStudioProject project,
    PublisherStudioViewModel parentViewModel,
    ILogger logger,
    INotificationService? notificationService = null,
    ILocalizationService? localizationService = null,
    IPublisherSubscriptionStore? subscriptionStore = null) : ObservableValidator
{
    private bool _isSyncing;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [LocalizedRequired("Tools.PublisherStudio.Validation.PublisherIdRequired", "Publisher ID is required")]
    [RegularExpression(RegexConstants.PublisherIdPattern, ErrorMessage = "Publisher ID must use lowercase letters, numbers, and hyphens only (no spaces or special characters)")]
    private string _publisherId = project?.Catalog?.Publisher?.Id ?? string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Publisher Name is required")]
    [MinLength(2, ErrorMessage = "Publisher Name must be at least 2 characters")]
    private string _publisherName = project?.Catalog?.Publisher?.Name ?? string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(PublisherProfileViewModel), nameof(ValidateUrl))]
    private string _avatarUrl = project?.Catalog?.Publisher?.AvatarUrl ?? string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(PublisherProfileViewModel), nameof(ValidateUrl))]
    private string _websiteUrl = project?.Catalog?.Publisher?.WebsiteUrl ?? string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(PublisherProfileViewModel), nameof(ValidateUrl))]
    private string _supportUrl = project?.Catalog?.Publisher?.SupportUrl ?? string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [CustomValidation(typeof(PublisherProfileViewModel), nameof(ValidateEmail))]
    private string _contactEmail = project?.Catalog?.Publisher?.ContactEmail ?? string.Empty;

    [ObservableProperty]
    private string _description = project?.Catalog?.Publisher?.Description ?? string.Empty;

    [ObservableProperty]
    private string _tagsString = project?.Tags != null ? string.Join(", ", project.Tags) : string.Empty;

    /// <summary>
    /// Validates that a string is either empty or a valid HTTP/HTTPS URL.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="context">The validation context.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public static ValidationResult? ValidateUrl(string? value, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Success;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(ValidationResourceResolver.FormatMessage("Tools.PublisherStudio.Validation.ValidHttpUrlFormat", "{0} must be a valid http or https URL.", context.DisplayName));
    }

    /// <summary>
    /// Validates that an email address is either empty or valid.
    /// </summary>
    /// <param name="value">The string value to validate.</param>
    /// <param name="context">The validation context.</param>
    /// <returns>A validation result indicating success or failure.</returns>
    public static ValidationResult? ValidateEmail(string? value, ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ValidationResult.Success;
        }

        var attr = new EmailAddressAttribute();
        if (attr.IsValid(value) && value.Contains('@') && !value.StartsWith('@') && !value.EndsWith('@'))
        {
            return ValidationResult.Success;
        }

        return new ValidationResult(ValidationResourceResolver.FormatMessage("Tools.PublisherStudio.Validation.ValidContactEmail", "Contact email must be a valid email address."));
    }

    /// <summary>
    /// Reloads the view model fields from the current project.
    /// </summary>
    public void LoadFromProject()
    {
        _isSyncing = true;
        try
        {
            var pub = project?.Catalog?.Publisher;
            PublisherId = pub?.Id ?? string.Empty;
            PublisherName = pub?.Name ?? string.Empty;
            AvatarUrl = pub?.AvatarUrl ?? string.Empty;
            WebsiteUrl = pub?.WebsiteUrl ?? string.Empty;
            SupportUrl = pub?.SupportUrl ?? string.Empty;
            ContactEmail = pub?.ContactEmail ?? string.Empty;
            Description = pub?.Description ?? string.Empty;
            TagsString = project?.Tags != null ? string.Join(", ", project.Tags) : string.Empty;
            ClearErrors();
        }
        finally
        {
            _isSyncing = false;
        }
    }

    /// <summary>
    /// Applies the current profile values to the underlying project and catalog.
    /// </summary>
    public void ApplyToProject()
    {
        if (project?.Catalog == null)
        {
            return;
        }

        project.Catalog.Publisher ??= new();
        if (!string.IsNullOrWhiteSpace(PublisherId))
        {
            var trimmedId = PublisherId.ToLowerInvariant().Trim();
            if (System.Text.RegularExpressions.Regex.IsMatch(trimmedId, RegexConstants.PublisherIdPattern))
            {
                project.Catalog.Publisher.Id = trimmedId;
            }
        }

        if (!string.IsNullOrWhiteSpace(PublisherName))
        {
            project.Catalog.Publisher.Name = PublisherName.Trim();
        }

        project.Catalog.Publisher.AvatarUrl = string.IsNullOrWhiteSpace(AvatarUrl) ? null : AvatarUrl.Trim();
        project.Catalog.Publisher.WebsiteUrl = string.IsNullOrWhiteSpace(WebsiteUrl) ? null : WebsiteUrl.Trim();
        project.Catalog.Publisher.SupportUrl = string.IsNullOrWhiteSpace(SupportUrl) ? null : SupportUrl.Trim();
        project.Catalog.Publisher.ContactEmail = string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail.Trim();
        project.Catalog.Publisher.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();

        project.Tags = TagsString
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void OnProfileFieldChanged()
    {
        if (_isSyncing)
        {
            return;
        }

        ApplyToProject();
        MarkDirty();
    }

    partial void OnPublisherIdChanged(string value) => OnProfileFieldChanged();

    partial void OnPublisherNameChanged(string value) => OnProfileFieldChanged();

    partial void OnAvatarUrlChanged(string value) => OnProfileFieldChanged();

    partial void OnWebsiteUrlChanged(string value) => OnProfileFieldChanged();

    partial void OnSupportUrlChanged(string value) => OnProfileFieldChanged();

    partial void OnContactEmailChanged(string value) => OnProfileFieldChanged();

    partial void OnDescriptionChanged(string value) => OnProfileFieldChanged();

    partial void OnTagsStringChanged(string value) => OnProfileFieldChanged();

    private void MarkDirty()
    {
        parentViewModel?.MarkDirty();
        parentViewModel?.PublishShareViewModel?.MarkAllCatalogsChanged();
    }

    /// <summary>
    /// Saves the publisher profile to the project and writes to disk immediately.
    /// </summary>
    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        ValidateAllProperties();

        if (HasErrors)
        {
            logger?.LogWarning("Cannot save publisher profile due to validation errors");
            notificationService?.ShowWarning(
                localizationService?.GetString("Tools.PublisherStudio.Profile.ValidationTitle") ?? "Validation Errors",
                localizationService?.GetString("Tools.PublisherStudio.Profile.ValidationMessage") ?? "Please fix the validation errors before saving.",
                NotificationDurations.Medium);
            return;
        }

        try
        {
            ApplyToProject();

            // Persist changes to disk through parent view model silently (avoid duplicate toasts)
            if (parentViewModel != null)
            {
                await parentViewModel.SaveProjectSilentAsync();
            }

            if (subscriptionStore != null && !string.IsNullOrWhiteSpace(PublisherId))
            {
                try
                {
                    var subRes = await subscriptionStore.GetSubscriptionAsync(PublisherId);
                    if (subRes.Success && subRes.Data != null)
                    {
                        var sub = subRes.Data;
                        sub.PublisherName = PublisherName;
                        sub.AvatarUrl = string.IsNullOrWhiteSpace(AvatarUrl)
                            ? null
                            : ImageCacheService.SanitizeRemoteImageUrl(AvatarUrl);
                        await subscriptionStore.UpdateSubscriptionAsync(sub);
                        WeakReferenceMessenger.Default.Send(new PublisherSubscriptionsChangedMessage(PublisherId));
                    }
                }
                catch (Exception ex)
                {
                    logger?.LogWarning(ex, "Failed to update local publisher subscription after saving profile");
                }
            }

            notificationService?.ShowSuccess(
                localizationService?.GetString("Tools.PublisherStudio.Profile.SavedTitle") ?? "Profile Saved",
                localizationService?.GetString("Tools.PublisherStudio.Profile.SavedMessage") ?? "Publisher profile saved successfully.",
                NotificationDurations.Short);
            logger?.LogInformation("Publisher profile saved: {PublisherId} ({PublisherName})", PublisherId, PublisherName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to save publisher profile");
            notificationService?.ShowError(
                localizationService?.GetString("Tools.PublisherStudio.Profile.SaveFailedTitle") ?? "Save Failed",
                $"Failed to save: {ex.Message}",
                NotificationDurations.Long);
        }
    }
}
