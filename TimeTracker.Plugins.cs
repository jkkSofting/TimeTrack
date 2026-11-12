// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using TimeTrack.Plugins;

// Ablage der Plugins und lesender Zugriff für sie. Plugins bekommen die Verbindung nie selbst, sondern nur diese
// Abfragen über den PluginHost. Die Ablage liegt in derselben Datenbank, damit Sicherungen sie mitnehmen; die Tabellen
// der Zeiterfassung und ihre Trigger bleiben unberührt.
public sealed partial class TimeTracker
{
    /// <summary>
    /// Legt die Ablage der Plugins an (idempotent, nur additiv). Läuft innerhalb der Transaktion von
    /// <see cref="EnsureSchema"/>.
    /// </summary>
    private static void EnsurePluginSchema(SQLiteCommand cmd)
    {
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS plugin_records(
            plugin_id   TEXT NOT NULL,
            collection  TEXT NOT NULL,
            key         TEXT NOT NULL,
            json        TEXT NOT NULL,
            updated_utc TEXT NOT NULL,  -- yyyy-MM-ddTHH:mm:ssZ
            PRIMARY KEY (plugin_id, collection, key)
        );";
        cmd.ExecuteNonQuery();
    }

    // ---------- Ablage ----------

    /// <summary>Der gespeicherte Wert oder <c>null</c>.</summary>
    public string GetPluginRecord(string pluginId, string collection, string key)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT json FROM plugin_records WHERE plugin_id = @p AND collection = @c AND key = @k;";
            cmd.Parameters.AddWithValue("@p", pluginId);
            cmd.Parameters.AddWithValue("@c", collection);
            cmd.Parameters.AddWithValue("@k", key);
            var o = cmd.ExecuteScalar();
            return o == null || o == DBNull.Value ? null : (string)o;
        }
    }

    /// <summary>Legt den Eintrag an oder überschreibt ihn.</summary>
    public void SetPluginRecord(string pluginId, string collection, string key, string json)
    {
        if (json == null) throw new ArgumentNullException(nameof(json));

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            INSERT INTO plugin_records (plugin_id, collection, key, json, updated_utc)
                 VALUES (@p, @c, @k, @j, @u)
            ON CONFLICT(plugin_id, collection, key) DO UPDATE SET
                 json = excluded.json,
                 updated_utc = excluded.updated_utc;";
            cmd.Parameters.AddWithValue("@p", pluginId);
            cmd.Parameters.AddWithValue("@c", collection);
            cmd.Parameters.AddWithValue("@k", key);
            cmd.Parameters.AddWithValue("@j", json);
            cmd.Parameters.AddWithValue("@u", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Löscht den Eintrag. <c>true</c>, wenn es ihn gab.</summary>
    public bool DeletePluginRecord(string pluginId, string collection, string key)
    {
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM plugin_records WHERE plugin_id = @p AND collection = @c AND key = @k;";
            cmd.Parameters.AddWithValue("@p", pluginId);
            cmd.Parameters.AddWithValue("@c", collection);
            cmd.Parameters.AddWithValue("@k", key);
            return cmd.ExecuteNonQuery() > 0;
        }
    }

    /// <summary>Alle Einträge einer Sammlung, Schlüssel → Wert, nach Schlüssel sortiert.</summary>
    public Dictionary<string, string> ListPluginRecords(string pluginId, string collection)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT key, json FROM plugin_records WHERE plugin_id = @p AND collection = @c ORDER BY key;";
            cmd.Parameters.AddWithValue("@p", pluginId);
            cmd.Parameters.AddWithValue("@c", collection);
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    result[r.GetString(0)] = r.GetString(1);
            }
        }
        return result;
    }

    // ---------- Lesender Zugriff für Plugins ----------

    /// <summary>Alle Projekte mit ihrer internen Kennung, nach Name sortiert.</summary>
    public List<PluginProject> GetPluginProjects()
    {
        var result = new List<PluginProject>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, projektname, kunde, kostentraeger, ist_extern FROM projects ORDER BY projektname;";
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    result.Add(new PluginProject
                    {
                        Id = r.GetInt32(0),
                        Name = r.GetString(1),
                        Customer = r.GetString(2),
                        CostCarrier = r.GetString(3),
                        IsExternal = r.GetInt32(4) != 0
                    });
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Alle Buchungen im Zeitraum [fromInclusive, toInclusive] mit Projekt und Kostenträger, sortiert nach Datum und
    /// Beginn. Buchungen mit unlesbaren Zeiten oder Ende vor Beginn werden übersprungen.
    /// </summary>
    public List<PluginTimeEntry> GetPluginTimeEntries(DateTime fromInclusive, DateTime toInclusive)
    {
        var result = new List<PluginTimeEntry>();
        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT te.id, te.datum, te.startzeit, te.endzeit, te.beschreibung, p.id, p.projektname, p.kostentraeger
              FROM time_entries te
              JOIN projects p ON p.id = te.projekt_id
             WHERE te.datum >= @from AND te.datum <= @to
             ORDER BY te.datum, te.startzeit, te.id;";
            cmd.Parameters.AddWithValue("@from", fromInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.Parameters.AddWithValue("@to", toInclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    if (!TryParseClock(r.GetString(2), out var start) || !TryParseClock(r.GetString(3), out var end) || end <= start)
                        continue;

                    result.Add(new PluginTimeEntry
                    {
                        Id = r.GetInt32(0),
                        Date = DateTime.ParseExact(r.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Start = start,
                        End = end,
                        Description = r.IsDBNull(4) ? null : r.GetString(4),
                        ProjectId = r.GetInt32(5),
                        ProjectName = r.GetString(6),
                        CostCarrier = r.GetString(7)
                    });
                }
            }
        }
        return result;
    }

    private static bool TryParseClock(string text, out TimeSpan value)
    {
        return TimeSpan.TryParseExact((text ?? string.Empty).Trim(), TimeFormats, CultureInfo.InvariantCulture, out value);
    }
}
