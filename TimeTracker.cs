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
using Zeitmanagement.Helpers;

public sealed partial class TimeTracker : IDisposable
{
    private readonly string _dbPath;
    private readonly SQLiteConnection _conn;
    private bool _disposed;

    public TimeTracker(string dbFilePath = BackupHelper.DbFilePath)
    {
        if (string.IsNullOrWhiteSpace(dbFilePath))
            throw new ArgumentException("dbFilePath darf nicht leer sein.", nameof(dbFilePath));

        _dbPath = dbFilePath;

        // Ordner prüfen / erstellen
        var directory = Path.GetDirectoryName(_dbPath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Falls Datei fehlt: anlegen (SQLite macht das beim Open auch, aber so ist es klarer)
        if (!File.Exists(_dbPath))
        {
            SQLiteConnection.CreateFile(_dbPath);
        }

        // Connection String
        var cs = new SQLiteConnectionStringBuilder
        {
            DataSource = _dbPath,
            ForeignKeys = true, // wichtig für ON DELETE CASCADE
            JournalMode = SQLiteJournalModeEnum.Wal
        }.ToString();

        // Verbindung aufbauen
        _conn = new SQLiteConnection(cs);
        _conn.Open();

        // sicherstellen, dass Tabellen da sind
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
                kostentraeger   TEXT    NOT NULL,
                ist_extern      INTEGER NOT NULL DEFAULT 0, -- 1 = vom Kunden bezahlt
                rundung_minuten INTEGER NOT NULL DEFAULT 0  -- SAP-Aufrundung je Buchung: 0 = aus, 5, 10, 15, 60
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

            // Migration: Datenbanken vor der SAP-Verrechnung haben noch keine Spalte ist_extern
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'ist_extern';";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                cmd.CommandText = "ALTER TABLE projects ADD COLUMN ist_extern INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }

            // Migration: Datenbanken vor "alle Projekte im Quick Select" haben noch keine Spalte quickselect_aktiv.
            // Bestehende Projekte bleiben dadurch (DEFAULT 1) im Quick Select wählbar.
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'quickselect_aktiv';";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                cmd.CommandText = "ALTER TABLE projects ADD COLUMN quickselect_aktiv INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
            }

