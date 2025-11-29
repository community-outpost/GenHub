using System.Security;
using GenHub.Core.Interfaces.GitHub;
using GenHub.Features.GitHub.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;

namespace GenHub.Tests.Core.ViewModels;

/// <summary>
/// Tests for GitHubTokenDialogViewModel.
/// </summary>
public class GitHubTokenDialogViewModelTests
{
    private readonly Mock<IGitHubApiClient> _gitHubApiClientMock;
    private readonly Mock<IGitHubTokenStorage> _tokenStorageMock;
    private readonly Mock<ILogger<GitHubTokenDialogViewModel>> _loggerMock;
    private readonly GitHubTokenDialogViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitHubTokenDialogViewModelTests"/> class.
    /// </summary>
    public GitHubTokenDialogViewModelTests()
    {
        _gitHubApiClientMock = new Mock<IGitHubApiClient>();
        _tokenStorageMock = new Mock<IGitHubTokenStorage>();
        _loggerMock = new Mock<ILogger<GitHubTokenDialogViewModel>>();
        _viewModel = new GitHubTokenDialogViewModel(
            _gitHubApiClientMock.Object,
            _tokenStorageMock.Object,
            _loggerMock.Object);
    }

    /// <summary>
    /// Verifies that constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_InitializesPropertiesCorrectly()
    {
        // Assert
        Assert.False(_viewModel.IsValidating);
        Assert.Equal("Please enter a token", _viewModel.ValidationMessage);
        Assert.False(_viewModel.IsTokenValid);
        Assert.False(_viewModel.ShouldClose);
    }

