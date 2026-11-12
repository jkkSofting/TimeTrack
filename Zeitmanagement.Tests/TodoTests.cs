// Target: .NET Framework + C# 7.3
using System;
using System.Linq;
using Xunit;

namespace Zeitmanagement.Tests
{
    public class TodoTests
    {
        private static string[] Titles(TimeTracker db, int columnId)
        {
            return db.GetTodoTickets().Where(t => t.ColumnId == columnId).OrderBy(t => t.Position).Select(t => t.Titel).ToArray();
        }

        private static int[] Positions(TimeTracker db, int columnId)
        {
            return db.GetTodoTickets().Where(t => t.ColumnId == columnId).OrderBy(t => t.Position).Select(t => t.Position).ToArray();
        }

        [Fact]
        public void New_database_has_the_default_columns_in_order()
        {
            using (var t = new TestDb())
            {
                Assert.Equal(new[] { "To Do", "In Arbeit", "Erledigt" }, t.Db.GetTodoColumns().Select(c => c.Name).ToArray());
            }
        }

        [Fact]
        public void Default_columns_are_not_recreated_after_the_user_deleted_them()
        {
            using (var t = new TestDb())
            {
                foreach (var c in t.Db.GetTodoColumns()) Assert.True(t.Db.DeleteTodoColumn(c.Id));

                t.Restart();

                Assert.Empty(t.Db.GetTodoColumns());
            }
        }

        [Fact]
        public void Schema_survives_restart_and_keeps_data()
        {
            using (var t = new TestDb())
            {
                var col = t.Db.GetTodoColumns()[0].Id;
                t.Db.AddTodoTicket(col, "Eins", "Beschreibung\nmit Zeilen");
                t.Db.AddNote("Notiz", "Text", null);

                t.Restart();
                t.Restart();

                var ticket = Assert.Single(t.Db.GetTodoTickets());
                Assert.Equal("Eins", ticket.Titel);
                Assert.Equal("Beschreibung\nmit Zeilen", ticket.Beschreibung);
                Assert.Equal("Notiz", Assert.Single(t.Db.GetNotes()).Titel);
            }
        }

        [Fact]
        public void Tickets_are_appended_to_the_end_of_their_column()
        {
            using (var t = new TestDb())
            {
                var col = t.Db.GetTodoColumns()[0].Id;
                t.Db.AddTodoTicket(col, "A", "");
                t.Db.AddTodoTicket(col, "B", null);
                t.Db.AddTodoTicket(col, "C", "");

                Assert.Equal(new[] { "A", "B", "C" }, Titles(t.Db, col));
                Assert.Equal(new[] { 0, 1, 2 }, Positions(t.Db, col));
                Assert.Equal(string.Empty, t.Db.GetTodoTickets().First(x => x.Titel == "B").Beschreibung);
            }
        }

        [Fact]
        public void Ticket_needs_a_title()
        {
            using (var t = new TestDb())
            {
                var col = t.Db.GetTodoColumns()[0].Id;
                Assert.Throws<ArgumentException>(() => t.Db.AddTodoTicket(col, "  ", "x"));

                var id = t.Db.AddTodoTicket(col, "A", "");
                Assert.Throws<ArgumentException>(() => t.Db.UpdateTodoTicket(id, "", "x"));
            }
        }

        [Fact]
        public void Update_changes_title_and_description()
        {
            using (var t = new TestDb())
            {
                var col = t.Db.GetTodoColumns()[0].Id;
                var id = t.Db.AddTodoTicket(col, "Alt", "alt");

                t.Db.UpdateTodoTicket(id, "  Neu ", "neu");

                var ticket = Assert.Single(t.Db.GetTodoTickets());
                Assert.Equal("Neu", ticket.Titel);
                Assert.Equal("neu", ticket.Beschreibung);
            }
        }

