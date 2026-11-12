// Target: .NET Framework + C# 7.3
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Zeitmanagement.Helpers
{
    /// <summary>
    /// Die zuletzt benutzte Auswahl des Tätigkeitsnachweises (Zeitraum, Projekte, Optionen), als JSON in den Benutzereinstellungen.
    /// Lesen wirft nie: Fehlt etwas oder ist der Text unlesbar, gelten die Standardwerte. Unbekannte Werte bleiben beim Standard.
    /// Die Rundung der Dauer wird bewusst nie gemerkt: Sie ändert die abrechenbare Zeit und muss jedes Mal ausdrücklich gewählt werden.
    /// </summary>
    [DataContract]
    public sealed class ExportPrefs
    {
        /// <summary>Schnellwahl des Zeitraums (<see cref="ExportPeriod"/>); bei "custom" gelten <see cref="From"/> und <see cref="To"/>.</summary>
        [DataMember(Name = "period")] public string PeriodKey { get; set; }
        [DataMember(Name = "from")] public string From { get; set; }
        [DataMember(Name = "to")] public string To { get; set; }
        [DataMember(Name = "projects")] public List<string> ProjectNames { get; set; }

        [DataMember(Name = "kind")] public string Kind { get; set; }
        [DataMember(Name = "grouping")] public string Grouping { get; set; }
        [DataMember(Name = "duration")] public string DurationFormat { get; set; }
        [DataMember(Name = "showTimes")] public bool? ShowTimes { get; set; }
        [DataMember(Name = "alwaysProject")] public bool AlwaysShowProjectColumn { get; set; }
        [DataMember(Name = "carrier")] public bool ShowCarrierColumn { get; set; }
        [DataMember(Name = "daySums")] public bool? ShowDaySubtotals { get; set; }
        [DataMember(Name = "summary")] public bool? ShowSummary { get; set; }
        /// <summary>Der Auftraggeber steht nur im Dokument, wenn der Nutzer es will (Standard: nein).</summary>
        [DataMember(Name = "showClient")] public bool ShowClient { get; set; }

        [DataMember(Name = "title")] public string Title { get; set; }
        [DataMember(Name = "landscape")] public bool Landscape { get; set; }
        [DataMember(Name = "branding")] public bool? ShowBranding { get; set; }

        [DataMember(Name = "csvInternational")] public bool CsvInternational { get; set; }
        [DataMember(Name = "csvIso")] public bool CsvIsoDates { get; set; }
        [DataMember(Name = "csvTotal")] public bool CsvTotalRow { get; set; }

        private static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(ExportPrefs));

        public string ToJson()
        {
            using (var stream = new MemoryStream())
            {
                Serializer.WriteObject(stream, this);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        /// <summary>Liest die gespeicherte Auswahl; bei leerem oder kaputtem Text die Standardwerte.</summary>
        public static ExportPrefs FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new ExportPrefs();

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    return (ExportPrefs)Serializer.ReadObject(stream) ?? new ExportPrefs();
                }
            }
            catch (Exception)
            {
                // Beliebiger Text aus der Konfigurationsdatei: Was auch schiefgeht, es gelten die Standardwerte.
                return new ExportPrefs();
            }
        }

        // ---------- Lesen mit Standardwerten ----------

        public ExportProjectKind GetKind() { return ParseEnum(Kind, ExportProjectKind.All); }
        public ExportGrouping GetGrouping() { return ParseEnum(Grouping, ExportGrouping.ByDay); }
        public ExportDurationFormat GetDurationFormat() { return ParseEnum(DurationFormat, ExportDurationFormat.Decimal); }

        public bool GetShowTimes() { return ShowTimes ?? true; }
        public bool GetShowDaySubtotals() { return ShowDaySubtotals ?? true; }
        public bool GetShowSummary() { return ShowSummary ?? true; }
        public bool GetShowBranding() { return ShowBranding ?? true; }

        /// <summary>Der Zeitraum, falls er fest gespeichert ist (Benutzerdefiniert); sonst <c>false</c>.</summary>
        public bool TryGetCustomRange(out DateTime from, out DateTime to)
        {
            from = to = DateTime.MinValue;
            DateTime f, t;
            if (!TryParseDate(From, out f) || !TryParseDate(To, out t) || f > t) return false;
            from = f;
            to = t;
            return true;
        }

        public static string FormatDate(DateTime date)
        {
            return ExportFormat.IsoDate(date);
        }

        private static bool TryParseDate(string text, out DateTime date)
        {
            return DateTime.TryParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out date);
        }

        private static T ParseEnum<T>(string text, T fallback) where T : struct
        {
            T value;
            return !string.IsNullOrEmpty(text) && Enum.TryParse(text, true, out value) && Enum.IsDefined(typeof(T), value) ? value : fallback;
        }
    }
}
