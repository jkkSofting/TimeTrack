// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SQLite;

public sealed class TimeTracker : IDisposable
{
    private readonly string _dbPath;
    private readonly SQLiteConnection _conn;
    private bool _disposed;

    public TimeTracker(string dbFilePath = "timetracker.db")
    {
        if (string.IsNullOrWhiteSpace(dbFilePath))
            throw new ArgumentException("dbFilePath darf nicht leer sein.", nameof(dbFilePath));

        _dbPath = dbFilePath;
        var cs = new SQLiteConnectionStringBuilder
        {
            DataSource = _dbPath,
            ForeignKeys = true, // wichtig für ON DELETE CASCADE
            JournalMode = SQLiteJournalModeEnum.Wal
        }.ToString();

        _conn = new SQLiteConnection(cs);
        _conn.Open();
        EnsureSchema();
    }

    private void EnsureSchema()
    {
        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            cmd.CommandText =
            @"
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS projects(
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                projektname     TEXT    NOT NULL UNIQUE,
                kunde           TEXT    NOT NULL,
                anzahl_stunden  REAL    NOT NULL DEFAULT 0.0,
                kostentraeger   TEXT    NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_projects_name ON projects(projektname);

            CREATE TABLE IF NOT EXISTS time_entries(
                id           INTEGER PRIMARY KEY AUTOINCREMENT,
                datum        TEXT    NOT NULL, -- yyyy-MM-dd
                startzeit    TEXT    NOT NULL, -- HH:mm
                endzeit      TEXT    NOT NULL, -- HH:mm
                projekt_id   INTEGER NOT NULL,
                beschreibung TEXT,
                FOREIGN KEY(projekt_id) REFERENCES projects(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_time_entries_proj  ON time_entries(projekt_id);
            CREATE INDEX IF NOT EXISTS idx_time_entries_datum ON time_entries(datum);
            ";
            cmd.ExecuteNonQuery();

            var sumExpr = @"
                IFNULL(SUM(
                    (julianday(datetime(datum || ' ' || endzeit)) -
                     julianday(datetime(datum || ' ' || startzeit))) * 24.0
                ), 0.0)
            ";

            cmd.CommandText = $@"
            CREATE TRIGGER IF NOT EXISTS te_ai AFTER INSERT ON time_entries
            BEGIN
              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = NEW.projekt_id)
               WHERE id = NEW.projekt_id;
            END;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = $@"
            CREATE TRIGGER IF NOT EXISTS te_au AFTER UPDATE ON time_entries
            BEGIN
              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = NEW.projekt_id)
               WHERE id = NEW.projekt_id;

              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = OLD.projekt_id)
               WHERE id = OLD.projekt_id;
            END;";
            cmd.ExecuteNonQuery();

            cmd.CommandText = $@"
            CREATE TRIGGER IF NOT EXISTS te_ad AFTER DELETE ON time_entries
            BEGIN
              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = OLD.projekt_id)
               WHERE id = OLD.projekt_id;
            END;";
            cmd.ExecuteNonQuery();

