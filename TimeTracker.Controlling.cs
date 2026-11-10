// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.Linq;
using Zeitmanagement.Helpers;

// Speicher für den Projektcontrolling-Import (SAP-Excel): Importe, PSP-Elemente (Kostenträger),
// Beträge je Quellstand und Projektkontext. Alles liegt in derselben Datenbank wie die Zeiterfassung,
// damit Sicherungen die Importhistorie mitnehmen. Die Zeiterfassungstabellen und ihre Trigger bleiben unberührt.
public sealed partial class TimeTracker
{
    /// <summary>
    /// Legt die Tabellen des Controlling-Imports an (idempotent, nur additiv).
    /// Läuft innerhalb der Transaktion von <see cref="EnsureSchema"/>.
    /// </summary>
    private static void EnsureControllingSchema(SQLiteCommand cmd)
    {
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS controlling_imports(
            id                INTEGER PRIMARY KEY AUTOINCREMENT,
            file_name         TEXT    NOT NULL,                 -- nur der Dateiname, kein Pfad
            file_hash         TEXT    NOT NULL,                 -- SHA-256 der ausgewerteten Bytes
            source_timestamp  TEXT    NOT NULL,                 -- Stand der SAP-Daten, lokale Quellzeit ohne Zeitzone
            imported_utc      TEXT    NOT NULL,                 -- Zeitpunkt des Imports in UTC
            timestamp_origin  TEXT    NOT NULL,                 -- 'workbook' | 'user'
            currency_code     TEXT    NULL,                     -- NULL = nicht angegeben
            currency_context  TEXT    NOT NULL,                 -- 'UNSPECIFIED' oder Währungscode
            parser_version    INTEGER NOT NULL,
            allowlist_hash    TEXT    NOT NULL,
            payload_hash      TEXT    NOT NULL,
            fingerprint       TEXT    NOT NULL UNIQUE,
            scanned_count     INTEGER NOT NULL DEFAULT 0,
            matched_count     INTEGER NOT NULL,
            ignored_count     INTEGER NOT NULL,
            warning_count     INTEGER NOT NULL,
            warnings          TEXT    NULL
        );
        CREATE INDEX IF NOT EXISTS idx_controlling_imports_source ON controlling_imports(source_timestamp);

        -- Ein PSP-Element (= Kostenträger im SAP). Bewusst ohne Verweis auf lokale Projekte: Die Zuordnung ergibt sich
        -- jeweils aus dem aktuellen Kostenträger eines Projekts und ändert alte Beobachtungen nie.
        CREATE TABLE IF NOT EXISTS controlling_carriers(
            id           INTEGER PRIMARY KEY AUTOINCREMENT,
            carrier_key  TEXT NOT NULL UNIQUE,                  -- normalisiert
            display_text TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS controlling_project_context(
            id                  INTEGER PRIMARY KEY AUTOINCREMENT,
            import_id           INTEGER NOT NULL REFERENCES controlling_imports(id) ON DELETE CASCADE,
            parent_key          TEXT    NOT NULL,               -- normalisierte externe Projekt-ID
            external_parent_id  TEXT    NOT NULL,
            parent_name         TEXT    NULL,
            customer            TEXT    NULL,
            manager             TEXT    NULL,
            status              TEXT    NULL,
            cost_center         TEXT    NULL,
            planned_start       TEXT    NULL,                   -- yyyy-MM-dd (Planungsdatum, keine Buchung)
            planned_end         TEXT    NULL,
            order_value_total   TEXT    NULL,                   -- Beträge/Anteile als exakter Dezimaltext
            invoiced_total      TEXT    NULL,
            open_total          TEXT    NULL,
            invoiced_ratio      TEXT    NULL,
            progress_ratio      TEXT    NULL,
            wip_open            TEXT    NULL,
            order_value_year    TEXT    NULL,
            open_year           TEXT    NULL,
            UNIQUE(import_id, parent_key)
        );

        CREATE TABLE IF NOT EXISTS controlling_snapshots(
            id                  INTEGER PRIMARY KEY AUTOINCREMENT,
            import_id           INTEGER NOT NULL REFERENCES controlling_imports(id) ON DELETE CASCADE,
            carrier_id          INTEGER NOT NULL REFERENCES controlling_carriers(id),
            source_sheet        TEXT    NOT NULL,
            source_row          INTEGER NOT NULL,
            external_parent_id  TEXT    NULL,
            parent_key          TEXT    NULL,                   -- verweist auf controlling_project_context(import_id, parent_key)
            parent_name         TEXT    NULL,
            psp_name            TEXT    NULL,
            profit_center       TEXT    NULL,
            planned_cost        TEXT    NULL,                   -- NULL = nicht geliefert (nicht 0)
            actual_cost         TEXT    NULL,
            UNIQUE(import_id, carrier_id)
        );
        CREATE INDEX IF NOT EXISTS idx_controlling_snapshots_carrier ON controlling_snapshots(carrier_id);
        CREATE INDEX IF NOT EXISTS idx_controlling_snapshots_import  ON controlling_snapshots(import_id);
        ";
        cmd.ExecuteNonQuery();
    }

    // ---------- Speichern ----------

    /// <summary>
    /// Speichert einen bestätigten Import als neue, unveränderliche Charge in einer einzigen Transaktion.
    /// Ein identischer Import (gleicher Fingerabdruck) wird nicht noch einmal gespeichert.
    /// Bei jedem Fehler bleibt nichts zurück.
    /// </summary>
    public ControllingSaveResult SaveControllingImport(ControllingImportDraft draft)
    {
        if (draft == null) throw new ArgumentNullException(nameof(draft));
        if (string.IsNullOrWhiteSpace(draft.FileName)) throw new ArgumentException("Dateiname fehlt.", nameof(draft));
        if (draft.Rows.Count == 0) throw new ArgumentException("Der Import enthält keine Zeilen.", nameof(draft));

        var fingerprint = draft.Fingerprint();

        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            try
            {
                cmd.CommandText = "SELECT id FROM controlling_imports WHERE fingerprint = @f;";
                cmd.Parameters.AddWithValue("@f", fingerprint);
                var existing = cmd.ExecuteScalar();
                if (existing != null && existing != DBNull.Value)
                {
                    tx.Rollback();
                    return new ControllingSaveResult
                    {
                        Status = ControllingSaveStatus.AlreadyImported,
                        ImportId = Convert.ToInt32(existing, CultureInfo.InvariantCulture)
                    };
                }

                cmd.Parameters.Clear();
                cmd.CommandText = @"
                INSERT INTO controlling_imports
                    (file_name, file_hash, source_timestamp, imported_utc, timestamp_origin, currency_code, currency_context,
                     parser_version, allowlist_hash, payload_hash, fingerprint, scanned_count, matched_count, ignored_count,
                     warning_count, warnings)
                VALUES
                    (@name, @hash, @src, @utc, @origin, @cur, @ctx,
                     @parser, @allow, @payload, @fp, @scanned, @matched, @ignored,
                     @warnc, @warn);
                SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@name", System.IO.Path.GetFileName(draft.FileName));
                cmd.Parameters.AddWithValue("@hash", draft.FileHash ?? string.Empty);
                cmd.Parameters.AddWithValue("@src", draft.SourceTimestamp.ToString(CostMath.TimestampFormat, CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@utc", DateTime.UtcNow.ToString(CostMath.UtcFormat, CultureInfo.InvariantCulture));
                cmd.Parameters.AddWithValue("@origin", draft.TimestampOrigin == TimestampOrigin.User ? "user" : "workbook");
                cmd.Parameters.AddWithValue("@cur", (object)draft.CurrencyCode ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@ctx", draft.CurrencyContext);
                cmd.Parameters.AddWithValue("@parser", draft.ParserVersion);
                cmd.Parameters.AddWithValue("@allow", draft.AllowlistHash ?? string.Empty);
                cmd.Parameters.AddWithValue("@payload", draft.PayloadHash ?? string.Empty);
                cmd.Parameters.AddWithValue("@fp", fingerprint);
                cmd.Parameters.AddWithValue("@scanned", draft.ScannedCount);
                cmd.Parameters.AddWithValue("@matched", draft.Rows.Count);
                cmd.Parameters.AddWithValue("@ignored", draft.IgnoredCount);
                cmd.Parameters.AddWithValue("@warnc", draft.WarningCount);
                cmd.Parameters.AddWithValue("@warn", draft.Warnings.Count == 0 ? (object)DBNull.Value : string.Join("\n", draft.Warnings));
                var importId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);

                foreach (var parent in draft.Parents)
                    InsertControllingParent(cmd, importId, parent);

                foreach (var row in draft.Rows)
                {
                    cmd.Parameters.Clear();
                    cmd.CommandText = "INSERT OR IGNORE INTO controlling_carriers (carrier_key, display_text) VALUES (@key, @text);";
                    cmd.Parameters.AddWithValue("@key", row.CarrierKey);
                    cmd.Parameters.AddWithValue("@text", row.CarrierText ?? row.CarrierKey);
                    cmd.ExecuteNonQuery();

                    cmd.Parameters.Clear();
                    cmd.CommandText = "SELECT id FROM controlling_carriers WHERE carrier_key = @key;";
                    cmd.Parameters.AddWithValue("@key", row.CarrierKey);
                    var carrierId = Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);

                    cmd.Parameters.Clear();
                    cmd.CommandText = @"
                    INSERT INTO controlling_snapshots
                        (import_id, carrier_id, source_sheet, source_row, external_parent_id, parent_key, parent_name,
                         psp_name, profit_center, planned_cost, actual_cost)
                    VALUES
                        (@imp, @car, @sheet, @srow, @pid, @pkey, @pname, @psp, @pc, @plan, @act);";
                    cmd.Parameters.AddWithValue("@imp", importId);
                    cmd.Parameters.AddWithValue("@car", carrierId);
                    cmd.Parameters.AddWithValue("@sheet", row.SourceSheet ?? string.Empty);
                    cmd.Parameters.AddWithValue("@srow", row.SourceRow);
                    cmd.Parameters.AddWithValue("@pid", (object)row.ExternalParentId ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pkey", (object)row.ParentKey ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pname", (object)row.ParentName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@psp", (object)row.PspName ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@pc", (object)row.ProfitCenter ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@plan", DecimalParameter(row.PlannedCost));
                    cmd.Parameters.AddWithValue("@act", DecimalParameter(row.ActualCost));
                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
                return new ControllingSaveResult { Status = ControllingSaveStatus.Saved, ImportId = importId };
            }
            catch
            {
                try { tx.Rollback(); } catch { /* Transaktion ist bereits beendet */ }
                throw;
            }
        }
    }

    private static void InsertControllingParent(SQLiteCommand cmd, int importId, ParentContext p)
    {
        cmd.Parameters.Clear();
        cmd.CommandText = @"
        INSERT INTO controlling_project_context
            (import_id, parent_key, external_parent_id, parent_name, customer, manager, status, cost_center,
             planned_start, planned_end, order_value_total, invoiced_total, open_total, invoiced_ratio,
             progress_ratio, wip_open, order_value_year, open_year)
        VALUES
            (@imp, @key, @pid, @name, @cust, @mgr, @status, @cc,
             @start, @end, @ovt, @inv, @open, @ratio,
             @prog, @wip, @ovy, @opy);";
        cmd.Parameters.AddWithValue("@imp", importId);
        cmd.Parameters.AddWithValue("@key", p.ParentKey);
        cmd.Parameters.AddWithValue("@pid", p.ExternalParentId ?? p.ParentKey);
        cmd.Parameters.AddWithValue("@name", (object)p.ParentName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cust", (object)p.Customer ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@mgr", (object)p.Manager ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@status", (object)p.Status ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cc", (object)p.CostCenter ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@start", DateParameter(p.PlannedStart));
        cmd.Parameters.AddWithValue("@end", DateParameter(p.PlannedEnd));
        cmd.Parameters.AddWithValue("@ovt", DecimalParameter(p.OrderValueTotal));
        cmd.Parameters.AddWithValue("@inv", DecimalParameter(p.InvoicedTotal));
        cmd.Parameters.AddWithValue("@open", DecimalParameter(p.OpenTotal));
        cmd.Parameters.AddWithValue("@ratio", DecimalParameter(p.InvoicedRatio));
        cmd.Parameters.AddWithValue("@prog", DecimalParameter(p.ProgressRatio));
        cmd.Parameters.AddWithValue("@wip", DecimalParameter(p.WipOpen));
        cmd.Parameters.AddWithValue("@ovy", DecimalParameter(p.OrderValueYear));
        cmd.Parameters.AddWithValue("@opy", DecimalParameter(p.OpenYear));
        cmd.ExecuteNonQuery();
    }

    // Beträge werden als exakter Dezimaltext gespeichert (REAL könnte Geld nicht exakt halten); unbekannt = NULL.
    private static object DecimalParameter(decimal? value)
    {
        var text = CostMath.ToInvariantText(value);
        return text == null ? (object)DBNull.Value : text;
    }

    private static object DateParameter(DateTime? value)
    {
        return value.HasValue ? (object)value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
    }

    // ---------- Lesen: Importe ----------

    private const string ImportColumns =
        "id, file_name, file_hash, source_timestamp, imported_utc, timestamp_origin, currency_code, currency_context, " +
        "parser_version, scanned_count, matched_count, ignored_count, warning_count, warnings";

    /// <summary>Alle Importe, neueste Quellstände zuerst (bei gleichem Stand der jüngere Import zuerst).</summary>
    public List<ControllingImportInfo> GetControllingImports()
    {
        var result = new List<ControllingImportInfo>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT " + ImportColumns + " FROM controlling_imports ORDER BY source_timestamp DESC, id DESC;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read()) result.Add(ReadImport(r));
            }
        }
        return result;
    }

    public ControllingImportInfo GetControllingImport(int importId)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT " + ImportColumns + " FROM controlling_imports WHERE id = @id;";
            cmd.Parameters.AddWithValue("@id", importId);
            using (var r = cmd.ExecuteReader())
            {
                return r.Read() ? ReadImport(r) : null;
            }
        }
    }

    private static ControllingImportInfo ReadImport(SQLiteDataReader r)
    {
        var info = new ControllingImportInfo
        {
            Id = r.GetInt32(0),
            FileName = r.GetString(1),
            FileHash = r.GetString(2),
            SourceTimestamp = ParseSourceTimestamp(r.GetString(3)),
            ImportedUtc = ParseUtc(r.GetString(4)),
            TimestampOrigin = r.GetString(5) == "user" ? TimestampOrigin.User : TimestampOrigin.Workbook,
            CurrencyCode = r.IsDBNull(6) ? null : r.GetString(6),
            CurrencyContext = r.GetString(7),
            ParserVersion = r.GetInt32(8),
            ScannedCount = r.GetInt32(9),
            MatchedCount = r.GetInt32(10),
            IgnoredCount = r.GetInt32(11),
            WarningCount = r.GetInt32(12)
        };
        if (!r.IsDBNull(13))
            info.Warnings.AddRange(r.GetString(13).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries));
        return info;
    }

    private static DateTime ParseSourceTimestamp(string text)
    {
        return DateTime.ParseExact(text, CostMath.TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    private static DateTime ParseUtc(string text)
    {
        return DateTime.ParseExact(text, CostMath.UtcFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    // ---------- Lesen: Beobachtungen ----------

    // Je Beobachtung: die "aktive" Revision ist die mit der größten Import-ID für dasselbe PSP-Element,
    // denselben Quellstand und denselben Währungskontext. Ältere Revisionen bleiben abrufbar.
    private const string ObservationSelect = @"
        SELECT s.id, s.import_id, c.carrier_key, c.display_text, i.source_timestamp, i.imported_utc, i.file_name,
               i.currency_code, i.currency_context, s.source_sheet, s.source_row, s.external_parent_id, s.parent_key,
               s.parent_name, s.psp_name, s.profit_center, s.planned_cost, s.actual_cost,
               (SELECT MAX(i2.id) FROM controlling_snapshots s2 JOIN controlling_imports i2 ON i2.id = s2.import_id
                 WHERE s2.carrier_id = s.carrier_id AND i2.source_timestamp = i.source_timestamp
                   AND i2.currency_context = i.currency_context) AS active_import_id,
               (SELECT COUNT(*) FROM controlling_snapshots s3 JOIN controlling_imports i3 ON i3.id = s3.import_id
                 WHERE s3.carrier_id = s.carrier_id AND i3.source_timestamp = i.source_timestamp
                   AND i3.currency_context = i.currency_context) AS revision_count
          FROM controlling_snapshots s
          JOIN controlling_imports i ON i.id = s.import_id
          JOIN controlling_carriers c ON c.id = s.carrier_id ";

    private List<ControllingObservation> QueryObservations(string whereAndOrder, Action<SQLiteCommand> parameters)
    {
        var result = new List<ControllingObservation>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = ObservationSelect + whereAndOrder;
            parameters?.Invoke(cmd);
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new ControllingObservation
                    {
                        SnapshotId = r.GetInt32(0),
                        ImportId = r.GetInt32(1),
                        CarrierKey = r.GetString(2),
                        CarrierText = r.GetString(3),
                        SourceTimestamp = ParseSourceTimestamp(r.GetString(4)),
                        ImportedUtc = ParseUtc(r.GetString(5)),
                        FileName = r.GetString(6),
                        CurrencyCode = r.IsDBNull(7) ? null : r.GetString(7),
                        CurrencyContext = r.GetString(8),
                        SourceSheet = r.GetString(9),
                        SourceRow = r.GetInt32(10),
                        ExternalParentId = r.IsDBNull(11) ? null : r.GetString(11),
                        ParentKey = r.IsDBNull(12) ? null : r.GetString(12),
                        ParentName = r.IsDBNull(13) ? null : r.GetString(13),
                        PspName = r.IsDBNull(14) ? null : r.GetString(14),
                        ProfitCenter = r.IsDBNull(15) ? null : r.GetString(15),
                        PlannedCost = r.IsDBNull(16) ? (decimal?)null : CostMath.ParseInvariant(r.GetString(16)),
                        ActualCost = r.IsDBNull(17) ? (decimal?)null : CostMath.ParseInvariant(r.GetString(17)),
                        ActiveImportId = r.GetInt32(18),
                        RevisionCount = r.GetInt32(19)
                    });
                }
            }
        }
        return result;
    }

    /// <summary>Genau der Inhalt einer Charge (auch wenn einzelne Zeilen inzwischen durch Korrekturen überholt sind).</summary>
    public List<ControllingObservation> GetControllingBatch(int importId)
    {
        return QueryObservations("WHERE s.import_id = @id ORDER BY c.carrier_key;", cmd => cmd.Parameters.AddWithValue("@id", importId));
    }

    /// <summary>
    /// Verlauf eines PSP-Elements: je Quellstand die aktive Revision, nach Quellstand aufsteigend
    /// (nicht nach Importreihenfolge). Jeder Währungskontext hat seinen eigenen Verlauf.
    /// </summary>
    public List<ControllingObservation> GetControllingCarrierHistory(string carrierKey, string currencyContext)
    {
        return QueryObservations(
                "WHERE c.carrier_key = @key AND i.currency_context = @ctx ORDER BY i.source_timestamp, i.id;",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("@key", carrierKey);
                    cmd.Parameters.AddWithValue("@ctx", currencyContext);
                })
            .Where(o => o.IsActive)
            .ToList();
    }

    /// <summary>Alle Revisionen eines PSP-Elements zu einem Quellstand, die neueste zuerst.</summary>
    public List<ControllingObservation> GetControllingCarrierRevisions(string carrierKey, DateTime sourceTimestamp, string currencyContext)
    {
        return QueryObservations(
            "WHERE c.carrier_key = @key AND i.currency_context = @ctx AND i.source_timestamp = @src ORDER BY i.id DESC;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@key", carrierKey);
                cmd.Parameters.AddWithValue("@ctx", currencyContext);
                cmd.Parameters.AddWithValue("@src", sourceTimestamp.ToString(CostMath.TimestampFormat, CultureInfo.InvariantCulture));
            });
    }

    /// <summary>
    /// Je PSP-Element der letzte bekannte Stand (aktive Revision mit dem größten Quellstand) im Währungskontext.
    /// Mit <paramref name="notAfter"/> zählen nur Stände bis einschließlich dieses Quellstands (für den Vergleich in der Vorschau).
    /// Ein später importierter, aber älterer Stand verdrängt nie einen neueren Quellstand.
    /// </summary>
    public Dictionary<string, ControllingObservation> GetLatestControllingObservations(
        IEnumerable<string> carrierKeys, string currencyContext, DateTime? notAfter = null)
    {
        var wanted = carrierKeys == null ? null : new HashSet<string>(carrierKeys, StringComparer.Ordinal);
        var result = new Dictionary<string, ControllingObservation>(StringComparer.Ordinal);
        if (wanted != null && wanted.Count == 0) return result;

        var limit = notAfter.HasValue ? notAfter.Value.ToString(CostMath.TimestampFormat, CultureInfo.InvariantCulture) : null;
        var rows = QueryObservations(
            "WHERE i.currency_context = @ctx" + (limit == null ? string.Empty : " AND i.source_timestamp <= @lim") + " ORDER BY i.source_timestamp, i.id;",
            cmd =>
            {
                cmd.Parameters.AddWithValue("@ctx", currencyContext);
                if (limit != null) cmd.Parameters.AddWithValue("@lim", limit);
            });

        foreach (var o in rows)
        {
            if (!o.IsActive) continue;
            if (wanted != null && !wanted.Contains(o.CarrierKey)) continue;

            ControllingObservation current;
            if (!result.TryGetValue(o.CarrierKey, out current) || o.SourceTimestamp >= current.SourceTimestamp)
                result[o.CarrierKey] = o;
        }
        return result;
    }

    /// <summary>Kontext eines SAP-Projekts zu einem Import oder <c>null</c>.</summary>
    public ParentContext GetControllingParentContext(int importId, string parentKey)
    {
        if (string.IsNullOrEmpty(parentKey)) return null;

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT parent_key, external_parent_id, parent_name, customer, manager, status, cost_center,
                   planned_start, planned_end, order_value_total, invoiced_total, open_total, invoiced_ratio,
                   progress_ratio, wip_open, order_value_year, open_year
              FROM controlling_project_context
             WHERE import_id = @imp AND parent_key = @key;";
            cmd.Parameters.AddWithValue("@imp", importId);
            cmd.Parameters.AddWithValue("@key", parentKey);

            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) return null;

                Func<int, string> text = i => r.IsDBNull(i) ? null : r.GetString(i);
                Func<int, decimal?> money = i => r.IsDBNull(i) ? (decimal?)null : CostMath.ParseInvariant(r.GetString(i));
                Func<int, DateTime?> date = i => r.IsDBNull(i)
                    ? (DateTime?)null
                    : DateTime.ParseExact(r.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture);

                return new ParentContext
                {
                    ParentKey = text(0),
                    ExternalParentId = text(1),
                    ParentName = text(2),
                    Customer = text(3),
                    Manager = text(4),
                    Status = text(5),
                    CostCenter = text(6),
                    PlannedStart = date(7),
                    PlannedEnd = date(8),
                    OrderValueTotal = money(9),
                    InvoicedTotal = money(10),
                    OpenTotal = money(11),
                    InvoicedRatio = money(12),
                    ProgressRatio = money(13),
                    WipOpen = money(14),
                    OrderValueYear = money(15),
                    OpenYear = money(16)
                };
            }
        }
    }

    // ---------- Löschen ----------

    /// <summary>
    /// Löscht einen Import samt Beobachtungen und Projektkontext. Frühere Revisionen werden dadurch wieder aktiv.
    /// PSP-Elemente ohne verbleibende Beobachtung verschwinden ebenfalls. Lokale Projekte und Buchungen bleiben unberührt.
    /// </summary>
    public bool DeleteControllingImport(int importId)
    {
        using (var tx = _conn.BeginTransaction())
        using (var cmd = _conn.CreateCommand())
        {
            cmd.Transaction = tx;
            try
            {
                cmd.CommandText = "DELETE FROM controlling_imports WHERE id = @id;";
                cmd.Parameters.AddWithValue("@id", importId);
                var deleted = cmd.ExecuteNonQuery() > 0;

                cmd.Parameters.Clear();
                cmd.CommandText = "DELETE FROM controlling_carriers WHERE id NOT IN (SELECT carrier_id FROM controlling_snapshots);";
                cmd.ExecuteNonQuery();

                tx.Commit();
                return deleted;
            }
            catch
            {
                try { tx.Rollback(); } catch { /* bereits beendet */ }
                throw;
            }
        }
    }

    // ---------- Lokale Projekte und Buchungen (für Zuordnung und Stundenverlauf) ----------

    /// <summary>
    /// Alle lokalen Projekte samt Kostenträger, unabhängig von Intern/Extern und Quick Switch.
    /// Liest nur; ändert weder Projekte noch Buchungen.
    /// </summary>
    public List<LocalProjectInfo> GetLocalProjectInfos()
    {
        var result = new List<LocalProjectInfo>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, projektname, kunde, kostentraeger, ist_extern FROM projects ORDER BY projektname;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new LocalProjectInfo
                    {
                        Id = r.GetInt32(0),
                        Name = r.GetString(1),
                        Customer = r.GetString(2),
                        Carrier = r.GetString(3),
                        IsExternal = r.GetInt32(4) != 0
                    });
                }
            }
        }
        return result;
    }

    /// <summary>Alle Buchungen der angegebenen Projekte (jede Buchung genau einmal), nach Datum und Startzeit sortiert.</summary>
    public List<LocalTimeEntry> GetTimeEntriesForProjectIds(IEnumerable<int> projectIds)
    {
        var ids = (projectIds ?? Enumerable.Empty<int>()).Distinct().ToList();
        var result = new List<LocalTimeEntry>();

        for (var offset = 0; offset < ids.Count; offset += 400)
        {
            var chunk = ids.Skip(offset).Take(400).ToList();
            using (var cmd = _conn.CreateCommand())
            {
                var names = new List<string>();
                for (var i = 0; i < chunk.Count; i++)
                {
                    names.Add("@p" + i.ToString(CultureInfo.InvariantCulture));
                    cmd.Parameters.AddWithValue(names[i], chunk[i]);
                }

                cmd.CommandText =
                    "SELECT id, projekt_id, datum, startzeit, endzeit, beschreibung FROM time_entries WHERE projekt_id IN (" +
                    string.Join(",", names) + ") ORDER BY datum, startzeit, id;";

                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        result.Add(new LocalTimeEntry
                        {
                            Id = r.GetInt32(0),
                            ProjectId = r.GetInt32(1),
                            DateText = r.GetString(2),
                            Start = r.GetString(3),
                            End = r.GetString(4),
                            Description = r.IsDBNull(5) ? null : r.GetString(5)
                        });
                    }
                }
            }
        }

        return result.OrderBy(e => e.DateText, StringComparer.Ordinal).ThenBy(e => e.Start, StringComparer.Ordinal).ThenBy(e => e.Id).ToList();
    }
}
