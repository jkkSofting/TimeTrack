// Target: .NET Framework + C# 7.3
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Xunit;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Tests
{
    public class ListSyncTests
    {
        private static ObservableCollection<string> List(params string[] items) => new ObservableCollection<string>(items);

        [Fact]
        public void Unchanged_list_raises_no_change_at_all()
        {
            var list = List("Ohne", "A", "B");
            var changes = 0;
            list.CollectionChanged += (s, e) => changes++;

            ListSync.Sync(list, new[] { "Ohne", "A", "B" });

            Assert.Equal(0, changes);
        }

        [Fact]
        public void Never_resets_the_list_so_a_selection_survives()
        {
            var list = List("Ohne", "A", "C");
            var actions = new System.Collections.Generic.List<NotifyCollectionChangedAction>();
            list.CollectionChanged += (s, e) => actions.Add(e.Action);

            ListSync.Sync(list, new[] { "Ohne", "A", "B", "C", "D" });

            Assert.Equal(new[] { "Ohne", "A", "B", "C", "D" }, list.ToArray());
            Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
        }

        [Fact]
        public void Removes_entries_that_are_gone_and_keeps_the_rest()
        {
            var list = List("Ohne", "A", "B", "C");

            ListSync.Sync(list, new[] { "Ohne", "C" });

            Assert.Equal(new[] { "Ohne", "C" }, list.ToArray());
        }

        [Fact]
        public void Follows_a_new_order_without_duplicates()
        {
            var list = List("Ohne", "B", "A");

            ListSync.Sync(list, new[] { "Ohne", "A", "B" });

            Assert.Equal(new[] { "Ohne", "A", "B" }, list.ToArray());
        }
    }
}
