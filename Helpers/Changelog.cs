using System;
using System.Collections.Generic;
using System.Linq;

namespace Zeitmanagement.Helpers
{
    internal enum ChangeKind
    {
        Neu,
        Verbessert,
        Behoben
    }

    internal sealed class ChangelogChange
    {
        public ChangelogChange(ChangeKind kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        public ChangeKind Kind { get; }
        public string Text { get; }
    }

    internal sealed class ChangelogRelease
    {
        public ChangelogRelease(string version, DateTime date, string title, params ChangelogChange[] changes)
        {
            Version = System.Version.Parse(version);
            VersionText = version;
            Date = date;
            Title = title;
            Changes = changes;
        }

        public Version Version { get; }
        public string VersionText { get; }
        public DateTime Date { get; }
        /// <summary>Optional headline of the release, empty for small releases.</summary>
        public string Title { get; }
        public IReadOnlyList<ChangelogChange> Changes { get; }
    }

    /// <summary>
    /// What changed in which version, shown on the "Hilfe &amp; Neuigkeiten" page.
    ///
    /// Every user-visible change gets an entry here, newest release first. The version must match
    /// the git tag / AssemblyVersion of the release (Scripts\Release.ps1 refuses to release a version
    /// that has no entry). Changes that are not released yet go into an entry for the upcoming version.
    /// </summary>
    internal static class Changelog
    {
        public static readonly IReadOnlyList<ChangelogRelease> Releases = new[]
        {
            new ChangelogRelease("2026.44.0", new DateTime(2026, 10, 30), "Designs, Hilfe & Neuigkeiten",
                Neu("Hilfe & Neuigkeiten: Diese Seite erklärt alle Bereiche von TimeTrack und zeigt, was sich mit jeder Version geändert hat. Nach einem Update weist ein „Neu“ in der Seitenleiste darauf hin."),
                Neu("Designs: Standard, Wald, Ozean, Halloween, Weihnachten, Ostern, VfB Stuttgart und Werder Bremen – jedes in einer hellen und einer dunklen Variante. Auswahl in den Einstellungen, wird sofort übernommen."),
                Neu("Rund um Halloween, Weihnachten und Ostern schaltet sich das passende Design automatisch ein (abschaltbar)."),
                Neu("Hell/Dunkel-Schalter unten in der Seitenleiste."),
                Neu("Der Pinguin zieht sich passend zum Design an: Hexenhut, Weihnachtsmütze, Hasenohren, Bommelmütze, Matrosenmütze oder Fanschal."),
                Neu("Den Fisch, den der Pinguin wirft, kannst du jetzt fangen. Die Einstellungen zeigen deine Fangstatistik."),
                Neu("Wie oft der Pinguin auftaucht, lässt sich in den Einstellungen festlegen (Standard: alle 60 bis 90 Minuten)."),
                Verbessert("Neues Aussehen: gruppierte Navigation mit Icons, Uhr und Datum in der Seitenleiste und einheitliche Karten, Schaltflächen und Eingabefelder auf allen Seiten."),
                Verbessert("Diagramme, Heatmap und Zeitstrahl nutzen zum Design passende Farben, die auch bei Farbsehschwäche gut unterscheidbar sind."),
                Verbessert("Quick Select zeigt pro Zeile Laufzeit und Status."),
                Verbessert("Einstellungen mit Design-Vorschau und einer immer sichtbaren Leiste zum Speichern und Verwerfen."),
                Verbessert("Der Pinguin ist jetzt ein Kaiserpinguin.")),

            new ChangelogRelease("2026.43.0", new DateTime(2026, 10, 22), "Der Pinguin ist da",
                Neu("Maskottchen: ein Pinguin mit einer Uhr auf dem Bauch – im App-Icon, in der Seitenleiste, im Quick Switch und im Dashboard, dort mit einer Sprechblase passend zu Tageszeit und gebuchten Stunden."),
                Neu("Ab und zu schaut ein Pinguin hinter der Taskleiste hervor. Wer ihn rechtzeitig anklickt, bekommt einen Fisch zugeworfen. Lässt sich in den Einstellungen abschalten."),
                Neu("Solange du in Teams deinen Bildschirm teilst, hat das Quick-Switch-Fenster einen roten Rahmen – zur Erinnerung, dass es im Meeting zu sehen ist."),
                Verbessert("Das Projekt einer laufenden Quick-Select-Zeile lässt sich nicht mehr versehentlich ändern.")),

            new ChangelogRelease("2026.42.3", new DateTime(2026, 10, 22), "",
                Verbessert("„Projekt starten“ im Quick Switch ist alphabetisch sortiert.")),

            new ChangelogRelease("2026.42.1", new DateTime(2026, 10, 14), "",
                Verbessert("Die SAP-Verrechnung zeigt Kostenträger statt Projektnamen.")),

            new ChangelogRelease("2026.42.0", new DateTime(2026, 10, 14), "",
                Verbessert("SAP-Verrechnung: Laufen ohne externes Projekt mehrere interne Projekte parallel, zählt nur das Projekt mit weniger Stunden an diesem Tag, die parallele Zeit der anderen wird verworfen."),
                Verbessert("Die Stundenbuchung zeigt in Klammern die real gearbeitete Zeit (ohne Pausen, parallele Buchungen nur einmal gezählt).")),

            new ChangelogRelease("2026.40.1", new DateTime(2026, 9, 30), "SAP-Verrechnung",
                Neu("SAP-Verrechnung: zeigt pro Monat und Kostenträger, was wirklich im SAP gebucht werden muss. Externe Zeit deckt parallele interne Zeit ab, Überschuss wird bis Monatsende mit freien internen Zeiten verrechnet."),
                Neu("Projekte lassen sich als „extern (vom Kunden bezahlt)“ markieren.")),

            new ChangelogRelease("2026.40.0", new DateTime(2026, 9, 28), "",
                Neu("Buchungen in Quick Select und Quick Switch lassen sich pausieren und fortsetzen.")),

            new ChangelogRelease("2026.39.12", new DateTime(2026, 9, 24), "",
                Verbessert("Übersichtlichere Anzeige des Arbeitstags in der Stundenbuchung.")),

            new ChangelogRelease("2026.39.11", new DateTime(2026, 9, 23), "",
                Neu("Die Buchungen eines Tages erscheinen als Zeitstrahl statt als Tabelle, parallele Buchungen nebeneinander.")),

            new ChangelogRelease("2026.39.10", new DateTime(2026, 9, 23), "",
                Verbessert("Auch im Quick Switch startet die Buchung sofort, sobald ein Projekt gewählt ist.")),

            new ChangelogRelease("2026.39.9", new DateTime(2026, 9, 23), "",
                Verbessert("Im Quick Select startet die Buchung sofort, sobald ein Projekt gewählt ist.")),

            new ChangelogRelease("2026.39.8", new DateTime(2026, 9, 22), "",
                Neu("Die Stundenbuchung berechnet die gesetzliche Pause (30 Minuten ab 6 Stunden, 45 Minuten ab 9 Stunden) und warnt, wenn der Arbeitstag zu lang wird.")),

            new ChangelogRelease("2026.39.7", new DateTime(2026, 9, 22), "",
                Verbessert("Nach Updates wird alle 10 Minuten statt alle 6 Stunden gesucht.")),

            new ChangelogRelease("2026.39.6", new DateTime(2026, 9, 22), "",
                Neu("Quick Select hat beliebig viele Zeilen, bis zu 4 Buchungen laufen gleichzeitig."),
                Behoben("Einstellungen gingen bei einem Update verloren.")),

            new ChangelogRelease("2026.39.2", new DateTime(2026, 9, 22), "",
                Verbessert("Updates installieren sich komplett im Hintergrund, ohne Assistent.")),

            new ChangelogRelease("2026.39.0", new DateTime(2026, 9, 22), "Updates, Dashboard & Automatik",
                Neu("Automatische Updates: TimeTrack sucht neue Versionen und installiert sie per Klick in den Einstellungen."),
                Neu("Dashboard mit Aktivitäts-Heatmap der letzten 12 Monate und Vergleichen, wie lange du schon gearbeitet hast."),
                Neu("Buchungen werden bei Sperrbildschirm oder Ruhemodus automatisch beendet, und TimeTrack erinnert dich, wenn kein Projekt läuft."),
                Neu("Pausen zwischen Buchungen werden berechnet und angezeigt."),
                Neu("Unplausible Buchungen werden markiert, Überschneidungen beim Eintragen gemeldet."),
                Neu("Datensicherung: Sicherungen erstellen und wiederherstellen."),
                Verbessert("Der Installer kommt ohne Lizenz- und Infoseiten aus.")),

            new ChangelogRelease("2026.29.2", new DateTime(2026, 7, 14), "",
                Behoben("Das Quick-Switch-Fenster erscheint zuverlässig mittig.")),

            new ChangelogRelease("2026.29.1", new DateTime(2026, 7, 14), "",
                Neu("Einstellungen für den Programmstart (normal oder minimiert, auf Wunsch mit Quick Switch)."),
                Neu("TimeTrack kann mit Windows starten, auch direkt im Installer wählbar.")),

            new ChangelogRelease("2026.29.0", new DateTime(2026, 7, 14), "Quick Switch & Fragmentierung",
                Neu("Quick Switch: ein kleines, immer sichtbares Fenster zum schnellen Projektwechsel."),
                Neu("Fragmentierung: zeigt, wie zersplittert ein Tag war, mit Details zu jedem Abschnitt."),
                Verbessert("Buchungen werden genauer geprüft (Ende nach Start), Fehlermeldungen sind verständlicher."),
                Behoben("Fehler bei der Zuordnung von Buchungen und beim Tageswechsel um Mitternacht.")),

            new ChangelogRelease("2026.22.1", new DateTime(2026, 5, 29), "",
                Verbessert("Lange Beschreibungen werden in der Stundenbuchung gekürzt.")),

            new ChangelogRelease("2026.22.0", new DateTime(2026, 5, 29), "Die erste Version",
                Neu("Projekte anlegen, Stunden buchen und im Dashboard auswerten."),
                Neu("Quick Select zum schnellen Wechseln zwischen Projekten."),
                Verbessert("Stunden werden als Stunden:Minuten angezeigt.")),
        };

        /// <summary>The newest release, i.e. the version the changelog is written up to.</summary>
        public static ChangelogRelease Latest => Releases.OrderByDescending(r => r.Version).First();

        private static ChangelogChange Neu(string text) => new ChangelogChange(ChangeKind.Neu, text);
        private static ChangelogChange Verbessert(string text) => new ChangelogChange(ChangeKind.Verbessert, text);
        private static ChangelogChange Behoben(string text) => new ChangelogChange(ChangeKind.Behoben, text);
    }
}
