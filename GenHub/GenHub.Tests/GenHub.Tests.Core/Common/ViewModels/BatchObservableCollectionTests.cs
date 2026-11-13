using GenHub.Common.ViewModels;
using GenHub.Core.Constants;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using Xunit;

namespace GenHub.Tests.Core.Common.ViewModels;

/// <summary>
/// Unit tests for <see cref="BatchObservableCollection{T}"/>.
/// </summary>
public class BatchObservableCollectionTests
{
    /// <summary>
    /// Replacing every item raises one reset plus the count and indexer notifications bindings rely on,
    /// and leaves only the new items.
    /// </summary>
    /// <param name="replacement">The new items.</param>
    [Theory]
    [InlineData(new object[] { new[] { "a", "b", "c" } })]
    [InlineData(new object[] { new string[0] })]
    public void ReplaceAll_RaisesOneResetWithCountAndIndexerNotifications(string[] replacement)
    {
        var collection = new BatchObservableCollection<string> { "old" };
        var actions = new List<NotifyCollectionChangedAction>();
        var properties = new List<string?>();
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);
        ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => properties.Add(e.PropertyName);

        collection.ReplaceAll(replacement);

        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
        Assert.Equal([nameof(collection.Count), UiConstants.IndexerPropertyName], properties);
        Assert.Equal(replacement, collection);
    }

    /// <summary>
    /// A source that throws while it is enumerated leaves the collection unchanged and raises nothing.
    /// </summary>
    [Fact]
    public void ReplaceAll_SourceThrows_LeavesCollectionUnchanged()
    {
        var collection = new BatchObservableCollection<string> { "old" };
        var raised = 0;
        collection.CollectionChanged += (_, _) => raised++;

        Assert.Throws<InvalidOperationException>(() => collection.ReplaceAll(Throwing()));

        Assert.Equal(["old"], collection);
        Assert.Equal(0, raised);
    }

    private static IEnumerable<string> Throwing()
    {
        yield return "new";
        throw new InvalidOperationException("source failed");
    }
}