        [Theory]
        [InlineData(0, new[] { "D", "A", "B", "C" })]
        [InlineData(1, new[] { "A", "D", "B", "C" })]
        [InlineData(3, new[] { "A", "B", "C", "D" })]
        [InlineData(99, new[] { "A", "B", "C", "D" })]
        [InlineData(-5, new[] { "D", "A", "B", "C" })]
        public void Move_into_another_column_inserts_at_the_index(int index, string[] expected)
        {
            using (var t = new TestDb())
            {
                var cols = t.Db.GetTodoColumns();
                foreach (var title in new[] { "A", "B", "C" }) t.Db.AddTodoTicket(cols[0].Id, title, "");
                var d = t.Db.AddTodoTicket(cols[1].Id, "D", "");

                t.Db.MoveTodoTicket(d, cols[0].Id, index);

                Assert.Equal(expected, Titles(t.Db, cols[0].Id));
                Assert.Equal(new[] { 0, 1, 2, 3 }, Positions(t.Db, cols[0].Id));
                Assert.Empty(Titles(t.Db, cols[1].Id));
            }
        }

        [Fact]
        public void Move_out_of_a_column_closes_the_gap()
        {
            using (var t = new TestDb())
            {
                var cols = t.Db.GetTodoColumns();
                t.Db.AddTodoTicket(cols[0].Id, "A", "");
                var b = t.Db.AddTodoTicket(cols[0].Id, "B", "");
                t.Db.AddTodoTicket(cols[0].Id, "C", "");

                t.Db.MoveTodoTicket(b, cols[2].Id, 0);

                Assert.Equal(new[] { "A", "C" }, Titles(t.Db, cols[0].Id));
                Assert.Equal(new[] { 0, 1 }, Positions(t.Db, cols[0].Id));
                Assert.Equal(new[] { "B" }, Titles(t.Db, cols[2].Id));
            }
        }

        [Theory]
        [InlineData("A", 2, new[] { "B", "C", "A" })]
        [InlineData("C", 0, new[] { "C", "A", "B" })]
        [InlineData("B", 0, new[] { "B", "A", "C" })]
        [InlineData("B", 1, new[] { "A", "B", "C" })]
        public void Move_within_a_column_reorders_it(string title, int index, string[] expected)
        {
            using (var t = new TestDb())
            {
                var col = t.Db.GetTodoColumns()[0].Id;
                foreach (var s in new[] { "A", "B", "C" }) t.Db.AddTodoTicket(col, s, "");
                var id = t.Db.GetTodoTickets().First(x => x.Titel == title).Id;

                t.Db.MoveTodoTicket(id, col, index);

                Assert.Equal(expected, Titles(t.Db, col));
                Assert.Equal(new[] { 0, 1, 2 }, Positions(t.Db, col));
            }
        }

        [Fact]
        public void Move_rejects_unknown_ticket_and_column_without_changing_anything()
        {
            using (var t = new TestDb())
            {
                var cols = t.Db.GetTodoColumns();
                var a = t.Db.AddTodoTicket(cols[0].Id, "A", "");

                Assert.Throws<InvalidOperationException>(() => t.Db.MoveTodoTicket(9999, cols[0].Id, 0));
                Assert.Throws<InvalidOperationException>(() => t.Db.MoveTodoTicket(a, 9999, 0));

                Assert.Equal(new[] { "A" }, Titles(t.Db, cols[0].Id));
            }
        }

        [Fact]
        public void Deleting_a_ticket_closes_the_gap()
        {
            using (var t = new TestDb())
            {
                var col = t.Db.GetTodoColumns()[0].Id;
                t.Db.AddTodoTicket(col, "A", "");
                var b = t.Db.AddTodoTicket(col, "B", "");
                t.Db.AddTodoTicket(col, "C", "");

                t.Db.DeleteTodoTicket(b);
                t.Db.DeleteTodoTicket(b); // zweimal ist kein Fehler

                Assert.Equal(new[] { "A", "C" }, Titles(t.Db, col));
                Assert.Equal(new[] { 0, 1 }, Positions(t.Db, col));
            }
        }

