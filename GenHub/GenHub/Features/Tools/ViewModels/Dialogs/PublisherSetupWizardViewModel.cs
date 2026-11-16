using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Common.Validation;
using GenHub.Core.Models.Publishers;
using System;
using System.Threading.Tasks;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Publisher Setup Wizard.
/// </summary>
public partial class PublisherSetupWizardViewModel(
    PublisherStudioProject project,
    Action<bool> closeAction,
    GenHub.Core.Interfaces.Common.ILocalizationService? localizationService = null) : ObservableValidator
{
    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private string _stepTitle = localizationService?.GetString("Tools.PublisherStudio.SetupWizard.StepIdentity") ?? "Publisher Identity";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [LocalizedRequired("Tools.PublisherStudio.Validation.PublisherIdRequired", "Publisher ID is required")]
    [LocalizedRegularExpression("^[a-z0-9]+$", "Tools.PublisherStudio.Validation.PublisherIdPattern", "Lowercase, alphanumeric only")]
    private string _publisherId = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [LocalizedRequired("Tools.PublisherStudio.Validation.DisplayNameRequired", "Display Name is required")]
    [LocalizedMinLength(2, "Tools.PublisherStudio.Validation.MinLength2", "At least 2 characters")]
    private string _publisherName = string.Empty;

    [ObservableProperty]
    private string _websiteUrl = string.Empty;

    [ObservableProperty]
    private string _contactEmail = string.Empty;

    [ObservableProperty]
    private string? _validationError;

    /// <summary>
    /// Gets a value indicating whether the current step is the publisher identity step.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound to XAML view")]
    public bool IsStep0 => CurrentStep == 0;

    /// <summary>
    /// Gets a value indicating whether the current step is the contact information step.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound to XAML view")]
    public bool IsStep1 => CurrentStep == 1;

    /// <summary>
    /// Gets a value indicating whether the current step is the setup complete step.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S2325:Make member static", Justification = "ViewModel property bound to XAML view")]
    public bool IsStep2 => CurrentStep == 2;

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new System.Net.Mail.MailAddress(email);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep == 0)
        {
            ValidateProperty(PublisherId, nameof(PublisherId));
            ValidateProperty(PublisherName, nameof(PublisherName));

            if (GetErrors(nameof(PublisherId)).GetEnumerator().MoveNext() ||
                GetErrors(nameof(PublisherName)).GetEnumerator().MoveNext())
            {
                return;
            }

            CurrentStep++;
            UpdateStepTitle();
        }
        else if (CurrentStep == 1)
        {
            // Validate Contact info if entered (already validated by attributes on change, but check here)
            // Attributes are optional so empty is valid unless [Required]
            if (HasErrors) return;
            if (!ValidateContactInfo()) return;

            CurrentStep++;
            UpdateStepTitle();
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 0)
        {
            CurrentStep--;
            UpdateStepTitle();
        }
    }

    [RelayCommand]
    private void Finish()
    {
        if (!ValidateContactInfo())
        {
            return;
        }

        // Save to project
        if (project?.Catalog?.Publisher != null)
        {
            project.Catalog.Publisher.Id = PublisherId;
            project.Catalog.Publisher.Name = PublisherName;
            project.Catalog.Publisher.WebsiteUrl = string.IsNullOrWhiteSpace(WebsiteUrl) ? null : WebsiteUrl.Trim();
            project.Catalog.Publisher.ContactEmail = string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail.Trim();

            ArgumentNullException.ThrowIfNull(closeAction);
            closeAction(true);
            return;
        }

        ArgumentNullException.ThrowIfNull(closeAction);
        closeAction(false);
    }

    [RelayCommand]
    private void Cancel()
    {
        ArgumentNullException.ThrowIfNull(closeAction);
        closeAction(false);
    }

    private void UpdateStepTitle()
    {
        StepTitle = CurrentStep switch
        {
            0 => GetLocalizedString("Tools.PublisherStudio.SetupWizard.StepIdentity", "Publisher Identity"),
            1 => GetLocalizedString("Tools.PublisherStudio.SetupWizard.StepContact", "Contact Information"),
            2 => GetLocalizedString("Tools.PublisherStudio.SetupWizard.StepComplete", "Setup Complete"),
            _ => string.Empty,
        };

        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
    }

    private bool ValidateContactInfo()
    {
        if (!string.IsNullOrWhiteSpace(WebsiteUrl) &&
            (!Uri.TryCreate(WebsiteUrl.Trim(), UriKind.Absolute, out var uri) ||
             (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.SetupWizard.InvalidWebsiteUrl",
                "Please enter a valid HTTP or HTTPS website URL.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ContactEmail) && !IsValidEmail(ContactEmail.Trim()))
        {
            ValidationError = GetLocalizedString(
                "Tools.PublisherStudio.SetupWizard.InvalidContactEmail",
                "Please enter a valid email address.");
            return false;
        }

        ValidationError = null;
        return true;
    }

    private string GetLocalizedString(string key, string fallback)
    {
        return localizationService?.GetString(key) ?? fallback;
    }
}
