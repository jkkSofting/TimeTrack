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
            return db.GetTodoTickets(null).Where(t => t.ColumnId == columnId).OrderBy(t => t.Position).Select(t => t.Titel).ToArray();
        }

        private static int[] Positions(TimeTracker db, int columnId)
        {
            return db.GetTodoTickets(null).Where(t => t.ColumnId == columnId).OrderBy(t => t.Position).Select(t => t.Position).ToArray();
        }

        [Fact]
        public void New_database_has_the_default_columns_in_order()
        {
            using (var t = new TestDb())
            {
                Assert.Empty(t.Db.GetTodoColumns(null)); // erst das Öffnen richtet das Board ein
                t.Db.EnsureTodoBoard(null);
                Assert.Equal(new[] { "To Do", "In Arbeit", "Erledigt" }, t.Db.GetTodoColumns(null).Select(c => c.Name).ToArray());
            }
        }

        [Fact]
        public void Default_columns_are_not_recreated_after_the_user_deleted_them()
        {
            using (var t = new TestDb())
            {
                t.Db.EnsureTodoBoard(null);
                foreach (var c in t.Db.GetTodoColumns(null)) Assert.True(t.Db.DeleteTodoColumn(c.Id));

                t.Restart();
                t.Db.EnsureTodoBoard(null);

                Assert.Empty(t.Db.GetTodoColumns(null));
            }
        }

        [Fact]
        public void Schema_survives_restart_and_keeps_data()
        {
            using (var t = new TestDb())
            {
                t.Db.EnsureTodoBoard(null);
                var col = t.Db.GetTodoColumns(null)[0].Id;
                t.Db.AddTodoTicket(col, "Eins", "Beschreibung\nmit Zeilen");
                t.Db.AddNote("Notiz", "Text", null);

                t.Restart();
                t.Restart();

                var ticket = Assert.Single(t.Db.GetTodoTickets(null));
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
                t.Db.EnsureTodoBoard(null);
                var col = t.Db.GetTodoColumns(null)[0].Id;
                t.Db.AddTodoTicket(col, "A", "");
                t.Db.AddTodoTicket(col, "B", null);
                t.Db.AddTodoTicket(col, "C", "");

                Assert.Equal(new[] { "A", "B", "C" }, Titles(t.Db, col));
                Assert.Equal(new[] { 0, 1, 2 }, Positions(t.Db, col));
                Assert.Equal(string.Empty, t.Db.GetTodoTickets(null).First(x => x.Titel == "B").Beschreibung);
            }
        }

        [Fact]
        public void Ticket_needs_a_title()
        {
            using (var t = new TestDb())
            {
                t.Db.EnsureTodoBoard(null);
                var col = t.Db.GetTodoColumns(null)[0].Id;
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
                t.Db.EnsureTodoBoard(null);
                var col = t.Db.GetTodoColumns(null)[0].Id;
                var id = t.Db.AddTodoTicket(col, "Alt", "alt");

                t.Db.UpdateTodoTicket(id, "  Neu ", "neu");

                var ticket = Assert.Single(t.Db.GetTodoTickets(null));
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
                t.Db.EnsureTodoBoard(null);
                var cols = t.Db.GetTodoColumns(null);
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
                t.Db.EnsureTodoBoard(null);
                var cols = t.Db.GetTodoColumns(null);
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
                t.Db.EnsureTodoBoard(null);
                var col = t.Db.GetTodoColumns(null)[0].Id;
                foreach (var s in new[] { "A", "B", "C" }) t.Db.AddTodoTicket(col, s, "");
                var id = t.Db.GetTodoTickets(null).First(x => x.Titel == title).Id;

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
                t.Db.EnsureTodoBoard(null);
                var cols = t.Db.GetTodoColumns(null);
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
                t.Db.EnsureTodoBoard(null);
                var col = t.Db.GetTodoColumns(null)[0].Id;
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
        public void Column_with_tickets_is_only_deleted_on_request_together_with_its_tickets()
        {
            using (var t = new TestDb())
            {
                t.Db.EnsureTodoBoard(null);
                var cols = t.Db.GetTodoColumns(null);
                t.Db.AddTodoTicket(cols[0].Id, "A", "");
                t.Db.AddTodoTicket(cols[0].Id, "B", "");
                t.Db.AddTodoTicket(cols[2].Id, "Bleibt", "");

                Assert.False(t.Db.DeleteTodoColumn(cols[0].Id));
                Assert.Equal(3, t.Db.GetTodoTickets(null).Count);

                Assert.True(t.Db.DeleteTodoColumn(cols[0].Id, withTickets: true));
                Assert.Equal(new[] { "Bleibt" }, t.Db.GetTodoTickets(null).Select(x => x.Titel).ToArray());
                Assert.Equal(new[] { "In Arbeit", "Erledigt" }, t.Db.GetTodoColumns(null).Select(c => c.Name).ToArray());
                Assert.Equal(new[] { 0, 1 }, t.Db.GetTodoColumns(null).Select(c => c.Position).ToArray());
            }
        }

        [Fact]
        public void Empty_column_is_deleted_and_the_gap_closes()
        {
            using (var t = new TestDb())
            {
                t.Db.EnsureTodoBoard(null);
                var cols = t.Db.GetTodoColumns(null);

                Assert.True(t.Db.DeleteTodoColumn(cols[1].Id));

                Assert.Equal(new[] { "To Do", "Erledigt" }, t.Db.GetTodoColumns(null).Select(c => c.Name).ToArray());
                Assert.Equal(new[] { 0, 1 }, t.Db.GetTodoColumns(null).Select(c => c.Position).ToArray());
                Assert.True(t.Db.DeleteTodoColumn(9999)); // unbekannt: nichts zu tun
            }
        }

        [Fact]
        public void Columns_can_be_added_renamed_and_moved()
        {
            using (var t = new TestDb())
            {
                t.Db.EnsureTodoBoard(null);
                var review = t.Db.AddTodoColumn(null, "  Review ");
                var cols = t.Db.GetTodoColumns(null);
                Assert.Equal("Review", cols.Last().Name);

                t.Db.RenameTodoColumn(review, "Abnahme");
                t.Db.MoveTodoColumn(review, 1);

                Assert.Equal(new[] { "To Do", "Abnahme", "In Arbeit", "Erledigt" }, t.Db.GetTodoColumns(null).Select(c => c.Name).ToArray());

                t.Db.MoveTodoColumn(review, 99);
                Assert.Equal("Abnahme", t.Db.GetTodoColumns(null).Last().Name);

                Assert.Throws<ArgumentException>(() => t.Db.AddTodoColumn(null, " "));
                Assert.Throws<ArgumentException>(() => t.Db.RenameTodoColumn(review, ""));
                Assert.Throws<InvalidOperationException>(() => t.Db.MoveTodoColumn(9999, 0));
            }
        }

        [Fact]
        public void Every_project_has_its_own_board_with_default_columns()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.AddOrEnsureProject("Beta", "Kunde", "KT-2");
                t.Db.EnsureTodoBoard("Alpha");
                t.Db.EnsureTodoBoard("Beta");
                t.Db.EnsureTodoBoard(null);

                var alpha = t.Db.GetTodoColumns("Alpha");
                var beta = t.Db.GetTodoColumns("Beta");
                Assert.Equal(new[] { "To Do", "In Arbeit", "Erledigt" }, alpha.Select(c => c.Name).ToArray());
                Assert.Equal(new[] { "To Do", "In Arbeit", "Erledigt" }, beta.Select(c => c.Name).ToArray());
                Assert.Empty(alpha.Select(c => c.Id).Intersect(beta.Select(c => c.Id)));

                t.Db.AddTodoTicket(alpha[0].Id, "Nur Alpha", "");
                t.Db.AddTodoColumn("Beta", "Nur Beta");

                Assert.Equal(new[] { "Nur Alpha" }, t.Db.GetTodoTickets("Alpha").Select(x => x.Titel).ToArray());
                Assert.Empty(t.Db.GetTodoTickets("Beta"));
                Assert.Empty(t.Db.GetTodoTickets(null));
                Assert.Equal(4, t.Db.GetTodoColumns("Beta").Count);
                Assert.Equal(3, t.Db.GetTodoColumns("Alpha").Count);
                Assert.Equal(3, t.Db.GetTodoColumns(null).Count);
            }
        }

        [Fact]
        public void Board_setup_happens_once_and_unknown_projects_are_rejected()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard("Alpha");
                t.Db.EnsureTodoBoard("Alpha");
                Assert.Equal(3, t.Db.GetTodoColumns("Alpha").Count);

                Assert.Throws<InvalidOperationException>(() => t.Db.EnsureTodoBoard("Gibt es nicht"));
                Assert.Throws<InvalidOperationException>(() => t.Db.GetTodoColumns("Gibt es nicht"));
                Assert.Throws<InvalidOperationException>(() => t.Db.AddTodoColumn("Gibt es nicht", "X"));
            }
        }

        [Fact]
        public void Columns_move_only_within_their_own_board()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard("Alpha");
                t.Db.EnsureTodoBoard(null);
                var alpha = t.Db.GetTodoColumns("Alpha");

                t.Db.MoveTodoColumn(alpha[0].Id, 99);

                Assert.Equal(new[] { "In Arbeit", "Erledigt", "To Do" }, t.Db.GetTodoColumns("Alpha").Select(c => c.Name).ToArray());
                Assert.Equal(new[] { "To Do", "In Arbeit", "Erledigt" }, t.Db.GetTodoColumns(null).Select(c => c.Name).ToArray());
            }
        }

        [Fact]
        public void Ticket_cannot_be_moved_to_a_column_of_another_board()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard("Alpha");
                t.Db.EnsureTodoBoard(null);
                var alpha = t.Db.GetTodoColumns("Alpha");
                var general = t.Db.GetTodoColumns(null);
                var id = t.Db.AddTodoTicket(alpha[0].Id, "A", "");

                Assert.Throws<InvalidOperationException>(() => t.Db.MoveTodoTicket(id, general[0].Id, 0));

                Assert.Equal(new[] { "A" }, t.Db.GetTodoTickets("Alpha").Select(x => x.Titel).ToArray());
                Assert.Empty(t.Db.GetTodoTickets(null));
            }
        }

        [Fact]
        public void Deleting_a_project_removes_its_board_but_not_the_others()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard("Alpha");
                t.Db.EnsureTodoBoard(null);
                t.Db.AddTodoTicket(t.Db.GetTodoColumns("Alpha")[0].Id, "A", "");
                t.Db.AddTodoTicket(t.Db.GetTodoColumns(null)[0].Id, "Allgemein", "");

                t.Db.DeleteProject("Alpha");

                Assert.Equal(0, t.Scalar("SELECT COUNT(*) FROM todo_columns WHERE projekt_id IS NOT NULL"));
                Assert.Equal(new[] { "Allgemein" }, t.Db.GetTodoTickets(null).Select(x => x.Titel).ToArray());
                Assert.Equal(3, t.Db.GetTodoColumns(null).Count);
            }
        }

        [Fact]
        public void Board_of_the_first_version_survives_as_the_general_board()
        {
            using (var t = new TestDb())
            {
                // Zustand der ersten Fassung nachbauen: ein Board ohne Spalte projekt_id, ohne Tabelle todo_boards
                t.Db.Dispose();
                System.Data.SQLite.SQLiteConnection.ClearAllPools();
                using (var conn = new System.Data.SQLite.SQLiteConnection("Data Source=" + t.Path))
                {
                    conn.Open();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                        DROP TABLE todo_tickets; DROP TABLE todo_columns; DROP TABLE todo_boards;
                        CREATE TABLE todo_columns(id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL, position INTEGER NOT NULL);
                        CREATE TABLE todo_tickets(
                            id INTEGER PRIMARY KEY AUTOINCREMENT,
                            column_id INTEGER NOT NULL REFERENCES todo_columns(id) ON DELETE CASCADE,
                            titel TEXT NOT NULL, beschreibung TEXT NOT NULL DEFAULT '', position INTEGER NOT NULL,
                            created_utc TEXT NOT NULL, updated_utc TEXT NOT NULL);
                        INSERT INTO todo_columns (name, position) VALUES ('Alt A', 0), ('Alt B', 1);
                        INSERT INTO todo_tickets (column_id, titel, position, created_utc, updated_utc)
                             VALUES (1, 'Altes Ticket', 0, '2026-11-12T10:00:00Z', '2026-11-12T10:00:00Z');";
                        cmd.ExecuteNonQuery();
                    }
                }
                System.Data.SQLite.SQLiteConnection.ClearAllPools();

                t.Restart();
                t.Restart();
                t.Db.EnsureTodoBoard(null); // darf keine Standardspalten dazulegen

                Assert.Equal(new[] { "Alt A", "Alt B" }, t.Db.GetTodoColumns(null).Select(c => c.Name).ToArray());
                Assert.Equal("Altes Ticket", Assert.Single(t.Db.GetTodoTickets(null)).Titel);
            }
        }

        [Fact]
        public void Notes_are_listed_newest_change_first_and_can_be_edited_and_deleted()
        {
            using (var t = new TestDb())
            {
                t.Db.EnsureTodoBoard(null);
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
                t.Db.EnsureTodoBoard(null);
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
                t.Db.EnsureTodoBoard(null);
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

        // ---------- Quick Switch für Tickets ----------

        [Fact]
        public void Only_the_default_To_Do_column_of_a_project_board_is_offered_in_quick_switch()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard("Alpha");
                t.Db.EnsureTodoBoard(null);
                Assert.Equal(new[] { true, false, false }, t.Db.GetTodoColumns("Alpha").Select(c => c.QuickSwitch).ToArray());
                Assert.Equal(new[] { false, false, false }, t.Db.GetTodoColumns(null).Select(c => c.QuickSwitch).ToArray());
            }
        }

        [Fact]
        public void Quick_switch_lists_tickets_of_enabled_project_columns_sorted_without_the_general_board()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Beta", "Kunde", "KT-2");
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard(null);
                t.Db.EnsureTodoBoard("Alpha");
                t.Db.EnsureTodoBoard("Beta");

                var general = t.Db.GetTodoColumns(null)[0].Id;
                var alpha = t.Db.GetTodoColumns("Alpha");
                var beta = t.Db.GetTodoColumns("Beta");

                t.Db.AddTodoTicket(general, "Ohne Projekt", "");
                t.Db.AddTodoTicket(beta[0].Id, "B-Ticket", "");
                t.Db.AddTodoTicket(alpha[0].Id, "zweites", "");
                t.Db.AddTodoTicket(alpha[0].Id, "Erstes", "");
                t.Db.AddTodoTicket(alpha[1].Id, "In Arbeit", ""); // Spalte nicht eingeschaltet

                var tickets = t.Db.GetQuickSwitchTickets();
                Assert.Equal(new[] { "Alpha/Erstes", "Alpha/zweites", "Beta/B-Ticket" },
                    tickets.Select(x => x.Projektname + "/" + x.Titel).ToArray());
            }
        }

        [Fact]
        public void Quick_switch_follows_the_column_switch_ticket_moves_and_project_rename()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard("Alpha");
                var cols = t.Db.GetTodoColumns("Alpha");
                var ticket = t.Db.AddTodoTicket(cols[0].Id, "Aufgabe", "");
                Assert.Single(t.Db.GetQuickSwitchTickets());

                t.Db.MoveTodoTicket(ticket, cols[1].Id, 0);
                Assert.Empty(t.Db.GetQuickSwitchTickets());

                t.Db.SetTodoColumnQuickSwitch(cols[1].Id, true);
                Assert.True(t.Db.GetTodoColumns("Alpha")[1].QuickSwitch);
                Assert.Equal("Aufgabe", Assert.Single(t.Db.GetQuickSwitchTickets()).Titel);

                t.Db.UpdateProject("Alpha", "Beta", "Kunde", "KT-1");
                Assert.Equal("Beta", Assert.Single(t.Db.GetQuickSwitchTickets()).Projektname);

                t.Db.SetTodoColumnQuickSwitch(cols[1].Id, false);
                Assert.Empty(t.Db.GetQuickSwitchTickets());
            }
        }

        [Fact]
        public void Quick_switch_ticket_disappears_with_its_project_and_the_switch_survives_a_restart()
        {
            using (var t = new TestDb())
            {
                t.Db.AddOrEnsureProject("Alpha", "Kunde", "KT-1");
                t.Db.EnsureTodoBoard("Alpha");
                var cols = t.Db.GetTodoColumns("Alpha");
                t.Db.AddTodoTicket(cols[0].Id, "Aufgabe", "");
                t.Db.SetTodoColumnQuickSwitch(cols[0].Id, false);

                t.Restart();
                Assert.False(t.Db.GetTodoColumns("Alpha")[0].QuickSwitch);

                t.Db.SetTodoColumnQuickSwitch(cols[0].Id, true);
                t.Db.DeleteProject("Alpha");
                Assert.Empty(t.Db.GetQuickSwitchTickets());
            }
        }
    }
}
