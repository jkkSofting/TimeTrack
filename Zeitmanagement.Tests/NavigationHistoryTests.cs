// Target: .NET Framework + C# 7.3
using System;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;

namespace Zeitmanagement.Tests
{
    public class NavigationHistoryTests
    {
        private static NavigationHistory<string> NewHistory(int capacity = NavigationHistory<string>.DefaultCapacity) =>
            new NavigationHistory<string>((a, b) => a == b, capacity);

        [Fact]
        public void Empty_history_has_no_current_and_no_moves()
        {
            var history = NewHistory();

            Assert.Equal(0, history.Count);
            Assert.Null(history.Current);
            Assert.False(history.CanGoBack);
            Assert.False(history.CanGoForward);
        }

        [Fact]
        public void First_push_becomes_current_without_moves()
        {
            var history = NewHistory();

            history.Push("dashboard");

            Assert.Equal("dashboard", history.Current);
            Assert.False(history.CanGoBack);
            Assert.False(history.CanGoForward);
        }

        [Fact]
        public void GoBack_and_GoForward_walk_through_the_pushed_pages()
        {
            var history = NewHistory();
            history.Push("dashboard");
            history.Push("hours");
            history.Push("projects");

            Assert.True(history.CanGoBack);
            Assert.Equal("hours", history.GoBack());
            Assert.Equal("dashboard", history.GoBack());
            Assert.False(history.CanGoBack);

            Assert.True(history.CanGoForward);
            Assert.Equal("hours", history.GoForward());
            Assert.Equal("projects", history.GoForward());
            Assert.False(history.CanGoForward);
            Assert.Equal("projects", history.Current);
        }

        [Fact]
        public void Push_after_going_back_discards_the_forward_entries()
        {
            var history = NewHistory();
            history.Push("dashboard");
            history.Push("hours");
            history.Push("projects");
            history.GoBack();
            history.GoBack();

            history.Push("notes");

            Assert.Equal("notes", history.Current);
            Assert.False(history.CanGoForward);
            Assert.Equal(2, history.Count);
            Assert.Equal("dashboard", history.GoBack());
        }

        [Fact]
        public void Consecutive_equal_entries_are_not_stored_twice()
        {
            var history = NewHistory();
            history.Push("hours");
            history.Push("hours");
            history.Push("hours");

            Assert.Equal(1, history.Count);
            Assert.False(history.CanGoBack);
        }

        [Fact]
        public void Equal_entry_after_going_back_keeps_the_forward_entries()
        {
            var history = NewHistory();
            history.Push("dashboard");
            history.Push("hours");
            history.GoBack();

            history.Push("dashboard");

            Assert.True(history.CanGoForward);
            Assert.Equal("hours", history.GoForward());
        }

        [Fact]
        public void Same_key_is_not_a_duplicate_when_the_comparison_says_different()
        {
            // Beispiel: derselbe Seitenschlüssel, aber ein anderer Tag
            var history = new NavigationHistory<(string Key, DateTime? Day)>(
                (a, b) => a.Key == b.Key && a.Day == b.Day);

            history.Push(("hours", new DateTime(2026, 11, 18)));
            history.Push(("hours", new DateTime(2026, 11, 19)));
            history.Push(("hours", new DateTime(2026, 11, 19)));

            Assert.Equal(2, history.Count);
            Assert.Equal(new DateTime(2026, 11, 18), history.GoBack().Day);
        }

        [Fact]
        public void Oldest_entries_are_dropped_beyond_capacity()
        {
            var history = NewHistory(capacity: 3);
            history.Push("a");
            history.Push("b");
            history.Push("c");
            history.Push("d");

            Assert.Equal(3, history.Count);
            Assert.Equal("d", history.Current);
            Assert.Equal("c", history.GoBack());
            Assert.Equal("b", history.GoBack());
            Assert.False(history.CanGoBack);
        }

        [Fact]
        public void Default_capacity_is_fifty()
        {
            var history = NewHistory();
            for (int i = 0; i < 80; i++)
                history.Push("seite " + i);

            Assert.Equal(50, history.Count);
            Assert.Equal(50, NavigationHistory<string>.DefaultCapacity);
            Assert.Equal("seite 79", history.Current);
            var backSteps = Enumerable.Range(0, 49).Count(_ => history.CanGoBack && history.GoBack() != null);
            Assert.Equal(49, backSteps);
            Assert.False(history.CanGoBack);
            Assert.Equal("seite 30", history.Current);
        }

        [Fact]
        public void GoBack_without_previous_entry_throws()
        {
            var history = NewHistory();
            history.Push("dashboard");

            Assert.Throws<InvalidOperationException>(() => history.GoBack());
        }

        [Fact]
        public void GoForward_without_later_entry_throws()
        {
            var history = NewHistory();
            history.Push("dashboard");

            Assert.Throws<InvalidOperationException>(() => history.GoForward());
        }
    }
}
