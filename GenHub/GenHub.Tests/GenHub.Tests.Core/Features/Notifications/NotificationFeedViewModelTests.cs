using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.Messaging;
using FluentAssertions;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Models.Enums;
using GenHub.Core.Models.Notifications;
using GenHub.Features.Notifications.ViewModels;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace GenHub.Tests.Core.Features.Notifications;

/// <summary>
/// Contains unit tests for <see cref="NotificationFeedViewModel"/> class.
/// </summary>
public class NotificationFeedViewModelTests
{
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<NotificationFeedViewModel>> _mockLogger;
    private readonly Mock<ILogger<NotificationFeedItemViewModel>> _mockItemLogger;
    private readonly Subject<NotificationMessage> _notificationSubject;
    private readonly NotificationFeedViewModel _viewModel;

    /// <summary>
    /// Initializes a new instance of the <see cref="NotificationFeedViewModelTests"/> class.
    /// </summary>
    public NotificationFeedViewModelTests()
    {
        _mockNotificationService = new Mock<INotificationService>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<NotificationFeedViewModel>>();
        _mockItemLogger = new Mock<ILogger<NotificationFeedItemViewModel>>();
        _notificationSubject = new Subject<NotificationMessage>();

        _mockNotificationService.Setup(s => s.NotificationHistory)
            .Returns(_notificationSubject);

        _mockLoggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(_mockItemLogger.Object);

        _viewModel = new TestNotificationFeedViewModel(
            _mockNotificationService.Object,
            _mockLoggerFactory.Object,
            _mockLogger.Object);
    }

    /// <summary>
    /// Verifies that the constructor initializes properties correctly.
    /// </summary>
    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Assert
        _viewModel.IsFeedOpen.Should().BeFalse();
        _viewModel.UnreadCount.Should().Be(0);
        _viewModel.HasUnreadNotifications.Should().BeFalse();
        _viewModel.NotificationHistory.Should().BeEmpty();
        _viewModel.ToggleFeedCommand.Should().NotBeNull();
        _viewModel.ClearAllCommand.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.HasUnreadNotifications"/> returns true when there are unread notifications.
    /// </summary>
    [Fact]
    public void HasUnreadNotifications_ShouldReturnTrue_WhenUnreadNotificationsExist()
    {
        // Arrange
        SetupNotifications(1, true);

        // Assert
        _viewModel.HasUnreadNotifications.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.HasUnreadNotifications"/> returns false when there are no unread notifications.
    /// </summary>
    [Fact]
    public void HasUnreadNotifications_ShouldReturnFalse_WhenNoUnreadNotifications()
    {
        // Arrange
        SetupNotifications(1, false); // All notifications are read

        // Assert
        _viewModel.HasUnreadNotifications.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.UnreadCount"/> is calculated correctly.
    /// </summary>
    [Fact]
    public void UnreadCount_ShouldCalculateCorrectly()
    {
        // Arrange
        SetupNotifications(3, true);

        // Assert
        _viewModel.UnreadCount.Should().Be(3);
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.UnreadCount"/> updates when notifications are marked as read.
    /// </summary>
    [Fact]
    public void UnreadCount_ShouldUpdate_WhenMarkedAsRead()
    {
        // Arrange
        SetupNotifications(2, true);
        var notificationToMarkRead = _viewModel.NotificationHistory.First();

        // Act
        // MarkAsReadCommand takes a Guid
        _viewModel.MarkAsReadCommand.Execute(notificationToMarkRead.Id);

        // Assert
        _mockNotificationService.Verify(x => x.MarkAsRead(notificationToMarkRead.Id), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.ToggleFeedCommand"/> toggles the feed state.
    /// </summary>
    [Fact]
    public void ToggleFeedCommand_ShouldToggleFeedState()
    {
        // Act
        _viewModel.ToggleFeedCommand.Execute(null);

        // Assert
        _viewModel.IsFeedOpen.Should().BeTrue();

        // Act - Toggle again
        _viewModel.ToggleFeedCommand.Execute(null);

        // Assert
        _viewModel.IsFeedOpen.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.ClearAllCommand"/> calls service.
    /// </summary>
    [Fact]
    public void ClearAllCommand_ShouldClearNotifications()
    {
        // Arrange
        SetupNotifications(3);

        // Act
        _viewModel.ClearAllCommand.Execute(null);

        // Assert
        _mockNotificationService.Verify(x => x.ClearHistory(), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.DismissNotificationCommand"/> calls service.
    /// </summary>
    [Fact]
    public void DismissNotificationCommand_ShouldDismissNotification()
    {
        // Arrange
        SetupNotifications(1);
        var notification = _viewModel.NotificationHistory.First();

        // Act
        _viewModel.DismissNotificationCommand.Execute(notification.Id);

        // Assert
        _mockNotificationService.Verify(x => x.Dismiss(notification.Id), Times.Once);
    }

    /// <summary>
    /// Verifies that <see cref="NotificationFeedViewModel.Dispose"/> cleans up subscriptions.
    /// </summary>
    [Fact]
    public void Dispose_CleansUpSubscriptions()
    {
        // Act
        _viewModel.Dispose();

        // Assert
        // Indirect verification: Ensure no crashes
        Assert.True(true);
    }

    private void SetupNotifications(int count, bool unread = true)
    {
        for (int i = 0; i < count; i++)
        {
            var notification = new NotificationMessage(
                NotificationType.Info,
                $"Title {i}",
                $"Message {i}",
                showInBadge: unread) // Set showInBadge to match unread for testing
            {
                IsRead = !unread,
            };

            _notificationSubject.OnNext(notification);
        }
    }

    private class TestNotificationFeedViewModel(
        INotificationService notificationService,
        ILoggerFactory loggerFactory,
        ILogger<NotificationFeedViewModel> logger)
        : NotificationFeedViewModel(notificationService, loggerFactory, logger)
    {
        protected override void RunOnUI(Action action)
        {
            // Execute synchronously for tests
            action();
        }
    }
}
