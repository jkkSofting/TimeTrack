using System;
using System.Collections.Generic;

namespace TimeTrack.Plugins
{
    /// <summary>
    /// Lesender Zugriff auf die Zeiterfassung. Schreiben ist bewusst nicht vorgesehen, damit ein fehlerhaftes Plugin
    /// keine Buchungen verändern kann.
    /// </summary>
    /// <remarks>
    /// Darf von jedem Thread aufgerufen werden; TimeTrack führt die Abfrage auf dem UI-Thread aus, weil die Datenbank
    /// nur dort benutzt wird. Aus einem Hintergrund-Thread blockiert der Aufruf also, bis der UI-Thread frei ist.
    /// </remarks>
    public interface ITimeTrackData
    {
        /// <summary>Alle Projekte, nach Name sortiert.</summary>
        IReadOnlyList<PluginProject> GetProjects();

        /// <summary>
        /// Alle abgeschlossenen Buchungen mit Datum im Zeitraum [<paramref name="fromInclusive"/>,
        /// <paramref name="toInclusive"/>] (nur der Datumsteil zählt), sortiert nach Datum und Beginn. Eine laufende
        /// Buchung im Quick Switch ist erst nach Pause oder Stop enthalten.
        /// </summary>
        IReadOnlyList<PluginTimeEntry> GetTimeEntries(DateTime fromInclusive, DateTime toInclusive);
    }

    /// <summary>Ein Projekt der Zeiterfassung.</summary>
    public sealed class PluginProject
    {
        /// <summary>Interne Kennung. Bleibt beim Umbenennen gleich.</summary>
        public int Id { get; set; }

        public string Name { get; set; }

        public string Customer { get; set; }

        /// <summary>Kostenträger, enthält in der Regel ein SAP-PSP-Element.</summary>
        public string CostCarrier { get; set; }

        /// <summary>Vom Kunden bezahlt.</summary>
        public bool IsExternal { get; set; }
    }

    /// <summary>Eine Buchung der Zeiterfassung.</summary>
    public sealed class PluginTimeEntry
    {
        /// <summary>Interne Kennung. Bleibt beim Bearbeiten gleich, verschwindet beim Löschen.</summary>
        public int Id { get; set; }

        /// <summary>Tag der Buchung (ohne Uhrzeit).</summary>
        public DateTime Date { get; set; }

        /// <summary>Beginn als Uhrzeit des Tages.</summary>
        public TimeSpan Start { get; set; }

        /// <summary>Ende als Uhrzeit des Tages, immer nach <see cref="Start"/>.</summary>
        public TimeSpan End { get; set; }

        public TimeSpan Duration => End - Start;

        public int ProjectId { get; set; }

        public string ProjectName { get; set; }

        public string CostCarrier { get; set; }

        /// <summary>Beschreibung, kann <c>null</c> sein.</summary>
        public string Description { get; set; }
    }
}
