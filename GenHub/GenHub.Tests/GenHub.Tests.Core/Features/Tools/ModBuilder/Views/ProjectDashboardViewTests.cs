// <copyright file="ProjectDashboardViewTests.cs" company="Enowx Labs">
// Copyright (c) Enowx Labs. All rights reserved.
// </copyright>

namespace GenHub.Tests.Core.Features.Tools.ModBuilder.Views;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using GenHub.Core.Constants;
using GenHub.Core.Interfaces.Common;
using GenHub.Core.Interfaces.GameInstallations;
using GenHub.Core.Interfaces.Notifications;
using GenHub.Core.Interfaces.Tools.ModBuilder;
using GenHub.Core.Models.Results.ModBuilder;
using GenHub.Features.Tools.ModBuilder.ViewModels;
using GenHub.Features.Tools.ModBuilder.Views;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

/// <summary>
/// Headless layout tests for <see cref="ProjectDashboardView"/>.
/// </summary>
public class ProjectDashboardViewTests
{
    /// <summary>
    /// Verifies that featured template card buttons span the full card width so the
    /// pill, badges, and hover background all render edge to edge instead of sizing
    /// to their text content.
    /// </summary>
    /// <returns>A task representing the asynchronous test operation.</returns>
    [AvaloniaFact]
    public async Task ShowcaseCardButtons_SpanFullCardWidthAsync()
    {
        using var viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        Assert.Equal(4, viewModel.PublisherSampleProjects.Count);

        var view = new ProjectDashboardView { DataContext = viewModel };
        var window = new Window { Width = 1600, Height = 900, Content = view };

        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs(null);

            var cards = view.GetVisualDescendants()
                .OfType<Border>()
                .Where(border => border.Classes.Contains("showcase-card"))
                .ToList();

            Assert.Equal(4, cards.Count);

            foreach (var card in cards)
            {
                var button = card.GetVisualDescendants()
                    .OfType<Button>()
                    .First(candidate => candidate.Classes.Contains("card-action-btn"));
                var expectedWidth = card.Bounds.Width - 2; // BorderThickness of 1 on each side.
                Assert.True(
                    button.Bounds.Width >= expectedWidth - 1,
                    $"Card button width {button.Bounds.Width:F1} should fill card width {card.Bounds.Width:F1}.");
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static ModBuilderViewModel CreateViewModel()
    {
        var mockProjectConfigService = new Mock<IProjectConfigService>();
        mockProjectConfigService
            .Setup(service => service.GetRecentProjectsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ProjectOperationResult<List<string>>.CreateSuccess(new List<string>(), TimeSpan.Zero));
        var mockNotificationService = new Mock<INotificationService>();
        var fileManager = new FileManagerViewModel(
            Mock.Of<IGameInstallationService>(),
            mockNotificationService.Object,
            Mock.Of<ILogger<FileManagerViewModel>>());

        return new ModBuilderViewModel(
            Mock.Of<IBuildEngineService>(),
            mockProjectConfigService.Object,
            Mock.Of<IConfigurationLoaderService>(),
            Mock.Of<IProjectStructureGenerator>(),
            mockNotificationService.Object,
            CreateLocalizationService(),
            fileManager,
            Mock.Of<ILoggerFactory>(),
            Mock.Of<ILogger<ModBuilderViewModel>>());
    }

    private static ILocalizationService CreateLocalizationService()
    {
        var resourceManager = new ResourceManager(LocalizationConstants.StringResourceBaseName, typeof(GenHub.Common.Services.LocalizationService).Assembly);
        var mock = new Mock<ILocalizationService>();
        mock.Setup(service => service.GetString(It.IsAny<string>(), It.IsAny<object?[]>()))
            .Returns<string, object?[]>((key, args) =>
            {
                var value = resourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;
                return args != null && args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, value, args) : value;
            });
        return mock.Object;
    }
}
