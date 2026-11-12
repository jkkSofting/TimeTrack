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
            new ChangelogRelease("2026.47.1", new DateTime(2026, 11, 12), "To-Do und Notizen",
                Neu("„To-Do“ in der Seitenleiste (Gruppe „Organisation“): ein Board wie bei Trello oder Jira, und zwar je Projekt ein eigenes. Oben wählst du unter „Board von“ das Projekt; „Ohne Projekt“ ist für alles, was zu keinem Projekt gehört. Du legst Tickets mit Titel und Beschreibung an und ordnest sie in Spalten."),
                Neu("Jedes Board startet mit den Spalten „To Do“, „In Arbeit“ und „Erledigt“. Spalten legst du mit „Neue Spalte“ oder „Spalte hinzufügen“ am Ende des Boards an, benennst sie um, schiebst sie nach links oder rechts und löschst sie mit dem Papierkorb im Spaltenkopf. Hat die Spalte Tickets, fragt TimeTrack vorher nach, denn die Tickets gehen mit."),
                Neu("Tickets verschiebst du mit der Maus per Ziehen, in eine andere Spalte oder an eine andere Stelle derselben Spalte; ein farbiger Strich zeigt, wo das Ticket landet. Ein Klick auf ein Ticket öffnet es zum Bearbeiten, dort kannst du es auch einer anderen Spalte zuordnen oder löschen."),
                Neu("„Notizen“ in der Seitenleiste: Notizen mit Titel, Text und auf Wunsch einem Projekt. Die Liste ist durchsuchbar und nach Projekt filterbar; wählst du ein Projekt als Filter, gehört eine neue Notiz gleich dazu. TimeTrack speichert Änderungen kurz nach dem Tippen von selbst."),
                Neu("Boards und Notizen liegen in der TimeTrack-Datenbank und sind in jeder Datensicherung enthalten. Löschst du ein Projekt, verschwindet sein Board mit; seine Notizen bleiben erhalten, nur ohne Projekt.")),

            new ChangelogRelease("2026.47.0", new DateTime(2026, 11, 12), "Plugins",
                Neu("TimeTrack lässt sich mit Plugins erweitern. Ein Plugin bringt eigene Seiten mit, die in der Seitenleiste unter „Plugins“ erscheinen, und kann Einstellungen ergänzen. Ohne Plugin ändert sich nichts."),
                Neu("Einstellungen → Plugins zeigt alle gefundenen Plugins mit Version und Status. Ein neu gefundenes Plugin ist zunächst aus und wird erst geladen, wenn du es einschaltest und TimeTrack neu startest. „Plugin-Ordner öffnen“ führt direkt zum Ordner, „Nach Plugins suchen“ findet neu abgelegte Plugins ohne Neustart (das passiert auch bei jedem Öffnen der Einstellungen) und erkennt ausgetauschte Dateien laufender Plugins."),
                Neu("Ein fehlerhaftes Plugin verhindert den Start nicht: Es bleibt aus, und die Einstellungen nennen den Grund, zum Beispiel eine aus dem Internet geladene, von Windows blockierte Datei."),
                Neu("Was ein Plugin speichert, liegt in der TimeTrack-Datenbank und ist in jeder Datensicherung enthalten. Zugangsdaten verschlüsselt Windows für deinen Benutzer; nach dem Wiederherstellen auf einem anderen Rechner musst du sie neu eingeben."),
                Behoben("Tätigkeitsnachweis, „Nach Tag“: Die Tagessumme stand nur unter Tagen mit mehreren Buchungen und las sich dadurch wie eine zu niedrige Summe über mehrere Tage. Jetzt hat jeder Tag seine eigene Tagessumme."),
                Verbessert("Tätigkeitsnachweis: Die Zusammenfassung im PDF (Gesamtzeit, Buchungen, Arbeitstage, Durchschnitt) lässt sich unter „Dokument“ mit „Zusammenfassung zeigen“ ausblenden.")),

            new ChangelogRelease("2026.46.1", new DateTime(2026, 11, 11), "Tätigkeitsnachweis",
                Neu("„Tätigkeitsnachweis“ in der Seitenleiste: Du wählst einen Zeitraum (Schnellwahl wie „Letzter Monat“, „Alle Buchungen“ der gewählten Projekte oder Von/Bis) und deine Projekte und exportierst die Buchungen als PDF oder CSV. Eine Vorschau zeigt vorher, was im Nachweis steht, mit Gesamtzeit, Arbeitstagen und jeder Buchung."),
                Neu("Das PDF ist ein A4-Dokument: Titel und Zeitraum, Auftragnehmer und Auftraggeber, Eckdaten mit Kostenträger, Zusammenfassung (bei mehreren Projekten mit Stunden und Kostenträger je Projekt), Einzelnachweis mit Zwischen- und Gesamtsumme und eine Bemerkung. Die Tabellenkopfzeile wiederholt sich auf jeder Seite, die Fußzeile nennt die Seitenzahl."),
                Neu("Gliederung nach Tag (die Tagessumme lässt sich abschalten), Projekt oder Kalenderwoche, Dauer dezimal (1,50), als h:mm (1:30) oder beides, Uhrzeiten sowie Projekt- und Kostenträger-Spalte nach Wunsch, auf Wunsch im Querformat. Dein Name und deine Anschrift als Auftragnehmer bleiben für den nächsten Nachweis gespeichert."),
                Neu("Standard ist die minutengenaue Dauer: Es wird nichts gerundet. Nur auf ausdrücklichen Wunsch rundet der Nachweis die Dauer jeder Buchung kaufmännisch auf 5, 10, 15 oder 30 Minuten und nennt dann die ungerundete Summe. Die Rundung wird nie gemerkt, jeder Nachweis beginnt wieder ohne."),
                Neu("Die CSV-Datei hat eine Zeile je Buchung und passt zum deutschen Excel (UTF-8, Semikolon, Dezimalkomma); alternativ international mit Komma und Dezimalpunkt. Text, der mit = + - oder @ beginnt, wird entschärft, damit Excel ihn nicht als Formel ausführt."),
                Neu("„PDF-Vorschau“ erzeugt den Nachweis als temporäre Datei und öffnet ihn im PDF-Programm, ohne zu speichern. So prüfst du das Aussehen, bevor du den Nachweis ablegst."),
                Neu("Auftrags-/Bestellnummer und Auftraggeber (Name, Anschrift) trägst du einmal ein; TimeTrack merkt sie für das Projekt und füllt sie beim nächsten Nachweis wieder ein. Auftragnehmer und Auftraggeber sind optional: Der Auftraggeber steht nur im PDF, wenn du „Auftraggeber im Dokument zeigen“ wählst. Optional steht ein eigenes Logo oben rechts im PDF."),
                Neu("TimeTrack merkt sich deine letzte Auswahl: Optionen, Projekte und die Schnellwahl des Zeitraums. „Letzter Monat“ gilt beim nächsten Mal für den dann letzten Monat."),
                Neu("Unter „Projekte“ öffnet ein Symbol in der Zeile den Tätigkeitsnachweis mit diesem Projekt."),
                Neu("Vor dem Export prüft TimeTrack die Buchungen und warnt vor fehlenden Beschreibungen, Überschneidungen im selben Projekt, nicht lesbaren Zeiten (diese Buchungen fehlen im Nachweis, sie werden nie mit erfundener Dauer gezählt), Dauer 0 und mehr als 12 Stunden an einem Tag. Betroffene Zeilen sind in der Vorschau hinterlegt."),
                Neu("Projektcontrolling: Ein Standard-Stundensatz in der Kopfzeile gilt für jedes Projekt ohne eigenen Satz (je Währung des Imports). Der Stundensatz in den Details eines Projekts überschreibt ihn nur für dieses Projekt, ein leeres Feld nimmt den Standard. Ohne Standard bleibt alles wie bisher."),
                Verbessert("Buttons, Eingabefelder, Auswahllisten und ihre Aufklapp-Fenster haben nur noch einen sehr kleinen Eckenradius und wirken eckiger. Ebenso Karten und Innenflächen; nur kleine Kapseln (Status, „Neu“-Marken) bleiben rund.")),

            new ChangelogRelease("2026.46.0", new DateTime(2026, 11, 10), "Projektcontrolling",
                Neu("„Projektcontrolling“ in der Seitenleiste: „Excel importieren“ liest Plan- und Ist-Kosten aus einer SAP-Projektcontrolling-Datei (.xlsx) – nur lesend. Importiert werden nur PSP-Elemente, die in einem Kostenträger deiner Projekte enthalten sind; der Kostenträger muss die PSP-Element-ID enthalten, aber nicht genau so heißen."),
                Neu("Die Vorschau zeigt vor dem Speichern, was übernommen wird, samt Warnungen und Fehlern mit Blatt und Zelle. Der SAP-Stand kommt aus der Datei (fehlt er, gibst du ihn an), die Währung ist optional."),
                Neu("Jeder Import ist ein Stand: Der Kostenverlauf zeigt die Stände nach SAP-Datum. Ein erneuter Import desselben Stands wird nicht doppelt gespeichert, eine Korrektur wird als Revision abgelegt."),
                Neu("Lokaler Stundenverlauf je Kostenträger (Tag, Woche, Monat) aus deinen Buchungen. Er steht getrennt von den SAP-Geldbeträgen und ist nicht mit SAP abgestimmt."),
                Neu("Stundensatz je Projekt: In den Details trägst du für jedes Projekt seinen Stundensatz ein (je Währung des Imports). Damit rechnet TimeTrack Plan, Ist und Rest zusätzlich in rechnerische Stunden um (Betrag ÷ Stundensatz). Haben Projekte am selben Kostenträger unterschiedliche Sätze, wird nicht umgerechnet. Die Stunden sind eine Schätzung aus Geld, keine gebuchte Zeit."),
                Neu("Die erste Spalte der Übersicht, „Stand“, zeigt den Budgetverbrauch in Prozent mit einem kleinen Fortschrittsbalken. Sie bleibt beim horizontalen Scrollen stehen, sodass der Stand immer sichtbar ist."),
                Neu("Berechnete Stunden (aus Geld umgerechnet) lassen sich mit „Berechnete Stunden (h) zeigen“ ein- und ausblenden. Die Wahl bleibt gespeichert."),
                Neu("Diagramm: Im Projektcontrolling schaltest du zwischen „Tabelle“ und „Diagramm“ um. Das Diagramm zeigt den Stand je PSP-Element über die SAP-Daten – relativ in Prozent des Plans oder in Stunden (aus Geld umgerechnet, dazu für die gewählte Zeile Plan in Stunden und deine lokal gebuchten Stunden, kumuliert). Fährst du mit der Maus über einen Punkt, siehst du den Stand dort; neben einem Punkt zeigt eine Linie alle Werte zum nächsten SAP-Datum. Mit „Kostenträger“ blendest du einzelne Kostenträger im Diagramm ein und aus (mit Suche, „Alle einblenden“ und „Alle ausblenden“)."),
                Verbessert("Unter „Projekte“ öffnet ein Symbol in der Zeile das Projektcontrolling mit dem Kostenträger des Projekts.")),

            new ChangelogRelease("2026.45.3", new DateTime(2026, 11, 5), "Quick Switch statt Quick Select",
                Neu("„Quick Switch“ in der Seitenleiste öffnet das Quick-Switch-Fenster direkt, ebenso „Quick Switch öffnen“ im Menü des Tray-Symbols."),
                Verbessert("Die Seite „Quick Select“ entfällt: Welche Projekte im Quick Switch wählbar sind, bestimmt allein der Schalter unter „Projekte“. Er heißt jetzt „Quick Switch“.")),

            new ChangelogRelease("2026.45.2", new DateTime(2026, 11, 5), "",
                Behoben("Der Schalter „Quick Select“ unter „Projekte“ wurde nicht übernommen: Ein ausgeschaltetes Projekt blieb im Quick Switch und im Quick Select wählbar. Der Schalter wirkt jetzt sofort.")),

            new ChangelogRelease("2026.45.1", new DateTime(2026, 11, 4), "",
                Verbessert("Updates kommen jetzt aus einem eigenen Release-Repository (github.com/jkkSofting/TimeTrack-Releases), in dem auch eine Anleitung und alle Neuigkeiten stehen. Für dich ändert sich nichts.")),

            new ChangelogRelease("2026.45.0", new DateTime(2026, 11, 4), "Alle Projekte im Quick Select",
                Neu("Unter „Projekte“ legt der Schalter „Quick Select“ fest, ob ein Projekt im Quick Select und im Quick Switch wählbar ist. Standardmäßig sind alle Projekte dabei."),
                Verbessert("Das Quick-Switch-Fenster bietet jetzt alle Projekte zum Starten an, nicht mehr nur die im Quick Select eingerichteten."),
                Behoben("Neu angelegte, umbenannte oder gelöschte Projekte erscheinen sofort im Quick Select und im Quick Switch, nicht erst nach einem Neustart.")),

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
