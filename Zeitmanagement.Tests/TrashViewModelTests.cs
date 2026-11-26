// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Zeitmanagement.Helpers;
using Zeitmanagement.ViewModel;

namespace Zeitmanagement.Tests
{
    public class TrashViewModelTests
    {
        private static readonly DateTime Day = new DateTime(2026, 3, 2);

        private sealed class FakeConfirm : IConfirmService
        {
            public bool Answer = true;
            public List<string> Texts { get; } = new List<string>();

            public bool Confirm(string text, string title)
            {
                Texts.Add(text);
                return Answer;
            }
        }

        private static int EntryId(TimeTracker db, string projekt)
        {
            return db.GetTimeEntriesForDate(Day).First(e => e.Projektname == projekt).Id;
        }

        private static void Seed(TimeTracker db)
        {
            db.AddOrEnsureProject("Alpha", "Kunde A", "KT-A");
            db.AddOrEnsureProject("Beta", "Kunde B", "KT-B");
            db.AddTimeEntry(Day, "08:00", "09:00", "Alpha", "a1");
            db.AddTimeEntry(Day, "09:00", "10:00", "Alpha", "a2");
            db.AddTimeEntry(Day, "10:00", "11:00", "Beta", "b1");
        }

        [Fact]
        public void Refresh_lists_trashed_projects_and_entries_with_counts()
        {
            using (var t = new TestDb())
            {
                Seed(t.Db);
                Assert.True(t.Db.MoveProjectToTrash("Alpha"));
                Assert.True(t.Db.MoveTimeEntryToTrash(EntryId(t.Db, "Beta")));

                var vm = new TrashViewModel(t.Db, new FakeConfirm());
                vm.Refresh();

                Assert.Single(vm.Projects);
                Assert.Equal(2, vm.Projects[0].AnzahlBuchungen);
                Assert.Single(vm.Entries);
                Assert.Equal("Beta", vm.Entries[0].Projektname);
                Assert.Equal("Projekte (1)", vm.ProjectsTabText);
                Assert.Equal("Buchungen (1)", vm.EntriesTabText);
                Assert.False(vm.IsEmpty);
            }
        }

        [Fact]
        public void Empty_state_flags()
        {
            using (var t = new TestDb())
            {
                var vm = new TrashViewModel(t.Db, new FakeConfirm());
                vm.Refresh();

                Assert.True(vm.IsEmpty);
                Assert.True(vm.IsProjectsEmpty);
                Assert.True(vm.IsEntriesEmpty);
                Assert.False(vm.HasItems);
                Assert.False(vm.EmptyTrashCommand.CanExecute(null));
            }
        }

        [Fact]
        public void Restore_project_removes_row_and_reports_plain_notice()
        {
            using (var t = new TestDb())
            {
                Seed(t.Db);
                Assert.True(t.Db.MoveProjectToTrash("Alpha"));
                int trashChanged = 0;
                var vm = new TrashViewModel(t.Db, new FakeConfirm(), () => trashChanged++);
                vm.Refresh();

                vm.RestoreProjectCommand.Execute(vm.Projects[0]);

                Assert.Equal("Projekt „Alpha“ wurde wiederhergestellt.", vm.Notice);
                Assert.True(vm.HasNotice);
                Assert.Empty(vm.Projects);
                Assert.Contains(t.Db.GetProjects(), p => p.Projektname == "Alpha");
                Assert.Equal(1, trashChanged);
            }
        }

        [Fact]
        public void Restore_project_with_name_conflict_reports_rename()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde A", "KT-A");
                Assert.True(t.Db.MoveProjectToTrash("Alpha"));
                // Neuer aktiver Projektname: das Papierkorb-Projekt wird zu "Alpha (gelöscht)"
                t.Db.AddOrEnsureProject("Alpha", "Kunde neu", "KT-N");

                var vm = new TrashViewModel(t.Db, new FakeConfirm());
                vm.Refresh();
                Assert.Equal("Alpha (gelöscht)", vm.Projects[0].Projektname);

                vm.RestoreProjectCommand.Execute(vm.Projects[0]);

