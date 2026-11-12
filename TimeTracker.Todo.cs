// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;

// To-Do-Board (Spalten und Tickets) und Projektnotizen. Alles liegt in derselben Datenbank wie die Zeiterfassung, damit
// Sicherungen es mitnehmen. Die Tabellen der Zeiterfassung und ihre Trigger bleiben unberührt; Notizen verweisen
// höchstens über die Projekt-ID auf ein Projekt (und überleben dessen Löschen ohne Zuordnung).
public sealed partial class TimeTracker
{
    private const string UtcFormat = "yyyy-MM-ddTHH:mm:ssZ";

    /// <summary>Spalten, die ein frisch angelegtes Board bekommt.</summary>
    private static readonly string[] DefaultTodoColumns = { "To Do", "In Arbeit", "Erledigt" };

    /// <summary>
    /// Legt die Tabellen für Board und Notizen an (idempotent, nur additiv). Läuft innerhalb der Transaktion von
    /// <see cref="EnsureSchema"/>. Die Standardspalten gibt es nur, wenn die Tabelle neu entsteht: Wer alle Spalten
    /// löscht, soll sie beim nächsten Start nicht wieder auftauchen sehen.
    /// </summary>
    private static void EnsureTodoSchema(SQLiteCommand cmd)
    {
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'todo_columns';";
        var isNew = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0;

        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS todo_columns(
            id       INTEGER PRIMARY KEY AUTOINCREMENT,
            name     TEXT    NOT NULL,
            position INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS todo_tickets(
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            column_id    INTEGER NOT NULL REFERENCES todo_columns(id) ON DELETE CASCADE,
            titel        TEXT    NOT NULL,
            beschreibung TEXT    NOT NULL DEFAULT '',
            position     INTEGER NOT NULL,
            created_utc  TEXT    NOT NULL,  -- yyyy-MM-ddTHH:mm:ssZ
            updated_utc  TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_todo_tickets_col ON todo_tickets(column_id, position);

        CREATE TABLE IF NOT EXISTS notes(
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            titel       TEXT    NOT NULL DEFAULT '',
            text        TEXT    NOT NULL DEFAULT '',
            projekt_id  INTEGER NULL REFERENCES projects(id) ON DELETE SET NULL,
            created_utc TEXT    NOT NULL,
            updated_utc TEXT    NOT NULL
        );
        CREATE INDEX IF NOT EXISTS idx_notes_updated ON notes(updated_utc);";
        cmd.ExecuteNonQuery();

        if (!isNew) return;

        cmd.CommandText = "INSERT INTO todo_columns (name, position) VALUES (@n, @p);";
        for (var i = 0; i < DefaultTodoColumns.Length; i++)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@n", DefaultTodoColumns[i]);
            cmd.Parameters.AddWithValue("@p", i);
            cmd.ExecuteNonQuery();
        }
        cmd.Parameters.Clear();
    }

    private static string UtcNow() => DateTime.UtcNow.ToString(UtcFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Schreibt die Reihenfolge einer Liste (Position = Index) in die Tabelle. <paramref name="table"/> ist immer
    /// eine Konstante dieser Datei, nie Eingabe von außen.
    /// </summary>
    private static void WritePositions(SQLiteCommand cmd, string table, IList<int> ids)
    {
        cmd.CommandText = "UPDATE " + table + " SET position = @p WHERE id = @id;";
        for (var i = 0; i < ids.Count; i++)
        {
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@p", i);
            cmd.Parameters.AddWithValue("@id", ids[i]);
            cmd.ExecuteNonQuery();
        }
        cmd.Parameters.Clear();
    }

    private static List<int> ReadIds(SQLiteCommand cmd, string sql, params SQLiteParameter[] parameters)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = sql;
        foreach (var p in parameters) cmd.Parameters.Add(p);

        var ids = new List<int>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) ids.Add(r.GetInt32(0));
        }
        cmd.Parameters.Clear();
        return ids;
    }

    private static int ClampIndex(int index, int count)
    {
        return index < 0 ? 0 : index > count ? count : index;
    }

    // ---------- Board: Spalten ----------

    public List<TodoColumnRow> GetTodoColumns()
    {
        var result = new List<TodoColumnRow>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name, position FROM todo_columns ORDER BY position, id;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    result.Add(new TodoColumnRow { Id = r.GetInt32(0), Name = r.GetString(1), Position = r.GetInt32(2) });
            }
        }
        return result;
    }

    /// <summary>Hängt eine Spalte hinten an und liefert ihre Id.</summary>
    public int AddTodoColumn(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Der Name der Spalte darf nicht leer sein.", nameof(name));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            INSERT INTO todo_columns (name, position)
                 VALUES (@n, (SELECT IFNULL(MAX(position), -1) + 1 FROM todo_columns));
            SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@n", name.Trim());
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public void RenameTodoColumn(int columnId, string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Der Name der Spalte darf nicht leer sein.", nameof(name));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE todo_columns SET name = @n WHERE id = @id;";
            cmd.Parameters.AddWithValue("@n", name.Trim());
            cmd.Parameters.AddWithValue("@id", columnId);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Setzt die Spalte an die Stelle <paramref name="newIndex"/> (0 = ganz links); ein zu großer Wert heißt: ganz rechts.</summary>
    public void MoveTodoColumn(int columnId, int newIndex)
    {
        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            var ids = ReadIds(cmd, "SELECT id FROM todo_columns ORDER BY position, id;");
            if (!ids.Remove(columnId))
            {
                tx.Rollback();
                throw new InvalidOperationException("Die Spalte existiert nicht.");
            }

            ids.Insert(ClampIndex(newIndex, ids.Count), columnId);
            WritePositions(cmd, "todo_columns", ids);
            tx.Commit();
        }
    }

    /// <summary>
    /// Löscht eine Spalte. Eine Spalte mit Tickets wird nie gelöscht (<c>false</c>), damit keine Tickets verloren gehen.
    /// </summary>
    public bool DeleteTodoColumn(int columnId)
    {
        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = "SELECT COUNT(*) FROM todo_tickets WHERE column_id = @id;";
            cmd.Parameters.AddWithValue("@id", columnId);
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0)
            {
                tx.Rollback();
                return false;
            }

            cmd.CommandText = "DELETE FROM todo_columns WHERE id = @id;";
            cmd.ExecuteNonQuery();

            // Lücke in den Positionen schließen
            WritePositions(cmd, "todo_columns", ReadIds(cmd, "SELECT id FROM todo_columns ORDER BY position, id;"));
            tx.Commit();
            return true;
        }
    }

    // ---------- Board: Tickets ----------

    /// <summary>Alle Tickets, je Spalte nach ihrer Reihenfolge.</summary>
    public List<TodoTicketRow> GetTodoTickets()
    {
        var result = new List<TodoTicketRow>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT id, column_id, titel, beschreibung, position, created_utc, updated_utc
              FROM todo_tickets
             ORDER BY column_id, position, id;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new TodoTicketRow
                    {
                        Id = r.GetInt32(0),
                        ColumnId = r.GetInt32(1),
                        Titel = r.GetString(2),
                        Beschreibung = r.GetString(3),
                        Position = r.GetInt32(4),
                        CreatedUtc = ParseUtc(r.GetString(5)),
                        UpdatedUtc = ParseUtc(r.GetString(6))
                    });
                }
            }
        }
        return result;
    }

    /// <summary>Legt ein Ticket ans Ende der Spalte und liefert seine Id.</summary>
    public int AddTodoTicket(int columnId, string titel, string beschreibung)
    {
        if (string.IsNullOrWhiteSpace(titel)) throw new ArgumentException("Der Titel darf nicht leer sein.", nameof(titel));

        using (var cmd = _conn.CreateCommand())
        {
            var now = UtcNow();
            cmd.CommandText = @"
            INSERT INTO todo_tickets (column_id, titel, beschreibung, position, created_utc, updated_utc)
                 VALUES (@c, @t, @b,
                         (SELECT IFNULL(MAX(position), -1) + 1 FROM todo_tickets WHERE column_id = @c),
                         @now, @now);
            SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@c", columnId);
            cmd.Parameters.AddWithValue("@t", titel.Trim());
            cmd.Parameters.AddWithValue("@b", beschreibung ?? string.Empty);
            cmd.Parameters.AddWithValue("@now", now);
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public void UpdateTodoTicket(int ticketId, string titel, string beschreibung)
    {
        if (string.IsNullOrWhiteSpace(titel)) throw new ArgumentException("Der Titel darf nicht leer sein.", nameof(titel));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            UPDATE todo_tickets
               SET titel = @t, beschreibung = @b, updated_utc = @now
             WHERE id = @id;";
            cmd.Parameters.AddWithValue("@t", titel.Trim());
            cmd.Parameters.AddWithValue("@b", beschreibung ?? string.Empty);
            cmd.Parameters.AddWithValue("@now", UtcNow());
            cmd.Parameters.AddWithValue("@id", ticketId);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteTodoTicket(int ticketId)
    {
        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = "SELECT column_id FROM todo_tickets WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", ticketId);
            var column = cmd.ExecuteScalar();
            if (column == null || column == DBNull.Value)
            {
                tx.Rollback();
                return;
            }

            cmd.CommandText = "DELETE FROM todo_tickets WHERE id = @id;";
            cmd.ExecuteNonQuery();

            WritePositions(cmd, "todo_tickets", ReadIds(cmd,
                "SELECT id FROM todo_tickets WHERE column_id = @c ORDER BY position, id;",
                new SQLiteParameter("@c", Convert.ToInt32(column, CultureInfo.InvariantCulture))));
            tx.Commit();
        }
    }

    /// <summary>
    /// Verschiebt ein Ticket in die Spalte <paramref name="targetColumnId"/> (auch dieselbe) an die Stelle
    /// <paramref name="targetIndex"/>. Der Index zählt in der Zielspalte ohne das Ticket selbst; ein zu großer Wert
    /// heißt: ans Ende.
    /// </summary>
    public void MoveTodoTicket(int ticketId, int targetColumnId, int targetIndex)
    {
        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText = "SELECT column_id FROM todo_tickets WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", ticketId);
            var source = cmd.ExecuteScalar();
            if (source == null || source == DBNull.Value)
            {
                tx.Rollback();
                throw new InvalidOperationException("Das Ticket existiert nicht.");
            }
            var sourceColumnId = Convert.ToInt32(source, CultureInfo.InvariantCulture);

            cmd.CommandText = "SELECT COUNT(*) FROM todo_columns WHERE id = @c;";
            cmd.Parameters.Clear();
            cmd.Parameters.AddWithValue("@c", targetColumnId);
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                tx.Rollback();
                throw new InvalidOperationException("Die Zielspalte existiert nicht.");
            }

            var ids = ReadIds(cmd,
                "SELECT id FROM todo_tickets WHERE column_id = @c AND id <> @id ORDER BY position, id;",
                new SQLiteParameter("@c", targetColumnId), new SQLiteParameter("@id", ticketId));
            ids.Insert(ClampIndex(targetIndex, ids.Count), ticketId);

            cmd.CommandText = "UPDATE todo_tickets SET column_id = @c WHERE id = @id;";
            cmd.Parameters.AddWithValue("@c", targetColumnId);
            cmd.Parameters.AddWithValue("@id", ticketId);
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();

            WritePositions(cmd, "todo_tickets", ids);

            if (sourceColumnId != targetColumnId)
            {
                WritePositions(cmd, "todo_tickets", ReadIds(cmd,
                    "SELECT id FROM todo_tickets WHERE column_id = @c ORDER BY position, id;",
                    new SQLiteParameter("@c", sourceColumnId)));
            }

            tx.Commit();
        }
    }

    // ---------- Notizen ----------

    /// <summary>Alle Notizen, die zuletzt geänderte zuerst.</summary>
    public List<NoteRow> GetNotes()
    {
        var result = new List<NoteRow>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT n.id, n.titel, n.text, p.projektname, n.created_utc, n.updated_utc
              FROM notes n
              LEFT JOIN projects p ON p.id = n.projekt_id
             ORDER BY n.updated_utc DESC, n.id DESC;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new NoteRow
                    {
                        Id = r.GetInt32(0),
                        Titel = r.GetString(1),
                        Text = r.GetString(2),
                        Projektname = r.IsDBNull(3) ? null : r.GetString(3),
                        CreatedUtc = ParseUtc(r.GetString(4)),
                        UpdatedUtc = ParseUtc(r.GetString(5))
                    });
                }
            }
        }
        return result;
    }

    /// <summary>Legt eine Notiz an und liefert ihre Id. Ein unbekannter oder leerer Projektname heißt: ohne Projekt.</summary>
    public int AddNote(string titel, string text, string projektname)
    {
        using (var cmd = _conn.CreateCommand())
        {
            var now = UtcNow();
            cmd.CommandText = @"
            INSERT INTO notes (titel, text, projekt_id, created_utc, updated_utc)
                 VALUES (@t, @x, (SELECT id FROM projects WHERE projektname = @p), @now, @now);
            SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@t", titel ?? string.Empty);
            cmd.Parameters.AddWithValue("@x", text ?? string.Empty);
            cmd.Parameters.AddWithValue("@p", string.IsNullOrWhiteSpace(projektname) ? (object)DBNull.Value : projektname);
            cmd.Parameters.AddWithValue("@now", now);
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public void UpdateNote(int noteId, string titel, string text, string projektname)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            UPDATE notes
               SET titel = @t,
                   text = @x,
                   projekt_id = (SELECT id FROM projects WHERE projektname = @p),
                   updated_utc = @now
             WHERE id = @id;";
            cmd.Parameters.AddWithValue("@t", titel ?? string.Empty);
            cmd.Parameters.AddWithValue("@x", text ?? string.Empty);
            cmd.Parameters.AddWithValue("@p", string.IsNullOrWhiteSpace(projektname) ? (object)DBNull.Value : projektname);
            cmd.Parameters.AddWithValue("@now", UtcNow());
            cmd.Parameters.AddWithValue("@id", noteId);
            cmd.ExecuteNonQuery();
        }
    }

    public void DeleteNote(int noteId)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM notes WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", noteId);
            cmd.ExecuteNonQuery();
        }
    }

    public class TodoColumnRow
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int Position { get; set; }
    }

    public class TodoTicketRow
    {
        public int Id { get; set; }
        public int ColumnId { get; set; }
        public string Titel { get; set; }
        public string Beschreibung { get; set; }
        public int Position { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public class NoteRow
    {
        public int Id { get; set; }
        public string Titel { get; set; }
        public string Text { get; set; }
        /// <summary>Das zugeordnete Projekt oder <c>null</c>.</summary>
        public string Projektname { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }
}
