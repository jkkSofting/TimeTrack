// Target: .NET Framework + C# 7.3
using System;
using System.Data.SQLite;

// Angaben für den Tätigkeitsnachweis, die zu einem Projekt gehören (Auftrags-/Bestellnummer, Auftraggeber). Sie liegen in
// derselben Datenbank wie die Zeiterfassung, damit Sicherungen sie mitnehmen. Verknüpft wird über die Projekt-ID: Die Angaben
// überleben ein Umbenennen des Projekts und verschwinden mit dem Projekt. Zeiterfassungstabellen und Trigger bleiben unberührt.
public sealed partial class TimeTracker
{
    /// <summary>
    /// Legt die Tabelle für die Nachweis-Angaben an (idempotent, nur additiv).
    /// Läuft innerhalb der Transaktion von <see cref="EnsureSchema"/>.
    /// </summary>
    private static void EnsureExportSchema(SQLiteCommand cmd)
    {
        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS export_project_meta(
            projekt_id         INTEGER PRIMARY KEY REFERENCES projects(id) ON DELETE CASCADE,
            auftragsnummer     TEXT NULL,   -- Auftrags- oder Bestellnummer des Kunden
            auftraggeber_name  TEXT NULL,   -- leer = der Kunde des Projekts
            auftraggeber_block TEXT NULL    -- Anschrift, Ansprechpartner (mehrzeilig)
        );";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Die Nachweis-Angaben eines Projekts, oder <c>null</c>, wenn keine gespeichert sind.</summary>
    public ExportProjectMeta GetExportProjectMeta(string projektname)
    {
        if (string.IsNullOrWhiteSpace(projektname)) return null;

        using (var cmd = _conn.CreateCommand())
        {
            cmd.CommandText = @"
            SELECT m.auftragsnummer, m.auftraggeber_name, m.auftraggeber_block
              FROM export_project_meta m
              JOIN projects p ON p.id = m.projekt_id
             WHERE p.projektname = @n;";
            cmd.Parameters.AddWithValue("@n", projektname);

            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) return null;
                return new ExportProjectMeta
                {
                    Auftragsnummer = r.IsDBNull(0) ? null : r.GetString(0),
                    AuftraggeberName = r.IsDBNull(1) ? null : r.GetString(1),
                    AuftraggeberBlock = r.IsDBNull(2) ? null : r.GetString(2)
                };
            }
        }
    }

    /// <summary>
    /// Speichert die Nachweis-Angaben eines Projekts. Leere Angaben werden gar nicht gespeichert; sind alle leer, verschwindet
    /// der Eintrag. Ein unbekanntes Projekt wird ignoriert.
    /// </summary>
    public void SaveExportProjectMeta(string projektname, ExportProjectMeta meta)
    {
        if (string.IsNullOrWhiteSpace(projektname)) return;

        var projectId = GetProjectIdByName(projektname);
        if (projectId <= 0) return;

        var number = Normalize(meta?.Auftragsnummer);
        var name = Normalize(meta?.AuftraggeberName);
        var block = Normalize(meta?.AuftraggeberBlock);

        using (var cmd = _conn.CreateCommand())
        {
            if (number == null && name == null && block == null)
            {
                cmd.CommandText = "DELETE FROM export_project_meta WHERE projekt_id = @id;";
                cmd.Parameters.AddWithValue("@id", projectId);
            }
            else
            {
                cmd.CommandText = @"
                INSERT INTO export_project_meta (projekt_id, auftragsnummer, auftraggeber_name, auftraggeber_block)
                     VALUES (@id, @nr, @name, @block)
                ON CONFLICT(projekt_id) DO UPDATE SET
                     auftragsnummer = excluded.auftragsnummer,
                     auftraggeber_name = excluded.auftraggeber_name,
                     auftraggeber_block = excluded.auftraggeber_block;";
                cmd.Parameters.AddWithValue("@id", projectId);
                cmd.Parameters.AddWithValue("@nr", (object)number ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@name", (object)name ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@block", (object)block ?? DBNull.Value);
            }
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>Entfernt umgebende Leerzeichen und Zeilenumbrüche; leer wird zu <c>null</c>.</summary>
    private static string Normalize(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    /// <summary>Angaben für den Tätigkeitsnachweis, die zu einem Projekt gehören.</summary>
    public class ExportProjectMeta
    {
        public string Auftragsnummer { get; set; }
        /// <summary><c>null</c>/leer = der Kunde des Projekts.</summary>
        public string AuftraggeberName { get; set; }
        public string AuftraggeberBlock { get; set; }

        public bool IsEmpty =>
            string.IsNullOrWhiteSpace(Auftragsnummer) && string.IsNullOrWhiteSpace(AuftraggeberName) && string.IsNullOrWhiteSpace(AuftraggeberBlock);
    }
}
