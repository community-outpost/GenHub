using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GenHub.Core.Models.Publishers;

namespace GenHub.Features.Tools.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Publisher Setup Wizard.
/// </summary>
public partial class PublisherSetupWizardViewModel : ObservableValidator
{
    private readonly PublisherStudioProject _project;
    private readonly Action<bool> _closeAction;

    [ObservableProperty]
    private int _currentStep;

    [ObservableProperty]
    private string _stepTitle = "Welcome";

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Publisher ID is required")]
    [RegularExpression("^[a-z0-9]+$", ErrorMessage = "Lowercase, alphanumeric only")]
    private string _publisherId = string.Empty;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "Display Name is required")]
    [MinLength(2, ErrorMessage = "At least 2 characters")]
    private string _publisherName = string.Empty;

    [ObservableProperty]
    private string _websiteUrl = string.Empty;

    [ObservableProperty]
    private string _contactEmail = string.Empty;

    /// <summary>
    /// Gets a value indicating whether the current step is the publisher identity step.
    /// </summary>
    public bool IsStep0 => CurrentStep == 0;

    /// <summary>
    /// Gets a value indicating whether the current step is the contact information step.
    /// </summary>
    public bool IsStep1 => CurrentStep == 1;

    /// <summary>
    /// Gets a value indicating whether the current step is the setup complete step.
    /// </summary>
    public bool IsStep2 => CurrentStep == 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublisherSetupWizardViewModel"/> class.
    /// </summary>
    /// <param name="project">The publisher studio project.</param>
    /// <param name="closeAction">The action to call when closing the wizard.</param>
    public PublisherSetupWizardViewModel(PublisherStudioProject project, Action<bool> closeAction)
    {
        _project = project;
        _closeAction = closeAction;
        CurrentStep = 0;
        UpdateStepTitle();
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

            CurrentStep++;
            UpdateStepTitle();
            Finish();
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
        // Save to project
        _project.Catalog.Publisher.Id = PublisherId;
        _project.Catalog.Publisher.Name = PublisherName;
        _project.Catalog.Publisher.WebsiteUrl = string.IsNullOrWhiteSpace(WebsiteUrl) ? null : WebsiteUrl;
        _project.Catalog.Publisher.ContactEmail = string.IsNullOrWhiteSpace(ContactEmail) ? null : ContactEmail;

        _closeAction(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        _closeAction(false);
    }

    private void UpdateStepTitle()
    {
        StepTitle = CurrentStep switch
        {
            0 => "Publisher Identity",
            1 => "Contact Information",
            2 => "Setup Complete",
            _ => string.Empty,
        };

        OnPropertyChanged(nameof(IsStep0));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
    }
}