        [Fact]
        public void Column_with_tickets_is_not_deleted_but_an_empty_one_is()
        {
            using (var t = new TestDb())
            {
                var cols = t.Db.GetTodoColumns();
                t.Db.AddTodoTicket(cols[0].Id, "A", "");

                Assert.False(t.Db.DeleteTodoColumn(cols[0].Id));
                Assert.Single(t.Db.GetTodoTickets());

                Assert.True(t.Db.DeleteTodoColumn(cols[1].Id));
                Assert.Equal(new[] { "To Do", "Erledigt" }, t.Db.GetTodoColumns().Select(c => c.Name).ToArray());
                Assert.Equal(new[] { 0, 1 }, t.Db.GetTodoColumns().Select(c => c.Position).ToArray());
            }
        }

        [Fact]
        public void Columns_can_be_added_renamed_and_moved()
        {
            using (var t = new TestDb())
            {
                var review = t.Db.AddTodoColumn("  Review ");
                var cols = t.Db.GetTodoColumns();
                Assert.Equal("Review", cols.Last().Name);

                t.Db.RenameTodoColumn(review, "Abnahme");
                t.Db.MoveTodoColumn(review, 1);

                Assert.Equal(new[] { "To Do", "Abnahme", "In Arbeit", "Erledigt" }, t.Db.GetTodoColumns().Select(c => c.Name).ToArray());

                t.Db.MoveTodoColumn(review, 99);
                Assert.Equal("Abnahme", t.Db.GetTodoColumns().Last().Name);

                Assert.Throws<ArgumentException>(() => t.Db.AddTodoColumn(" "));
                Assert.Throws<ArgumentException>(() => t.Db.RenameTodoColumn(review, ""));
                Assert.Throws<InvalidOperationException>(() => t.Db.MoveTodoColumn(9999, 0));
            }
        }

        [Fact]
        public void Notes_are_listed_newest_change_first_and_can_be_edited_and_deleted()
        {
            using (var t = new TestDb())
            {
                var first = t.Db.AddNote("Erste", "a", null);
                System.Threading.Thread.Sleep(1100); // Zeitstempel haben Sekundenauflösung
                var second = t.Db.AddNote("Zweite", "b", null);

                Assert.Equal(new[] { "Zweite", "Erste" }, t.Db.GetNotes().Select(n => n.Titel).ToArray());

                System.Threading.Thread.Sleep(1100);
                t.Db.UpdateNote(first, "Erste (neu)", "a2", null);

                var notes = t.Db.GetNotes();
                Assert.Equal(new[] { "Erste (neu)", "Zweite" }, notes.Select(n => n.Titel).ToArray());
                Assert.Equal("a2", notes[0].Text);
                Assert.True(notes[0].UpdatedUtc > notes[0].CreatedUtc);

                t.Db.DeleteNote(second);
                Assert.Equal(new[] { "Erste (neu)" }, t.Db.GetNotes().Select(n => n.Titel).ToArray());
            }
        }

        [Fact]
        public void Note_project_is_optional_and_unknown_projects_mean_no_project()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");

                var a = t.Db.AddNote("Mit", "", "Alpha");
                var b = t.Db.AddNote("Ohne", "", "");
                var c = t.Db.AddNote("Unbekannt", "", "Gibt es nicht");

                var notes = t.Db.GetNotes();
                Assert.Equal("Alpha", notes.First(n => n.Id == a).Projektname);
                Assert.Null(notes.First(n => n.Id == b).Projektname);
                Assert.Null(notes.First(n => n.Id == c).Projektname);

                t.Db.UpdateNote(b, "Ohne", "", "Alpha");
                Assert.Equal("Alpha", t.Db.GetNotes().First(n => n.Id == b).Projektname);
                t.Db.UpdateNote(b, "Ohne", "", null);
                Assert.Null(t.Db.GetNotes().First(n => n.Id == b).Projektname);
            }
        }

        [Fact]
        public void Note_follows_a_project_rename_and_survives_its_deletion()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.AddNote("N", "text", "Alpha");

                t.Db.UpdateProject("Alpha", "Beta", "Kunde", "KT-1");
                Assert.Equal("Beta", Assert.Single(t.Db.GetNotes()).Projektname);

                t.Db.DeleteProject("Beta");
                var note = Assert.Single(t.Db.GetNotes());
                Assert.Null(note.Projektname);
                Assert.Equal("text", note.Text);
            }
        }
    }
}