    /// <summary>
    /// Verifies that constructor throws exception when apiClient is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullApiClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GitHubTokenDialogViewModel(null!, _tokenStorageMock.Object, _loggerMock.Object));
    }

    /// <summary>
    /// Verifies that constructor throws exception when logger is null.
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new GitHubTokenDialogViewModel(_gitHubApiClientMock.Object, _tokenStorageMock.Object, null!));
    }

    /// <summary>
    /// Verifies that ValidateTokenAsync handles empty token.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateTokenAsync_HandlesEmptyToken()
    {
        // Arrange
        _viewModel.SetSecureToken(new SecureString());

        // Act
        await _viewModel.ValidateTokenAsync();

        // Assert
        Assert.Equal("Please enter a token", _viewModel.ValidationMessage);
        Assert.False(_viewModel.IsTokenValid);
        Assert.False(_viewModel.IsValidating);
    }

    /// <summary>
    /// Verifies that ValidateTokenAsync validates token successfully.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateTokenAsync_ValidatesTokenSuccessfully()
    {
        // Arrange
        var secureToken = new SecureString();
        foreach (char c in "valid-token")
        {
            secureToken.AppendChar(c);
        }

        _viewModel.SetSecureToken(secureToken);

        // Mock the authenticated user response
        var mockUser = new GenHub.Core.Models.GitHub.GitHubUser { Login = "testuser" };
        _gitHubApiClientMock.Setup(x => x.GetAuthenticatedUserAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockUser);

        // Act
        await _viewModel.ValidateTokenAsync();

        // Assert
        Assert.Contains("Token validated successfully", _viewModel.ValidationMessage);
        Assert.True(_viewModel.IsTokenValid);
        Assert.False(_viewModel.IsValidating);
        _gitHubApiClientMock.Verify(x => x.SetAuthenticationToken(It.Is<SecureString>(s => s.Length == "valid-token".Length)), Times.Once);
    }

    /// <summary>
    /// Verifies that ValidateTokenAsync handles validation failure.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task ValidateTokenAsync_HandlesValidationFailure()
    {
        // Arrange
        var secureToken = new SecureString();
        foreach (char c in "invalid-token")
        {
            secureToken.AppendChar(c);
        }

        _viewModel.SetSecureToken(secureToken);
        _gitHubApiClientMock.Setup(x => x.SetAuthenticationToken(It.IsAny<SecureString>()))
            .Throws(new Exception("Invalid token"));

        // Act
        await _viewModel.ValidateTokenAsync();

        // Assert
        Assert.Contains("Invalid token", _viewModel.ValidationMessage);
        Assert.False(_viewModel.IsTokenValid);
        Assert.False(_viewModel.IsValidating);
    }

    /// <summary>
    /// Verifies that Save saves valid token and closes dialog.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Save_SavesValidTokenAndClosesDialog()
    {
        // Arrange
        var secureToken = new SecureString();
        foreach (char c in "valid-token")
        {
            secureToken.AppendChar(c);
        }

        _viewModel.SetSecureToken(secureToken);
        _viewModel.IsTokenValid = true;

        // Act
        await _viewModel.SaveAsync();

        // Assert
        Assert.True(_viewModel.ShouldClose);
        _tokenStorageMock.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Once);
    }

    /// <summary>
    /// Verifies that Save does not close dialog for invalid token.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [Fact]
    public async Task Save_DoesNotCloseDialogForInvalidToken()
    {
        // Arrange
        var secureToken = new SecureString();
        foreach (char c in "invalid-token")
        {
            secureToken.AppendChar(c);
        }

        _viewModel.SetSecureToken(secureToken);
        _viewModel.IsTokenValid = false;

        // Act
        await _viewModel.SaveAsync();

        // Assert
        Assert.False(_viewModel.ShouldClose);
        _tokenStorageMock.Verify(x => x.SaveTokenAsync(It.IsAny<SecureString>()), Times.Never);
    }

    /// <summary>
    /// Verifies that Cancel closes dialog without saving.
    /// </summary>
    [Fact]
    public void Cancel_ClosesDialogWithoutSaving()
    {
        // Arrange
        var secureToken = new SecureString();
        foreach (char c in "test-token")
        {
            secureToken.AppendChar(c);
        }

        _viewModel.SetSecureToken(secureToken);
        _viewModel.IsTokenValid = true;
        _viewModel.ValidationMessage = "Valid";

        // Act
        _viewModel.Cancel();

        // Assert
        Assert.True(_viewModel.ShouldClose);

        // Validation state should remain unchanged
        Assert.True(_viewModel.IsTokenValid);
    }

    /// <summary>
    /// Verifies that CanSave returns true only when token is valid and not validating.
    /// </summary>
    /// <param name="isTokenValid">Whether the token is valid.</param>
    /// <param name="isValidating">Whether validation is in progress.</param>
    /// <param name="expected">The expected result.</param>
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    public void CanSave_ReturnsCorrectValue(bool isTokenValid, bool isValidating, bool expected)
    {
        // Arrange
        var secureToken = new SecureString();
        foreach (char c in "test-token")
        {
            secureToken.AppendChar(c);
        }

        _viewModel.SetSecureToken(secureToken);
        _viewModel.IsTokenValid = isTokenValid;
        _viewModel.IsValidating = isValidating;

        // Act & Assert
        Assert.Equal(expected, _viewModel.CanSave);
    }

    /// <summary>
    /// Verifies that CanSave command's CanExecute method returns correct value based on token validity and validation state.
    /// </summary>
    [Fact]
    public void CanSaveCommand_CanExecute_ReturnsCorrectValue()
    {
        // Arrange
        var secureToken = new SecureString();
        foreach (char c in "test-token")
        {
            secureToken.AppendChar(c);
        }

        _viewModel.SetSecureToken(secureToken);
        _viewModel.IsTokenValid = true;
        _viewModel.IsValidating = false;

        // Act & Assert
        Assert.True(_viewModel.SaveCommand.CanExecute(null));

        // Arrange
        _viewModel.IsTokenValid = false;

        // Force command to re-evaluate CanExecute
        _viewModel.SaveCommand.NotifyCanExecuteChanged();

        // Act & Assert
        Assert.False(_viewModel.SaveCommand.CanExecute(null));
    }
}