            // Migration: Datenbanken vor der Rundung je Projekt haben noch keine Spalte rundung_minuten (0 = aus).
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'rundung_minuten';";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                cmd.CommandText = "ALTER TABLE projects ADD COLUMN rundung_minuten INTEGER NOT NULL DEFAULT 0;";
                cmd.ExecuteNonQuery();
            }

            // Migration: Datenbanken vor der globalen Standardrundung haben noch keine Spalte rundung_global (1 = erbt die globale Rundung).
            // Nur beim Anlegen der Spalte (idempotent): Projekte mit eigener Rundung behalten diese (rundung_global = 0),
            // Projekte ohne Rundung erben künftig die globale Standardrundung.
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'rundung_global';";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                cmd.CommandText = "ALTER TABLE projects ADD COLUMN rundung_global INTEGER NOT NULL DEFAULT 1;";
                cmd.ExecuteNonQuery();
                cmd.CommandText = "UPDATE projects SET rundung_global = 0 WHERE rundung_minuten > 0;";
                cmd.ExecuteNonQuery();
            }

            // Migration: Papierkorb (Soft-Delete). geloescht_am = ISO-Zeitstempel (NULL = nicht gelöscht).
            // Ein Projekt im Papierkorb behält seine Buchungen unmarkiert; nur einzeln gelöschte Buchungen tragen einen eigenen Marker.
            bool trashColumnAdded = false;
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('projects') WHERE name = 'geloescht_am';";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                cmd.CommandText = "ALTER TABLE projects ADD COLUMN geloescht_am TEXT NULL;";
                cmd.ExecuteNonQuery();
                trashColumnAdded = true;
            }
            cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('time_entries') WHERE name = 'geloescht_am';";
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
            {
                cmd.CommandText = "ALTER TABLE time_entries ADD COLUMN geloescht_am TEXT NULL;";
                cmd.ExecuteNonQuery();
                trashColumnAdded = true;
            }

            // Summen-Trigger: Buchungen im Papierkorb zählen nicht zu anzahl_stunden. Immer neu angelegt (idempotent).
            var sumExpr = @"
                IFNULL(SUM(
                    (julianday(datetime(datum || ' ' || endzeit)) -
                     julianday(datetime(datum || ' ' || startzeit))) * 24.0
                ), 0.0)
            ";
            cmd.CommandText = $@"
            DROP TRIGGER IF EXISTS te_ai;
            DROP TRIGGER IF EXISTS te_au;
            DROP TRIGGER IF EXISTS te_ad;

            CREATE TRIGGER te_ai AFTER INSERT ON time_entries
            BEGIN
              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = NEW.projekt_id AND geloescht_am IS NULL)
               WHERE id = NEW.projekt_id;
            END;

            CREATE TRIGGER te_au AFTER UPDATE ON time_entries
            BEGIN
              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = NEW.projekt_id AND geloescht_am IS NULL)
               WHERE id = NEW.projekt_id;

              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = OLD.projekt_id AND geloescht_am IS NULL)
               WHERE id = OLD.projekt_id;
            END;

            CREATE TRIGGER te_ad AFTER DELETE ON time_entries
            BEGIN
              UPDATE projects
                 SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = OLD.projekt_id AND geloescht_am IS NULL)
               WHERE id = OLD.projekt_id;
            END;";
            cmd.ExecuteNonQuery();

            // Einmalige Neuberechnung der Summen beim Einführen der Spalten.
            if (trashColumnAdded)
            {
                cmd.CommandText = $@"
                UPDATE projects
                   SET anzahl_stunden = (SELECT {sumExpr} FROM time_entries WHERE projekt_id = projects.id AND geloescht_am IS NULL);";
                cmd.ExecuteNonQuery();
            }

            // Programmweite Einstellungen als Schlüssel/Wert (z. B. die globale Standardrundung).
            cmd.CommandText = "CREATE TABLE IF NOT EXISTS app_settings(key TEXT PRIMARY KEY, value TEXT);";
            cmd.ExecuteNonQuery();

            // Projektcontrolling-Import (SAP-Excel): zusätzliche Tabellen, die Zeiterfassung bleibt unberührt.
            EnsureControllingSchema(cmd);

            // Angaben für den Tätigkeitsnachweis (Auftragsnummer, Auftraggeber): eine zusätzliche Tabelle je Projekt.
            EnsureExportSchema(cmd);

            // Ablage der Plugins (Zuordnungen, Übertragungsstände, verschlüsselte Zugangsdaten): eine zusätzliche Tabelle.
            EnsurePluginSchema(cmd);

            // To-Do-Board und Projektnotizen: zusätzliche Tabellen, die Zeiterfassung bleibt unberührt.
            EnsureTodoSchema(cmd);

            tx.Commit();
        }
    }

    // ---------- Globale Einstellungen ----------

    private const string GlobalRoundingKey = "rundung_global_minuten";

    /// <summary>
    /// Globale Standardrundung in Minuten (0 = aus), die alle Projekte mit <c>rundung_global = 1</c> erben.
    /// Fehlt der Eintrag, gilt <see cref="RoundingOptions.DefaultGlobalMinutes"/>; der Wert ist immer ein erlaubter Wert.
    /// </summary>
    public int GetGlobalRoundingMinutes()
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT value FROM app_settings WHERE key = @k;";
            cmd.Parameters.AddWithValue("@k", GlobalRoundingKey);
            var o = cmd.ExecuteScalar();
            if (o == null || o == DBNull.Value) return RoundingOptions.DefaultGlobalMinutes;

            int minutes;
            if (!int.TryParse(Convert.ToString(o, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes))
                return RoundingOptions.DefaultGlobalMinutes;
            return RoundingOptions.Normalize(minutes);
        }
    }

    /// <summary>Speichert die globale Standardrundung (nicht erlaubte Werte werden zu 0 = aus normalisiert).</summary>
    public void SetGlobalRoundingMinutes(int minutes)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText =
            @"INSERT INTO app_settings (key, value) VALUES (@k, @v)
              ON CONFLICT(key) DO UPDATE SET value = excluded.value;";
            cmd.Parameters.AddWithValue("@k", GlobalRoundingKey);
            cmd.Parameters.AddWithValue("@v", RoundingOptions.Normalize(minutes).ToString(CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
    }

    // ---------- Projekte ----------

    /// <summary>
    /// Legt ein Projekt an oder aktualisiert es (UPSERT; die Rundung wird dabei immer mitgeschrieben).
    /// Rundung: <paramref name="rundungGlobal"/> true = erbt die globale Standardrundung, false = eigener Wert
    /// (<paramref name="rundungMinuten"/>, 0 = für das Projekt aus). Ohne Angabe (null) gilt aus Gründen der
    /// Rückwärtskompatibilität: <paramref name="rundungMinuten"/> &gt; 0 = eigener Wert, sonst erbt das Projekt die globale Rundung.
    /// </summary>
    public void AddOrEnsureProject(string projektname, string kunde, string kostentraeger, bool istExtern = false, int rundungMinuten = 0, bool? rundungGlobal = null)
    {
        if (string.IsNullOrWhiteSpace(projektname)) throw new ArgumentException("leer", nameof(projektname));
        if (string.IsNullOrWhiteSpace(kunde)) throw new ArgumentException("leer", nameof(kunde));
        if (string.IsNullOrWhiteSpace(kostentraeger)) throw new ArgumentException("leer", nameof(kostentraeger));

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // Ein gleichnamiges Papierkorb-Projekt darf nie stillschweigend wiederbelebt/überschrieben werden: erst umbenennen.
            RenameTrashedProjectWithName(cmd, projektname, " (gelöscht)");

            cmd.Parameters.Clear();
            // SQLite ab 3.24.0: UPSERT via ON CONFLICT .. DO UPDATE
            cmd.CommandText =
            @"
            INSERT INTO projects (projektname, kunde, kostentraeger, ist_extern, rundung_minuten, rundung_global)
                 VALUES (@name, @kunde, @kost, @ext, @rund, @glob)
            ON CONFLICT(projektname) DO UPDATE SET
                 kunde = excluded.kunde,
                 kostentraeger = excluded.kostentraeger,
                 ist_extern = excluded.ist_extern,
                 rundung_minuten = excluded.rundung_minuten,
                 rundung_global = excluded.rundung_global
            ";
            int own = RoundingOptions.Normalize(rundungMinuten);
            bool useGlobal = rundungGlobal ?? own <= 0;
            cmd.Parameters.AddWithValue("@name", projektname);
            cmd.Parameters.AddWithValue("@kunde", kunde);
            cmd.Parameters.AddWithValue("@kost", kostentraeger);
            cmd.Parameters.AddWithValue("@ext", istExtern ? 1 : 0);
            cmd.Parameters.AddWithValue("@rund", own);
            cmd.Parameters.AddWithValue("@glob", useGlobal ? 1 : 0);
            cmd.ExecuteNonQuery();
            tx.Commit();
        }
    }

    /// <summary>ENDGÜLTIGES Löschen eines Projekts samt Buchungen (egal ob im Papierkorb oder nicht). Für den Papierkorb siehe <see cref="MoveProjectToTrash"/>.</summary>
    public void DeleteProject(string projektname)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname darf nicht leer sein.", nameof(projektname));

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // Projekt-ID holen (und prüfen, ob es existiert)
            cmd.CommandText = "SELECT id FROM projects WHERE projektname = @name LIMIT 1;";
            cmd.Parameters.AddWithValue("@name", projektname);
            var o = cmd.ExecuteScalar();

            if (o == null || o == DBNull.Value)
            {
                // Nix zu löschen. Wir tun so, als wär das Absicht.
                tx.Rollback();
                return;
            }

            int id = Convert.ToInt32(o, CultureInfo.InvariantCulture);

            // Löschen (time_entries gehen dank ON DELETE CASCADE automatisch mit weg)
            cmd.Parameters.Clear();
            cmd.CommandText = "DELETE FROM projects WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            tx.Commit();
        }
    }


    public IEnumerable<ProjectRow> GetProjects()
    {
        // Globalwert VOR dem Reader lesen (einmal pro Aufruf, nicht je Zeile).
        int globalRounding = GetGlobalRoundingMinutes();

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT projektname, kunde, anzahl_stunden, kostentraeger, ist_extern, quickselect_aktiv, rundung_minuten, rundung_global FROM projects WHERE geloescht_am IS NULL ORDER BY projektname;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    int own = RoundingOptions.Normalize(r.GetInt32(6));
                    bool useGlobal = r.GetInt32(7) != 0;
                    yield return new ProjectRow
                    {
                        Projektname = r.GetString(0),
                        Kunde = r.GetString(1),
                        AnzahlStunden = r.GetDouble(2),
                        Kostentraeger = r.GetString(3),
                        IstExtern = r.GetInt32(4) != 0,
                        QuickSelectAktiv = r.GetInt32(5) != 0,
                        RundungEigenMinuten = own,
                        RundungGlobal = useGlobal,
                        RundungMinuten = RoundingOptions.Resolve(useGlobal, own, globalRounding)
                    };
                }
            }
        }
    }

    /// <summary>
    /// Legt fest, ob ein Projekt im Quick-Switch-Fenster wählbar ist.
    /// Buchungen auf das Projekt bleiben davon unberührt.
    /// </summary>
    public void SetProjectQuickSelectActive(string projektname, bool aktiv)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname darf nicht leer sein.", nameof(projektname));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE projects SET quickselect_aktiv = @aktiv WHERE projektname = @name AND geloescht_am IS NULL;";
            cmd.Parameters.AddWithValue("@aktiv", aktiv ? 1 : 0);
            cmd.Parameters.AddWithValue("@name", projektname);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Aktualisiert ein Projekt. Rundung: Werden weder <paramref name="rundungMinuten"/> noch <paramref name="rundungGlobal"/>
    /// übergeben (beide null), bleibt die bisherige Rundung des Projekts UNVERÄNDERT (bewusst konservativ; früher wurde sie auf 0 überschrieben,
    /// kein Test verlangt das). Sonst: <paramref name="rundungGlobal"/> true = erbt die globale Standardrundung, false = eigener Wert;
    /// ohne Angabe von <paramref name="rundungGlobal"/> gilt <paramref name="rundungMinuten"/> &gt; 0 = eigener Wert, 0 = erbt die globale Rundung.
    /// Wird nur <paramref name="rundungGlobal"/> übergeben, bleibt der gespeicherte eigene Wert erhalten.
    /// </summary>
    public void UpdateProject(string originalProjektname, string newProjektname, string kunde, string kostentraeger, bool istExtern = false, int? rundungMinuten = null, bool? rundungGlobal = null)
    {
        if (string.IsNullOrWhiteSpace(originalProjektname)) throw new ArgumentException("leer", nameof(originalProjektname));
        if (string.IsNullOrWhiteSpace(newProjektname)) throw new ArgumentException("leer", nameof(newProjektname));
        if (string.IsNullOrWhiteSpace(kunde)) throw new ArgumentException("leer", nameof(kunde));
        if (string.IsNullOrWhiteSpace(kostentraeger)) throw new ArgumentException("leer", nameof(kostentraeger));

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // Sicherstellen, dass das Original existiert
            cmd.CommandText = "SELECT id FROM projects WHERE projektname = @orig AND geloescht_am IS NULL LIMIT 1;";
            cmd.Parameters.AddWithValue("@orig", originalProjektname);
            var o = cmd.ExecuteScalar();
            if (o == null || o == DBNull.Value)
            {
                tx.Rollback();
                throw new InvalidOperationException($"Projekt '{originalProjektname}' existiert nicht.");
            }

            // Ein Papierkorb-Projekt mit dem neuen Namen weicht aus (UNIQUE), statt die Umbenennung zu blockieren.
            if (!string.Equals(originalProjektname, newProjektname, StringComparison.Ordinal))
            {
                cmd.Parameters.Clear();
                RenameTrashedProjectWithName(cmd, newProjektname, " (gelöscht)");
            }

            // Optional: prüfen, ob der neue Name schon existiert (und nicht derselbe Datensatz ist)
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT id FROM projects WHERE projektname = @new LIMIT 1;";
            cmd.Parameters.AddWithValue("@new", newProjektname);
            var existing = cmd.ExecuteScalar();

            // Wenn ein anderer Datensatz den neuen Namen hat -> sauberer Fehler
            var originalId = Convert.ToInt32(o, CultureInfo.InvariantCulture);
            if (existing != null && existing != DBNull.Value && Convert.ToInt32(existing, CultureInfo.InvariantCulture) != originalId)
            {
                tx.Rollback();
                throw new InvalidOperationException($"Projektname '{newProjektname}' ist bereits vergeben.");
            }

            // Rundung bestimmen (siehe XML-Kommentar): ohne Angabe die vorhandenen Werte behalten
            int own;
            bool useGlobal;
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT rundung_minuten, rundung_global FROM projects WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", originalId);
            using (var r = cmd.ExecuteReader())
            {
                r.Read();
                own = RoundingOptions.Normalize(r.GetInt32(0));
                useGlobal = r.GetInt32(1) != 0;
            }
            if (rundungMinuten.HasValue || rundungGlobal.HasValue)
            {
                if (rundungMinuten.HasValue) own = RoundingOptions.Normalize(rundungMinuten.Value);
                useGlobal = rundungGlobal ?? (own <= 0);
            }

            // Update
            cmd.Parameters.Clear();
            cmd.CommandText = @"
            UPDATE projects
               SET projektname = @pname,
                   kunde = @kunde,
                   kostentraeger = @kost,
                   ist_extern = @ext,
                   rundung_minuten = @rund,
                   rundung_global = @glob
             WHERE id = @id;";
            cmd.Parameters.AddWithValue("@pname", newProjektname);
            cmd.Parameters.AddWithValue("@kunde", kunde);
            cmd.Parameters.AddWithValue("@kost", kostentraeger);
            cmd.Parameters.AddWithValue("@ext", istExtern ? 1 : 0);
            cmd.Parameters.AddWithValue("@rund", own);
            cmd.Parameters.AddWithValue("@glob", useGlobal ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", originalId);
            cmd.ExecuteNonQuery();

            tx.Commit();
        }
    }


    // ---------- Buchungen ----------

    private static readonly string[] TimeFormats = { @"h\:mm", @"hh\:mm" };

    private static TimeSpan ParseTime(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new ArgumentException("Zeitstring fehlt.", nameof(s));

        var input = s.Trim();

        if (!TimeSpan.TryParseExact(input, TimeFormats, CultureInfo.InvariantCulture, out var ts))
            throw new ArgumentException("Ungültiges Zeitformat. Erlaubt sind h:mm oder hh:mm.", nameof(s));

        if (ts < TimeSpan.Zero || ts >= TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(s), "Zeit muss zwischen 00:00 und 23:59 liegen.");

        return ts;
    }

    // Wenn du die alte EnsureTimeFormat behalten willst:
    private static void EnsureTimeFormat(string s) { _ = ParseTime(s); }

    public void AddTimeEntry(
        DateTime datumOhneZeit,   // nur Datumsteil
        string startHHmm,         // "h:mm" oder "hh:mm"
        string endHHmm,           // "h:mm" oder "hh:mm"
        string projektname,
        string beschreibung = null)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname fehlt.", nameof(projektname));

        // parse & validieren
        var startSpan = ParseTime(startHHmm);
        var endSpan = ParseTime(endHHmm);

        if (endSpan <= startSpan)
            throw new ArgumentException("Endzeit muss nach Startzeit liegen.");

        // für DB konsistent auf hh:mm normalisieren
        var startNorm = startSpan.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        var endNorm = endSpan.ToString(@"hh\:mm", CultureInfo.InvariantCulture);

        var dateStr = datumOhneZeit.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        int projectId = GetProjectIdByName(projektname);
        if (projectId <= 0)
            throw new InvalidOperationException($"Projekt '{projektname}' existiert nicht. Bitte zuerst anlegen.");

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
            cmd.Parameters.AddWithValue("@s", startNorm);
            cmd.Parameters.AddWithValue("@e", endNorm);
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
             WHERE projekt_id = @pid AND geloescht_am IS NULL
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

    public IEnumerable<TimeEntryRow> GetTimeEntriesForDate(DateTime date)
    {
        string d = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT te.id, te.datum, te.startzeit, te.endzeit, te.beschreibung, p.projektname
              FROM time_entries te
              JOIN projects p ON p.id = te.projekt_id
             WHERE te.datum = @d AND te.geloescht_am IS NULL AND p.geloescht_am IS NULL
             ORDER BY te.startzeit;";
            cmd.Parameters.AddWithValue("@d", d);

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    yield return new TimeEntryRow
                    {
                        Id = r.GetInt32(0),
                        Datum = DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Startzeit = r.GetString(2),
                        Endzeit = r.GetString(3),
                        Beschreibung = r.IsDBNull(4) ? null : r.GetString(4),
                        Projektname = r.GetString(5)
                    };
                }
            }
        }
    }

    /// <summary>
    /// Liefert alle Buchungen im Zeitraum [fromInclusive, toInclusive] inkl. Projektname und Intern/Extern-Kennzeichen,
    /// sortiert nach Datum und Startzeit.
    /// </summary>
    public List<TimeEntryRow> GetTimeEntriesBetween(DateTime fromInclusive, DateTime toInclusive)
    {
        var result = new List<TimeEntryRow>();
        int globalRounding = GetGlobalRoundingMinutes();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT te.id, te.datum, te.startzeit, te.endzeit, te.beschreibung, p.projektname, p.ist_extern, p.rundung_global, p.rundung_minuten
              FROM time_entries te
              JOIN projects p ON p.id = te.projekt_id
             WHERE te.datum >= @from AND te.datum <= @to AND te.geloescht_am IS NULL AND p.geloescht_am IS NULL
             ORDER BY te.datum, te.startzeit;";
            cmd.Parameters.AddWithValue("@from", fromInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", toInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new TimeEntryRow
                    {
                        Id = r.GetInt32(0),
                        Datum = DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Startzeit = r.GetString(2),
                        Endzeit = r.GetString(3),
                        Beschreibung = r.IsDBNull(4) ? null : r.GetString(4),
                        Projektname = r.GetString(5),
                        IstExtern = r.GetInt32(6) != 0,
                        RundungMinuten = RoundingOptions.Resolve(r.GetInt32(7) != 0, r.GetInt32(8), globalRounding)
                    });
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Liefert die pro Tag summierten Arbeitsstunden im Zeitraum [fromInclusive, toInclusive].
    /// Nur Tage mit Buchungen sind enthalten. Ein einziger, indexgestützter Query.
    /// </summary>
    public Dictionary<DateTime, double> GetDailyHours(DateTime fromInclusive, DateTime toInclusive)
    {
        var result = new Dictionary<DateTime, double>();

        string from = fromInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string to = toInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT datum,
                   SUM((julianday(datetime(datum || ' ' || endzeit)) -
                        julianday(datetime(datum || ' ' || startzeit))) * 24.0) AS stunden
              FROM time_entries
             WHERE datum >= @from AND datum <= @to
               AND geloescht_am IS NULL
               AND projekt_id IN (SELECT id FROM projects WHERE geloescht_am IS NULL)
             GROUP BY datum;";
            cmd.Parameters.AddWithValue("@from", from);
            cmd.Parameters.AddWithValue("@to", to);

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    if (r.IsDBNull(0) || r.IsDBNull(1)) continue;
                    var date = DateTime.ParseExact(r.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    result[date.Date] = r.GetDouble(1);
                }
            }
        }

        return result;
    }

    public void UpdateTimeEntry(int id, DateTime datumOhneZeit, string startHHmm, string endHHmm, string projektname, string beschreibung = null)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(projektname)) throw new ArgumentException("Projektname fehlt.", nameof(projektname));

        EnsureTimeFormat(startHHmm);
        EnsureTimeFormat(endHHmm);

        var startSpan = ParseTime(startHHmm);
        var endSpan = ParseTime(endHHmm);
        if (endSpan <= startSpan)
            throw new ArgumentException("Endzeit muss nach Startzeit liegen.");

        int projectId = GetProjectIdByName(projektname);
        if (projectId <= 0) throw new InvalidOperationException($"Projekt '{projektname}' existiert nicht.");

        string d = datumOhneZeit.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;

            // Alte Projekt-ID ermitteln, damit Trigger-Update auch für altes Projekt passiert (falls Projekt gewechselt)
            cmd.CommandText = "SELECT projekt_id FROM time_entries WHERE id = @id AND geloescht_am IS NULL;";
            cmd.Parameters.AddWithValue("@id", id);
            object oldPidObj = cmd.ExecuteScalar();
            if (oldPidObj == null || oldPidObj == DBNull.Value)
            {
                tx.Rollback();
                throw new InvalidOperationException($"TimeEntry mit Id={id} existiert nicht.");
            }
            int oldPid = Convert.ToInt32(oldPidObj, CultureInfo.InvariantCulture);

            // Update
            cmd.Parameters.Clear();
            cmd.CommandText = @"
            UPDATE time_entries
               SET datum = @d, startzeit = @s, endzeit = @e, projekt_id = @pid, beschreibung = @b
             WHERE id = @id;";
            cmd.Parameters.AddWithValue("@d", d);
            cmd.Parameters.AddWithValue("@s", startHHmm);
            cmd.Parameters.AddWithValue("@e", endHHmm);
            cmd.Parameters.AddWithValue("@pid", projectId);
            cmd.Parameters.AddWithValue("@b", (object)beschreibung ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            // Trigger te_au kümmert sich bereits um NEW/OLD Projekte.
            tx.Commit();
        }
    }

    /// <summary>ENDGÜLTIGES Löschen einer Buchung (egal ob im Papierkorb oder nicht). Für den Papierkorb siehe <see cref="MoveTimeEntryToTrash"/>.</summary>
    public void DeleteTimeEntry(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM time_entries WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
            tx.Commit(); // Trigger aktualisieren Summen
        }
    }


    // ---------- Papierkorb (Soft-Delete) ----------

    private const string TrashTimeFormat = "yyyy-MM-ddTHH:mm:ss";

    private static string TrashNow() => DateTime.Now.ToString(TrashTimeFormat, CultureInfo.InvariantCulture);

    private static DateTime ParseTrashTime(string s)
    {
        DateTime d;
        return DateTime.TryParseExact(s, TrashTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : DateTime.MinValue;
    }

    /// <summary>Liefert einen freien Projektnamen "name + suffix", bei Bedarf "name + (Zusatz 2)" usw. (projektname ist UNIQUE, Papierkorb zählt mit).</summary>
    private static string FindFreeProjectName(SQLiteCommand cmd, string name, string suffix)
    {
        // suffix z. B. " (gelöscht)" -> Zähler-Variante " (gelöscht 2)"
        string core = suffix.TrimEnd(')');
        for (int i = 1; ; i++)
        {
            string candidate = i == 1 ? name + suffix : name + core + " " + i.ToString(CultureInfo.InvariantCulture) + ")";
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT COUNT(*) FROM projects WHERE projektname = @n;";
            cmd.Parameters.AddWithValue("@n", candidate);
            if (Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) == 0) return candidate;
        }
    }

    /// <summary>Benennt ein Papierkorb-Projekt mit genau diesem Namen um (Platz machen für ein neues/umbenanntes/wiederhergestelltes Projekt). Läuft in der Transaktion des Aufrufers.</summary>
    private static void RenameTrashedProjectWithName(SQLiteCommand cmd, string name, string suffix)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT id FROM projects WHERE projektname = @n AND geloescht_am IS NOT NULL;";
        cmd.Parameters.AddWithValue("@n", name);
        var o = cmd.ExecuteScalar();
        if (o == null || o == DBNull.Value) return;

        int id = Convert.ToInt32(o, CultureInfo.InvariantCulture);
        string free = FindFreeProjectName(cmd, name, suffix);
        cmd.Parameters.Clear();
        cmd.CommandText = "UPDATE projects SET projektname = @new WHERE id = @id;";
        cmd.Parameters.AddWithValue("@new", free);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Legt ein Projekt in den Papierkorb (Soft-Delete). Seine Buchungen bleiben unverändert und verschwinden mit ihm aus
    /// allen Ansichten. Liefert false, wenn es kein aktives Projekt dieses Namens gibt.
    /// </summary>
    public bool MoveProjectToTrash(string projektname)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname darf nicht leer sein.", nameof(projektname));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE projects SET geloescht_am = @t WHERE projektname = @n AND geloescht_am IS NULL;";
            cmd.Parameters.AddWithValue("@t", TrashNow());
            cmd.Parameters.AddWithValue("@n", projektname);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>Legt eine Buchung in den Papierkorb (Soft-Delete); die Projektsumme wird per Trigger angepasst. Liefert false, wenn es keine aktive Buchung mit dieser Id gibt.</summary>
    public bool MoveTimeEntryToTrash(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE time_entries SET geloescht_am = @t WHERE id = @id AND geloescht_am IS NULL;";
            cmd.Parameters.AddWithValue("@t", TrashNow());
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>Anzahl der aktiven (nicht im Papierkorb liegenden) Buchungen eines aktiven Projekts.</summary>
    public int CountTimeEntries(string projektname)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname darf nicht leer sein.", nameof(projektname));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT COUNT(*) FROM time_entries te
              JOIN projects p ON p.id = te.projekt_id
             WHERE p.projektname = @n AND p.geloescht_am IS NULL AND te.geloescht_am IS NULL;";
            cmd.Parameters.AddWithValue("@n", projektname);
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Alle Projekte im Papierkorb, zuletzt gelöschte zuerst. Die Buchungsanzahl zählt nur Buchungen, die mit dem Projekt zurückkommen (nicht einzeln gelöschte).</summary>
    public List<TrashedProjectRow> GetTrashedProjects()
    {
        var result = new List<TrashedProjectRow>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT p.projektname, p.kunde, p.kostentraeger,
                   (SELECT COUNT(*) FROM time_entries te WHERE te.projekt_id = p.id AND te.geloescht_am IS NULL),
                   p.geloescht_am
              FROM projects p
             WHERE p.geloescht_am IS NOT NULL
             ORDER BY p.geloescht_am DESC, p.projektname;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new TrashedProjectRow
                    {
                        Projektname = r.GetString(0),
                        Kunde = r.GetString(1),
                        Kostentraeger = r.GetString(2),
                        AnzahlBuchungen = r.GetInt32(3),
                        GeloeschtAm = ParseTrashTime(r.GetString(4))
                    });
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Alle einzeln gelöschten Buchungen, zuletzt gelöschte zuerst. Buchungen eines Papierkorb-Projekts, die nicht
    /// einzeln gelöscht wurden, erscheinen hier NICHT (sie gehören zum Projekt, siehe <see cref="GetTrashedProjects"/>).
    /// </summary>
    public List<TrashedTimeEntryRow> GetTrashedTimeEntries()
    {
        var result = new List<TrashedTimeEntryRow>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT te.id, te.datum, te.startzeit, te.endzeit, te.beschreibung, p.projektname, te.geloescht_am, p.geloescht_am IS NOT NULL
              FROM time_entries te
              JOIN projects p ON p.id = te.projekt_id
             WHERE te.geloescht_am IS NOT NULL
             ORDER BY te.geloescht_am DESC, te.id DESC;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new TrashedTimeEntryRow
                    {
                        Id = r.GetInt32(0),
                        Datum = DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Startzeit = r.GetString(2),
                        Endzeit = r.GetString(3),
                        Beschreibung = r.IsDBNull(4) ? null : r.GetString(4),
                        Projektname = r.GetString(5),
                        GeloeschtAm = ParseTrashTime(r.GetString(6)),
                        ProjektGeloescht = r.GetInt32(7) != 0
                    });
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Stellt ein Projekt aus dem Papierkorb samt seinen nicht einzeln gelöschten Buchungen wieder her. Einzeln gelöschte
    /// Buchungen bleiben im Papierkorb. Ist der Name inzwischen von einem aktiven Projekt belegt, wird unter
    /// "Name (wiederhergestellt)" (bei Bedarf "Name (wiederhergestellt 2)" usw.) wiederhergestellt.
    /// Liefert den tatsächlichen Projektnamen, oder null, wenn kein solches Papierkorb-Projekt existiert.
    /// </summary>
    public string RestoreProject(string projektname)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname darf nicht leer sein.", nameof(projektname));

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            string result = RestoreProjectCore(cmd, projektname);
            tx.Commit();
            return result;
        }
    }

    private static string RestoreProjectCore(SQLiteCommand cmd, string projektname)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT id FROM projects WHERE projektname = @n AND geloescht_am IS NOT NULL;";
        cmd.Parameters.AddWithValue("@n", projektname);
        var o = cmd.ExecuteScalar();
        if (o == null || o == DBNull.Value) return null;
        int id = Convert.ToInt32(o, CultureInfo.InvariantCulture);

        // Name belegt? Ein aktives Projekt kann denselben Namen nur tragen, wenn das Papierkorb-Projekt umbenannt wurde
        // (dann ist der Name hier ohnehin ein anderer); projektname ist UNIQUE, also genügt: der Name gehört diesem Projekt.
        // Konflikt entsteht nur, wenn der "Originalname" bereits wieder vergeben ist – das Papierkorb-Projekt trägt dann
        // seinen Ausweichnamen. Ein Ausweichname " (gelöscht)" wird beim Wiederherstellen wieder entfernt, falls frei.
        string target = projektname;
        const string deletedSuffix = " (gelöscht)";
        if (projektname.EndsWith(deletedSuffix, StringComparison.Ordinal))
        {
            string baseName = projektname.Substring(0, projektname.Length - deletedSuffix.Length);
            if (baseName.Length > 0 && !ProjectNameExists(cmd, baseName)) target = baseName;
            else if (baseName.Length > 0) target = FindFreeProjectName(cmd, baseName, " (wiederhergestellt)");
        }

        cmd.Parameters.Clear();
        cmd.CommandText = "UPDATE projects SET projektname = @n, geloescht_am = NULL WHERE id = @id;";
        cmd.Parameters.AddWithValue("@n", target);
        cmd.Parameters.AddWithValue("@id", id);
        cmd.ExecuteNonQuery();
        return target;
    }

    private static bool ProjectNameExists(SQLiteCommand cmd, string name)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = "SELECT COUNT(*) FROM projects WHERE projektname = @n;";
        cmd.Parameters.AddWithValue("@n", name);
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Stellt eine einzeln gelöschte Buchung wieder her. Liegt das Projekt der Buchung selbst im Papierkorb, wird das
    /// Projekt MIT wiederhergestellt (inklusive seiner übrigen, nicht einzeln gelöschten Buchungen; Namenskonflikt wie bei
    /// <see cref="RestoreProject"/>), damit die Buchung nicht unsichtbar bleibt.
    /// Liefert den Namen des Projekts, zu dem die Buchung nun gehört, oder null, wenn die Buchung nicht im Papierkorb liegt.
    /// </summary>
    public string RestoreTimeEntry(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
            SELECT p.projektname, p.geloescht_am IS NOT NULL
              FROM time_entries te JOIN projects p ON p.id = te.projekt_id
             WHERE te.id = @id AND te.geloescht_am IS NOT NULL;";
            cmd.Parameters.AddWithValue("@id", id);
            string name;
            bool projectTrashed;
            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) { tx.Rollback(); return null; }
                name = r.GetString(0);
                projectTrashed = r.GetInt32(1) != 0;
            }

            if (projectTrashed) name = RestoreProjectCore(cmd, name);

            cmd.Parameters.Clear();
            cmd.CommandText = "UPDATE time_entries SET geloescht_am = NULL WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();

            tx.Commit();
            return name;
        }
    }

    /// <summary>Löscht ein Projekt aus dem Papierkorb endgültig (samt allen Buchungen). Aktive Projekte werden nicht angefasst. Liefert false, wenn nichts gelöscht wurde.</summary>
    public bool PurgeProject(string projektname)
    {
        if (string.IsNullOrWhiteSpace(projektname))
            throw new ArgumentException("Projektname darf nicht leer sein.", nameof(projektname));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM projects WHERE projektname = @n AND geloescht_am IS NOT NULL;";
            cmd.Parameters.AddWithValue("@n", projektname);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>Löscht eine einzeln gelöschte Buchung endgültig. Aktive Buchungen werden nicht angefasst. Liefert false, wenn nichts gelöscht wurde.</summary>
    public bool PurgeTimeEntry(int id)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM time_entries WHERE id = @id AND geloescht_am IS NOT NULL;";
            cmd.Parameters.AddWithValue("@id", id);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>Leert den Papierkorb endgültig (Projekte samt Buchungen und einzeln gelöschte Buchungen). Liefert die Anzahl endgültig gelöschter Projekte plus Buchungen.</summary>
    public int EmptyTrash()
    {
        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            int n = 0;
            cmd.CommandText = "DELETE FROM time_entries WHERE geloescht_am IS NOT NULL;";
            n += cmd.ExecuteNonQuery();
            cmd.CommandText = "DELETE FROM projects WHERE geloescht_am IS NOT NULL;";
            n += cmd.ExecuteNonQuery();
            tx.Commit();
            return n;
        }
    }

    // ---------- Helpers ----------

    private int GetProjectIdByName(string projektname)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id FROM projects WHERE projektname = @n AND geloescht_am IS NULL LIMIT 1;";
            cmd.Parameters.AddWithValue("@n", projektname);
            var o = cmd.ExecuteScalar();
            if (o == null || o == DBNull.Value) return -1;
            return Convert.ToInt32(o, CultureInfo.InvariantCulture);
        }
    }

    //private static void EnsureTimeFormat(string hhmm)
    //{
    //    TimeSpan _;
    //    if (!TimeSpan.TryParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture, out _))
    //        throw new ArgumentException($"Zeit muss HH:mm sein (bekommen: '{hhmm}').");
    //}

    //private static TimeSpan ParseTime(string hhmm)
    //{
    //    return TimeSpan.ParseExact(hhmm, @"hh\:mm", CultureInfo.InvariantCulture);
    //}

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _conn?.Dispose();
    }

    /// <summary>
    /// Flushes all data still sitting in the WAL file into the main database file,
    /// so a plain file copy of the database is complete and consistent.
    /// </summary>
    public void Checkpoint()
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "PRAGMA wal_checkpoint(FULL);";
            cmd.ExecuteNonQuery();
        }
    }

    // ---------- DTOs ----------

    public class ProjectRow
    {
        public string Projektname { get; set; }
        public string Kunde { get; set; }
        public double AnzahlStunden { get; set; }
        public string Kostentraeger { get; set; }
        public bool IstExtern { get; set; }     // vom Kunden bezahlt
        public bool QuickSelectAktiv { get; set; } // im Quick Switch wählbar
        /// <summary>EFFEKTIVE SAP-Aufrundung je Buchung (bereits aufgelöst, 0 = aus, 5, 10, 15, 60).</summary>
        public int RundungMinuten { get; set; }
        /// <summary>Eigener, roher Rundungswert des Projekts (0 = aus); gilt nur, wenn <see cref="RundungGlobal"/> false ist.</summary>
        public int RundungEigenMinuten { get; set; }
        /// <summary>true = das Projekt erbt die globale Standardrundung.</summary>
        public bool RundungGlobal { get; set; }
    }

    /// <summary>Ein Projekt im Papierkorb.</summary>
    public class TrashedProjectRow
    {
        public string Projektname { get; set; }
        public string Kunde { get; set; }
        public string Kostentraeger { get; set; }
        /// <summary>Anzahl der Buchungen, die mit dem Projekt zurückkommen (ohne einzeln gelöschte).</summary>
        public int AnzahlBuchungen { get; set; }
        public DateTime GeloeschtAm { get; set; }
    }

    /// <summary>Eine einzeln gelöschte Buchung im Papierkorb.</summary>
    public class TrashedTimeEntryRow
    {
        public int Id { get; set; }
        public DateTime Datum { get; set; }
        public string Startzeit { get; set; }
        public string Endzeit { get; set; }
        public string Beschreibung { get; set; }
        public string Projektname { get; set; }
        public DateTime GeloeschtAm { get; set; }
        /// <summary>true = das Projekt der Buchung liegt selbst im Papierkorb.</summary>
        public bool ProjektGeloescht { get; set; }
    }

    public class TimeEntryRow
    {
        public int Id { get; set; }            // <— NEU
        public DateTime Datum { get; set; }
        public string Startzeit { get; set; }
        public string Endzeit { get; set; }
        public string Beschreibung { get; set; }
        public string Projektname { get; set; } // <— NEU (für Anzeige)
        public bool IstExtern { get; set; }     // nur von GetTimeEntriesBetween gefüllt
        public int RundungMinuten { get; set; } // effektive Rundung des Projekts (0 = aus); nur von GetTimeEntriesBetween gefüllt
    }

}