            tx.Commit();
        }
    }

    // ---------- Projekte ----------

    public void AddOrEnsureProject(string projektname, string kunde, string kostentraeger)
    {
        if (string.IsNullOrWhiteSpace(projektname)) throw new ArgumentException("leer", nameof(projektname));
        if (string.IsNullOrWhiteSpace(kunde)) throw new ArgumentException("leer", nameof(kunde));
        if (string.IsNullOrWhiteSpace(kostentraeger)) throw new ArgumentException("leer", nameof(kostentraeger));

        using (var cmd = _conn.CreateCommand())
        {
            // SQLite ab 3.24.0: UPSERT via ON CONFLICT .. DO UPDATE
            cmd.CommandText =
            @"
            INSERT INTO projects (projektname, kunde, kostentraeger)
                 VALUES (@name, @kunde, @kost)
            ON CONFLICT(projektname) DO UPDATE SET
                 kunde = excluded.kunde,
                 kostentraeger = excluded.kostentraeger
            ";
            cmd.Parameters.AddWithValue("@name", projektname);
            cmd.Parameters.AddWithValue("@kunde", kunde);
            cmd.Parameters.AddWithValue("@kost", kostentraeger);
            cmd.ExecuteNonQuery();
        }
    }

    public IEnumerable<ProjectRow> GetProjects()
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT projektname, kunde, anzahl_stunden, kostentraeger FROM projects ORDER BY projektname;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    yield return new ProjectRow
                    {
                        Projektname = r.GetString(0),
                        Kunde = r.GetString(1),
                        AnzahlStunden = r.GetDouble(2),
                        Kostentraeger = r.GetString(3)
                    };
                }
            }
        }
    }

    // ---------- Buchungen ----------

    public void AddTimeEntry(
        DateTime datumOhneZeit,   // nur Datumsteil
        string startHHmm,         // "HH:mm"
        string endHHmm,           // "HH:mm"
        string projektname,
        string beschreibung = null)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname fehlt.", nameof(projektname));

        EnsureTimeFormat(startHHmm);
        EnsureTimeFormat(endHHmm);

        var startSpan = ParseTime(startHHmm);
        var endSpan = ParseTime(endHHmm);
        if (endSpan <= startSpan)
            throw new ArgumentException("Endzeit muss nach Startzeit liegen. Nicht diskutierbar.");

        var dateStr = datumOhneZeit.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        int projectId = GetProjectIdByName(projektname);
        if (projectId <= 0)
            throw new InvalidOperationException($"Projekt '{projektname}' existiert nicht. Erst anlegen.");

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText =
            @"
            INSERT INTO time_entries (datum, startzeit, endzeit, projekt_id, beschreibung)
            VALUES (@d, @s, @e, @pid, @b);
            ";
            cmd.Parameters.AddWithValue("@d", dateStr);
            cmd.Parameters.AddWithValue("@s", startHHmm);
            cmd.Parameters.AddWithValue("@e", endHHmm);
            cmd.Parameters.AddWithValue("@pid", projectId);
            cmd.Parameters.AddWithValue("@b", (object)beschreibung ?? DBNull.Value);
            cmd.ExecuteNonQuery();

            tx.Commit(); // Trigger machen die Summen
        }
    }

    public IEnumerable<TimeEntryRow> GetTimeEntriesForProject(string projektname)
    {
        int projectId = GetProjectIdByName(projektname);
        if (projectId <= 0) yield break;

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText =
            @"
            SELECT datum, startzeit, endzeit, beschreibung
              FROM time_entries
             WHERE projekt_id = @pid
             ORDER BY datum, startzeit;
            ";
            cmd.Parameters.AddWithValue("@pid", projectId);

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    yield return new TimeEntryRow
                    {
                        Datum = DateTime.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Startzeit = r.GetString(1),
                        Endzeit = r.GetString(2),
                        Beschreibung = r.IsDBNull(3) ? null : r.GetString(3)
                    };
                }
            }
        }
    }

    // ---------- Helpers ----------

    private int GetProjectIdByName(string projektname)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM projects WHERE projektname = @n LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", projektname);
            var o = cmd.ExecuteScalar();
            if (o == null || o == DBNull.Value) return -1;
            return Convert.ToInt32(o, CultureInfo.InvariantCulture);
        }
    }

    private static void EnsureTimeFormat(string hhmm)
    {
        TimeSpan _;
        if (!TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out _))
            throw new ArgumentException($"Zeit muss HH:mm sein (bekommen: '{hhmm}').");
    }

    private static TimeSpan ParseTime(string hhmm)
    {
        return TimeSpan.ParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn?.Dispose();
    }

    // ---------- DTOs ----------

    public class ProjectRow
    {
        public string Projektname { get; set; }
        public string Kunde { get; set; }
        public double AnzahlStunden { get; set; }
        public string Kostentraeger { get; set; }
    }

    public class TimeEntryRow
    {
        public DateTime Datum { get; set; }   // nur Datum
        public string Startzeit { get; set; } // HH:mm
        public string Endzeit { get; set; }   // HH:mm
        public string Beschreibung { get; set; }
    }
}
