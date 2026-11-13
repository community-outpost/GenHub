using GenHub.Common.ViewModels;
using System.Collections.Generic;
using System.Collections.Specialized;
using Xunit;

namespace GenHub.Tests.Core.Common.ViewModels;

/// <summary>
/// Unit tests for <see cref="BatchObservableCollection{T}"/>.
/// </summary>
public class BatchObservableCollectionTests
{
    /// <summary>
    /// Replacing every item raises a single reset notification and leaves only the new items.
    /// </summary>
    [Fact]
    public void ReplaceAll_RaisesOneResetAndReplacesItems()
    {
        var collection = new BatchObservableCollection<string> { "old" };
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, e) => actions.Add(e.Action);

        collection.ReplaceAll(["a", "b", "c"]);

        Assert.Equal([NotifyCollectionChangedAction.Reset], actions);
        Assert.Equal(["a", "b", "c"], collection);
    }
}
