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
            new ChangelogRelease("2026.49.0", new DateTime(2026, 11, 26), "Papierkorb und aufgeräumte Projekte",
                Neu("Neuer Papierkorb unter „Verwaltung“: Gelöschte Projekte und einzeln gelöschte Buchungen liegen dort, getrennt in zwei Tabs. Du kannst sie wiederherstellen oder endgültig löschen, und „Papierkorb leeren“ entfernt alles auf einmal."),
                Neu("Ein Projekt im Papierkorb blendet seine Buchungen aus allen Ansichten und Summen aus. Stellst du es wieder her, kommen die Buchungen mit zurück – außer die, die du einzeln gelöscht hast."),
                Neu("Eine einzeln gelöschte Buchung, deren Projekt ebenfalls im Papierkorb liegt, stellt beim Wiederherstellen das Projekt mit wieder her."),
                Neu("Ist der Projektname beim Wiederherstellen inzwischen vergeben, erhält das Projekt den Zusatz „(wiederhergestellt)“. Ein Hinweis nennt den neuen Namen."),
                Verbessert("Projekte und Buchungen löschst du jetzt in den Papierkorb: Vorher fragt TimeTrack nach, und „Nein“ ist der Standard. Der Knopf im Buchungs-Overlay heißt entsprechend „In den Papierkorb“."),
                Verbessert("Endgültiges Löschen und „Papierkorb leeren“ fragen ebenfalls nach und lassen sich nicht rückgängig machen."),
                Verbessert("Wird eine laufende Quick-Switch-Buchung gestoppt, deren Projekt inzwischen im Papierkorb liegt, erklärt TimeTrack das in einer Meldung, statt eine Buchung anzulegen, die es nicht gibt. Die Zeit geht in diesem Fall verloren; stelle das Projekt wieder her und trage sie unter „Buchungen“ nach.")),

            new ChangelogRelease("2026.48.0", new DateTime(2026, 11, 23), "Standardrundung, Overlays und nur eine Instanz",
                Neu("TimeTrack läuft jetzt nur noch einmal: Startest du es ein zweites Mal – etwa über die Verknüpfung oder den Autostart –, wird das bereits laufende Fenster in den Vordergrund geholt und ein Hinweis eingeblendet, statt eine zweite Instanz zu öffnen. Lässt sich die laufende Instanz nicht erreichen, erklärt eine Meldung, was zu tun ist. So greifen nie zwei Instanzen gleichzeitig auf dieselbe Datenbank zu."),
                Neu("Globale Standardrundung: Die SAP-Verrechnung rundet jede Buchung auf die nächste angefangene Zeiteinheit auf. Die Standardrundung gilt ab Werk für 15 Minuten und ist für alle Projekte gleich, die sie erben."),
                Neu("Jedes Projekt wählt unter „Projekte“ seine Rundung: die Standardrundung erben, eine eigene Rundung (5, 10, 15 oder 60 Minuten) festlegen oder die Rundung für dieses Projekt ausschalten."),
                Neu("Auf der Seite „Projekte“ steht die Standardrundung über der Tabelle, und ein Stift-Button daneben ändert sie in einem Overlay. Die Spalte „Rundung“ zeigt „Standard (15 Minuten)“, „Aus“ oder den eigenen Wert."),
                Verbessert("Beim Neustart aus den Einstellungen (Sicherung wiederherstellen, Plugins) wartet die neue Instanz, bis die alte beendet ist."),
                Verbessert("Bestehende Projekte mit eigener Rundung behalten sie. Projekte ohne eigene Rundung nutzen künftig die Standardrundung. Wenn ein Projekt nicht gerundet werden soll, stelle es in seinen Einstellungen auf „Keine Rundung“."),
                Verbessert("Projekte anlegen und bearbeiten sowie Buchungen bearbeiten in der Stundenbuchung öffnen sich jetzt in einem Overlay über dem Fenster statt in einem Panel auf der Seite. Esc bricht ab. Neue Buchungen in der Stundenbuchung werden weiterhin direkt auf der Seite angelegt.")),

            new ChangelogRelease("2026.47.8", new DateTime(2026, 11, 20), "Navigation, Datumssprünge, Stundenbuchung und Tätigkeitsbericht",
                Verbessert("Das Scroll-Verhalten in der SAP-Buchung wurde verbessert.")),


            new ChangelogRelease("2026.47.7", new DateTime(2026, 11, 20), "Navigation, Datumssprünge, Stundenbuchung und Tätigkeitsbericht",
                Verbessert("Das „Tatsächliche Ende“ wurde aus der Stundenbuchung entfernt."),
                Neu("In der Stundenbuchung lässt sich über „Rundungen anzeigen“ zusätzlich die Aufrundung pro Projekt einblenden: pro Buchung in der Timeline und als Summe in der Kennzahlenleiste."),
                Neu("In der SAP-Verrechnung und der Fragmentierung springt ein Klick auf das Datum direkt zu diesem Tag in der Stundenbuchung."),
                Neu("Zurück/Vor: Ein Verlauf der besuchten Seiten ermöglicht das Vor- und Zurücknavigieren – über die Pfeile oben in der Seitenleiste, Alt+Links/Alt+Rechts oder die Maus-Zusatztasten. Auch die Sprünge zu einem Tag in der Stundenbuchung landen im Verlauf."),
                Neu("Im Tätigkeitsbericht wird jetzt die Rundung je Projekt berücksichtigt und ausgewiesen (Option „Rundung je Projekt anwenden“, standardmäßig an): Aufrundungen stehen in PDF, CSV und Vorschau, die reale Summe bleibt sichtbar.")),

            new ChangelogRelease("2026.47.6", new DateTime(2026, 11, 18), "Quick Switch beim Update sauber speichern",
                Behoben("Beim Installieren einer neuen Version wurden laufende Quick-Switch-Buchungen nicht zuverlässig gespeichert, weil der Installer TimeTrack beenden konnte, bevor es fertig gespeichert hatte. Jetzt werden die laufenden Abschnitte gespeichert, bevor das Update startet, und auch dann, wenn der Installer TimeTrack selbst schließt oder Windows herunterfährt. Die Einträge sind danach pausiert."),
                Behoben("Auch wenn das Hauptfenster bereits geschlossen war, konnte das Speichern beim Beenden übersprungen werden. Das ist behoben.")),

            new ChangelogRelease("2026.47.5", new DateTime(2026, 11, 17), "Pinguin: Fisch fangen, Wurfobjekte und Übungsmodus",
                Neu("Der Pinguin wirft je nach Design etwas anderes: im Standard den Fisch, im „Wald“ einen Fliegenpilz, im „Ozean“ einen Seestern, an Halloween einen Kürbis, an Weihnachten eine Christbaumkugel, an Ostern ein Osterei, bei „GTA VI“ ein Geldbündel, bei „Star Wars“ den Todesstern und bei den Vereinsdesigns einen Fußball in Vereinsfarben. Gefangen wird es wie der Fisch und zählt in der Statistik „Fisch gefangen“."),
                Neu("Zum Üben gibt es in den Einstellungen unter „Pinguin“ den Button „Pinguin zum Üben rufen“. Der Pinguin kommt sofort, auch wenn er sonst ausgeschaltet ist. Was du dabei fängst, zählt nicht in die Statistik."),
                Neu("Die Fangstatistik (Pinguin gefangen, Fisch gefangen) lässt sich in den Einstellungen mit „Statistik zurücksetzen“ wieder auf 0 setzen, nach einer Rückfrage."),
                Neu("In den Einstellungen unter „Pinguin“ und in der Hilfe unter „Der Pinguin“ steht jetzt, wie du den Fisch fängst."),
                Verbessert("Der Fisch ist leichter zu fangen: Er lässt sich schon fangen, sobald er an der Scheibe klebt, bleibt dort etwa eine Sekunde hängen, rutscht dann gut drei Sekunden lang langsam nach unten und zappelt dabei weniger."),
                Verbessert("Um das Wurfobjekt liegt eine unsichtbare Fangfläche, die etwa 30 Pixel über seinen Rand hinausreicht. Ein Klick knapp daneben fängt es, und du klickst nicht versehentlich etwas dahinter an."),
                Behoben("Der Fisch ließ sich gar nicht anklicken, weil sein Fenster alle Mausklicks durchgelassen hat. Jetzt fängst du ihn mit einem Klick.")),

            new ChangelogRelease("2026.47.4",new DateTime(2026, 11, 17), "Rundung pro Projekt, Quick Switch und neue Designs",
                Neu("Unter „Projekte“ lässt sich pro Projekt „Aufrunden pro Buchung“ einstellen: Aus, 5, 10, 15 oder 60 Minuten. Die Auswahl gibt es beim Anlegen und beim Bearbeiten, die Tabelle hat dafür eine Spalte „Rundung“."),
                Neu("SAP-Verrechnung: Jede Buchung eines Projekts mit Rundung wird auf die nächste angefangene Zeiteinheit aufgerundet. Bei 15 Minuten werden aus 2 Minuten 15 und aus 17 Minuten 30. Die aufgerundete Zeit zählt im SAP wie echte Zeit und kann so zum Beispiel parallele interne Zeit abdecken."),
                Neu("Auf der Seite „SAP-Verrechnung“ steht die aufgerundete Zeit in einer eigenen Farbe am Ende der Buchung (bei 2 Minuten also die 13 aufgerundeten Minuten). Die Kennzahl „Aufgerundet“ zeigt die Summe, und Hinweise je Tag und Kostenträger nennen, was dazugekommen ist."),
                Neu("Die Arbeitszeit (erste bis letzte Buchung) bleibt unverändert. Buchungen, Auswertungen und Exporte werden nicht gerundet, nur die SAP-Verrechnung."),
                Neu("Neues Design „GTA VI“ im Vice-City-Look: tiefe Dämmerungs-Violetttöne, warme Pastellflächen und flamingopinke sowie sonnenuntergangsfarbene Akzente. Es gibt es in Hell und Dunkel, gewählt wird es unter „Einstellungen“ bei „Design“."),
                Neu("Das Design „GTA VI“ erscheint automatisch nur einmalig vom 19.11. bis 30.11.2026, sofern du saisonale Designs nicht abschaltest. Danach bleibt es manuell auswählbar. Der Pinguin trägt dazu eine Sonnenbrille und eine Hibiskusblüte."),
                Neu("Neues Design „Star Wars“: hell im Wüstensand von Tatooine, dunkel als tiefer Weltraum mit Lichtschwert-Blau und Crawl-Gelb. Es erscheint automatisch nur am 4. Mai („May the 4th“), gewählt werden kann es jederzeit unter „Einstellungen“ bei „Design“. Der Pinguin trägt dazu ein Lichtschwert: blau im hellen, rot im dunklen Design."),
                Verbessert("Quick Switch: Bist du bei Sperrbildschirm oder Ruhemodus inaktiv, werden laufende Einträge nicht mehr entfernt, sondern pausiert. Der laufende Abschnitt wird gespeichert, der Eintrag bleibt in der Liste und lässt sich mit einem Klick fortsetzen. Im Tray erscheint der Hinweis „Buchung pausiert“. Die Einstellung heißt jetzt „Buchung bei Sperrbildschirm/Ruhemodus automatisch pausieren“."),
                Verbessert("Die Erinnerung „Kein Projekt ausgewählt“ erscheint weiterhin, wenn nur noch pausierte Einträge da sind."),
                Behoben("Beim Beenden von TimeTrack, auch über das Tray-Menü, bei einem Update oder bei der Windows-Abmeldung, werden laufende Quick-Switch-Einträge vorher gespeichert. Ihre Zeit geht nicht mehr verloren. Nur ein Absturz oder ein beendeter Prozess kann den laufenden Abschnitt seit der letzten Pause noch kosten.")),

            new ChangelogRelease("2026.47.3", new DateTime(2026, 11, 16), "SAP-Verrechnung zum Kopieren",
                Neu("SAP-Verrechnung: Ein Klick auf einen Kostenträger kopiert seine Bezeichnung in die Zwischenablage, ein Klick auf die Stunden kopiert sie als „h:mm“. Das geht in der Monatstabelle und in den Tageskarten unter „Im SAP buchen“. Die Fläche färbt sich kurz ein, damit du siehst, dass kopiert wurde."),
                Neu("Zusätzlich gilt beim Kostenträger: Rechtsklick kopiert den ganzen Text samt „(KTR-ID)“, Mausradklick nur die ID (nur wenn der Kostenträger eine ID in Klammern hat). Bei den Stunden kopiert der Rechtsklick sie mit Einheit, zum Beispiel „0:49 h“. Der Tooltip beim Überfahren nennt, was welcher Klick kopiert."),
                Verbessert("Beim Linksklick fällt die Kostenträger-ID weg: Aus „Bezeichnung (1500420C26FX001.01)“ wird „Bezeichnung“, denn die ID trägst du im SAP in ein eigenes Feld ein. Kostenträger ohne ID in Klammern kopiert TimeTrack unverändert.")),

            new ChangelogRelease("2026.47.2", new DateTime(2026, 11, 12), "Tickets im Quick Switch",
                Neu("Quick Switch hat zwei Modi: „Projekte“ wie bisher und „Tickets“. Im Modus „Tickets“ wählst du ein To-Do-Ticket; die Buchung startet sofort und läuft auf das Projekt, zu dessen Board das Ticket gehört. Als Buchungstext steht der Titel des Tickets statt „Quick Select“. TimeTrack merkt sich den Modus."),
                Neu("Welche Tickets der Quick Switch anbietet, legst du unter „To-Do“ fest: Im Editor einer Spalte (Stift-Symbol) schaltest du „Tickets im Quick Switch anbieten“ ein. Ein Blitz im Spaltenkopf zeigt, welche Spalten es sind. Neue Boards und bestehende Spalten „To Do“ eines Projekts bieten ihre Tickets von Anfang an an."),
                Neu("Die Liste der Tickets passt sich sofort an, wenn du ein Ticket anlegst, verschiebst, umbenennst oder löschst, oder ein Projekt umbenennst oder löschst."),
                Verbessert("Auf ein Projekt läuft immer nur eine Buchung. Ist es schon aktiv, nennt das Quick-Switch-Fenster den Grund, statt nichts zu tun. Die Liste im Fenster heißt jetzt „Aktive Buchungen“.")),

            new ChangelogRelease("2026.47.1.1", new DateTime(2026, 11, 12), "",
                Behoben("To-Do und Notizen: Nach dem Speichern eines Tickets sprang die Auswahl bei „Board von“ auf leer. Das Projekt bleibt jetzt gewählt; dasselbe gilt für die Projektwahl und den Filter bei den Notizen.")),

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
