using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using SAM.Core.ViewModels;
using Xunit;

namespace SAM.Tests
{
    public class BulkObservableCollectionTests
    {
        [Fact]
        public void ReplaceAllRaisesExactlyOneResetRegardlessOfSize()
        {
            BulkObservableCollection<int> collection = new();
            collection.ReplaceAll(Enumerable.Range(0, 10));

            var events = 0;
            NotifyCollectionChangedAction? action = null;
            collection.CollectionChanged += (_, e) =>
            {
                events++;
                action = e.Action;
            };

            collection.ReplaceAll(Enumerable.Range(1000, 5000));

            Assert.Equal(1, events);
            Assert.Equal(NotifyCollectionChangedAction.Reset, action);
        }

        [Fact]
        public void ReplaceAllLeavesTheCollectionMatchingTheNewSequence()
        {
            BulkObservableCollection<int> collection = new();
            collection.ReplaceAll(new[] { 1, 2, 3 });

            collection.ReplaceAll(new[] { 4, 5 });

            Assert.Equal(new[] { 4, 5 }, collection);
        }

        [Fact]
        public void ReplaceAllOnAnEmptyCollectionWithNoItemsClearsItAndStillFiresOnce()
        {
            BulkObservableCollection<int> collection = new();
            collection.ReplaceAll(new[] { 1, 2, 3 });

            var events = 0;
            collection.CollectionChanged += (_, __) => events++;

            collection.ReplaceAll(Array.Empty<int>());

            Assert.Empty(collection);
            Assert.Equal(1, events);
        }

        [Fact]
        public void ReplaceAllRaisesCountAndIndexerPropertyChanges()
        {
            BulkObservableCollection<int> collection = new();
            var raised = new System.Collections.Generic.List<string>();
            ((INotifyPropertyChanged)collection).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

            collection.ReplaceAll(new[] { 1, 2, 3 });

            Assert.Contains(nameof(collection.Count), raised);
            Assert.Contains("Item[]", raised);
        }

        [Fact]
        public void ReplaceAllRejectsNull()
        {
            BulkObservableCollection<int> collection = new();
            Assert.Throws<ArgumentNullException>(() => collection.ReplaceAll(null));
        }

        [Fact]
        public void IsStillAnOrdinaryObservableCollectionForSingleItemMutations()
        {
            // ReplaceAll is additive: the inherited Add/Remove/Clear behaviour (including their
            // own per-item notifications) must keep working for any other caller.
            BulkObservableCollection<int> collection = new();
            var events = 0;
            collection.CollectionChanged += (_, __) => events++;

            collection.Add(1);
            collection.Add(2);
            collection.Remove(1);

            Assert.Equal(3, events);
            Assert.Equal(new[] { 2 }, collection);
        }
    }
}
