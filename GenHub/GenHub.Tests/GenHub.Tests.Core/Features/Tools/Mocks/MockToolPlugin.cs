using Avalonia.Controls;
using GenHub.Core.Interfaces.Tools;
using GenHub.Core.Models.Tools;

namespace GenHub.Tests.Core.Features.Tools.Mocks;

/// <summary>
/// Mock implementation of <see cref="IToolPlugin"/> for testing purposes.
/// </summary>
public class MockToolPlugin : IToolPlugin
{
    private bool _isDisposed;
    private bool _isActivated;

    /// <summary>
    /// Initializes a new instance of the <see cref="MockToolPlugin"/> class.
    /// </summary>
    /// <param name="id">The tool ID.</param>
    /// <param name="name">The tool name.</param>
    /// <param name="version">The tool version.</param>
    /// <param name="author">The tool author.</param>
    public MockToolPlugin(string id, string name, string version, string author)
    {
        Metadata = new ToolMetadata
        {
            Id = id,
            Name = name,
            Version = version,
            Author = author,
            Description = $"Mock tool: {name}",
            Tags = new List<string> { "test", "mock" },
        };
    }

    /// <inheritdoc/>
    public ToolMetadata Metadata { get; }

    /// <summary>
    /// Gets a value indicating whether the tool has been disposed.
    /// </summary>
    public bool IsDisposed => _isDisposed;

    /// <summary>
    /// Gets a value indicating whether the tool is currently activated.
    /// </summary>
    public bool IsActivated => _isActivated;

    /// <summary>
    /// Gets the count of times CreateControl has been called.
    /// </summary>
    public int CreateControlCallCount { get; private set; }

    /// <summary>
    /// Gets the count of times OnActivated has been called.
    /// </summary>
    public int OnActivatedCallCount { get; private set; }

    /// <summary>
    /// Gets the count of times OnDeactivated has been called.
    /// </summary>
    public int OnDeactivatedCallCount { get; private set; }

    /// <summary>
    /// Gets the last service provider passed to OnActivated.
    /// </summary>
    public IServiceProvider? LastServiceProvider { get; private set; }

    /// <inheritdoc/>
    public Control CreateControl()
    {
        CreateControlCallCount++;
        return new TextBlock { Text = $"Mock Control for {Metadata.Name}" };
    }

    /// <inheritdoc/>
    public void OnActivated(IServiceProvider serviceProvider)
    {
        OnActivatedCallCount++;
        LastServiceProvider = serviceProvider;
        _isActivated = true;
    }

    /// <inheritdoc/>
    public void OnDeactivated()
    {
        OnDeactivatedCallCount++;
        _isActivated = false;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _isDisposed = true;
        _isActivated = false;
    }
}