                Assert.Contains("„Alpha (wiederhergestellt)“", vm.Notice);
                Assert.Contains("bereits vergeben", vm.Notice);
            }
        }

        [Fact]
        public void Restore_entry_of_trashed_project_restores_project_too()
        {
            using (var t = new TestDb())
            {
                Seed(t.Db);
                Assert.True(t.Db.MoveTimeEntryToTrash(EntryId(t.Db, "Alpha")));
                Assert.True(t.Db.MoveProjectToTrash("Alpha"));
                int trashChanged = 0;
                var vm = new TrashViewModel(t.Db, new FakeConfirm(), () => trashChanged++);
                vm.Refresh();
                Assert.Single(vm.Entries);
                Assert.True(vm.Entries[0].ProjektGeloescht);

                vm.RestoreEntryCommand.Execute(vm.Entries[0]);

                Assert.Contains("mit wiederhergestellt", vm.Notice);
                Assert.Contains(t.Db.GetProjects(), p => p.Projektname == "Alpha");
                Assert.Empty(vm.Entries);
                Assert.Equal(1, trashChanged);
            }
        }

        [Fact]
        public void Purge_asks_and_respects_no()
        {
            using (var t = new TestDb())
            {
                Seed(t.Db);
                Assert.True(t.Db.MoveProjectToTrash("Alpha"));
                var confirm = new FakeConfirm { Answer = false };
                var vm = new TrashViewModel(t.Db, confirm);
                vm.Refresh();

                vm.PurgeProjectCommand.Execute(vm.Projects[0]);

                Assert.Single(confirm.Texts);
                Assert.Contains("„Alpha“", confirm.Texts[0]);
                Assert.Contains("nicht rückgängig", confirm.Texts[0]);
                Assert.Single(t.Db.GetTrashedProjects());

                confirm.Answer = true;
                vm.PurgeProjectCommand.Execute(vm.Projects[0]);

                Assert.Empty(vm.Projects);
                Assert.Empty(t.Db.GetTrashedProjects());
            }
        }

        [Fact]
        public void EmptyTrash_confirm_text_contains_counts_and_clears()
        {
            using (var t = new TestDb())
            {
                Seed(t.Db);
                Assert.True(t.Db.MoveProjectToTrash("Alpha"));
                Assert.True(t.Db.MoveTimeEntryToTrash(EntryId(t.Db, "Beta")));
                var confirm = new FakeConfirm();
                var vm = new TrashViewModel(t.Db, confirm);
                vm.Refresh();

                vm.EmptyTrashCommand.Execute(null);

                Assert.Contains("1 Projekt(e) und 1 einzelne Buchung(en)", confirm.Texts.Single());
                Assert.StartsWith("Papierkorb geleert (", vm.Notice);
                Assert.True(vm.IsEmpty);
                Assert.Empty(t.Db.GetTrashedProjects());
                Assert.Empty(t.Db.GetTrashedTimeEntries());
            }
        }

        [Fact]
        public void Refresh_clears_notice_but_restore_keeps_it()
        {
            using (var t = new TestDb())
            {
                Seed(t.Db);
                Assert.True(t.Db.MoveProjectToTrash("Alpha"));
                var vm = new TrashViewModel(t.Db, new FakeConfirm());
                vm.Refresh();

                vm.RestoreProjectCommand.Execute(vm.Projects[0]);
                Assert.NotNull(vm.Notice);

                vm.Refresh();
                Assert.Null(vm.Notice);
                Assert.False(vm.HasNotice);
            }
        }

        [Fact]
        public void Tabs_are_mutually_exclusive()
        {
            using (var t = new TestDb())
            {
                var vm = new TrashViewModel(t.Db, new FakeConfirm());
                Assert.True(vm.ShowProjects);
                Assert.False(vm.ShowEntries);

                vm.ShowEntries = true;
                Assert.False(vm.ShowProjects);
                Assert.True(vm.ShowEntries);

                vm.ShowProjects = true;
                Assert.True(vm.ShowProjects);
                Assert.False(vm.ShowEntries);
            }
        }

        [Theory]
        [InlineData("Alpha", "Alpha", "Projekt „Alpha“ wurde wiederhergestellt.")]
        [InlineData("Alpha (gelöscht)", "Alpha", "Projekt „Alpha (gelöscht)“ wurde als „Alpha“ wiederhergestellt.")]
        [InlineData("Alpha (gelöscht)", "Alpha (wiederhergestellt)", "weil der Name bereits vergeben war.")]
        public void RestoreNotice_static_cases(string original, string actual, string expected)
        {
            string text = TrashViewModel.RestoreNotice(original, actual);

            Assert.Contains(expected, text);
        }
    }
}
