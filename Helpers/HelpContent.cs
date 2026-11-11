using System.Collections.Generic;

namespace Zeitmanagement.Helpers
{
    internal sealed class HelpTopic
    {
        public HelpTopic(string glyph, string title, string page, string summary, params string[] steps)
        {
            Glyph = glyph;
            Title = title;
            Page = page;
            Summary = summary;
            Steps = steps;
        }

        /// <summary>Icon glyph (Segoe Fluent Icons / Segoe MDL2 Assets), usually the one of the page in the navigation.</summary>
        public string Glyph { get; }
        public string Title { get; }
        /// <summary>Page key as passed to MainViewModel.SelectViewCommand, or null if the topic has no page of its own.</summary>
        public string Page { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Steps { get; }
    }

    /// <summary>
    /// The help topics shown on the "Hilfe &amp; Neuigkeiten" page, in the order of the navigation.
    /// Kept up to date with every feature, together with <see cref="Changelog"/>.
    /// </summary>
    internal static class HelpContent
    {
        public static readonly IReadOnlyList<HelpTopic> Topics = new[]
        {
            new HelpTopic("", "Erste Schritte", null,
                "In drei Schritten zur ersten Buchung.",
                "Lege unter „Projekte“ deine Projekte an – mit Kunde und Kostenträger, und als „extern“ markiert, wenn der Kunde die Zeit bezahlt.",
                "Öffne in der Seitenleiste „Quick Switch“ und wähle ein Projekt. Die Buchung startet, sobald es gewählt ist.",
                "Wechsle zwischen Projekten mit Pause, Stop und Start. Jede Buchung landet automatisch in der „Stundenbuchung“, wo du sie korrigieren oder ergänzen kannst.",
                "Für den Alltag praktisch: Das kleine Quick-Switch-Fenster kann immer sichtbar bleiben."),

            new HelpTopic("", "Dashboard", "Dashboard",
                "Dein Tag und dein Jahr auf einen Blick.",
                "Oben siehst du die heute gebuchte Zeit, deinen Durchschnitt der letzten 5 Werktage und dein Top-Projekt der letzten 30 Tage.",
                "Die Heatmap zeigt die Aktivität der letzten 12 Monate. Ein Klick auf einen Tag öffnet ihn in der Stundenbuchung.",
                "Der Vergleich übersetzt deine Arbeitszeit in etwas Greifbares. Der Würfel holt einen neuen Vergleich."),

            new HelpTopic("", "Quick Switch", null,
                "Das kleine Fenster zum Starten, Pausieren und Stoppen von Buchungen – bis zu 4 Projekte gleichzeitig.",
                "Öffnen über „Quick Switch“ in der Seitenleiste, über „Quick Switch öffnen“ im Menü des Tray-Symbols oder per Klick auf einen Hinweis des Tray-Symbols. In den Einstellungen kann es auch beim Programmstart aufgehen.",
                "„Projekt starten“ bietet alle Projekte an, auch neu angelegte – außer denen, die du unter „Projekte“ für Quick Switch ausgeschaltet hast. Die Buchung startet, sobald du eins wählst.",
                "Jedes laufende Projekt hat eigene Knöpfe: Pause beendet den laufenden Abschnitt und hält das Projekt frei, Start setzt es fort, Stop beendet die Buchung ganz.",
                "Jeder beendete Abschnitt wird sofort als Buchung gespeichert. Die Laufzeit zählt über Pausen hinweg weiter, bis du stoppst.",
                "„Vordergrund“ hält das Fenster über allen anderen Fenstern.",
                "Teilst du in Teams deinen Bildschirm, bekommt das Fenster einen roten Rahmen – es ist dann im Meeting zu sehen."),

            new HelpTopic("", "Stundenbuchung", "Hours",
                "Alle Buchungen eines Tages ansehen, nachtragen und korrigieren.",
                "Mit den Pfeilen wechselst du den Tag, „Heute“ springt zurück zum aktuellen Tag. Ein Datum lässt sich auch direkt eingeben.",
                "Neue Buchung: Projekt wählen, optional eine Beschreibung, Von und Bis als HH:mm eintragen, dann „Hinzufügen“.",
                "Ein Klick auf eine Buchung im Zeitstrahl öffnet sie zum Bearbeiten oder Löschen.",
                "Der Arbeitstag zeigt Beginn und Ende, Pausen und in Klammern die real gearbeitete Zeit, bei der parallele Buchungen nur einmal zählen.",
                "Überschneiden sich Buchungen, weist ein Hinweis darauf hin. Unten siehst du die Stunden je Projekt und je Kostenträger."),

            new HelpTopic("", "Fragmentierung", "Fragmentation",
                "Wie zersplittert war dein Tag?",
                "Jeder Balken zeigt einen Tag, jeder Abschnitt eine Buchung. Viele kurze Abschnitte bedeuten viele Wechsel.",
                "Oben wählst du, ob du einen Tag, eine Woche oder einen Monat siehst, mit den Pfeilen blätterst du weiter.",
                "Ein Klick auf einen Abschnitt zeigt Projekt, Zeit und Dauer."),

            new HelpTopic("", "SAP-Verrechnung", "Sap",
                "Was muss am Monatsende wirklich im SAP gebucht werden?",
                "Externe Projekte (vom Kunden bezahlt) decken interne Zeit ab, die parallel lief – diese interne Zeit entfällt im SAP.",
                "Laufen mehrere externe Projekte parallel, entsteht Überschuss. Er wird bis zum Monatsende mit späterer freier interner Zeit verrechnet.",
                "Laufen ohne externes Projekt mehrere interne parallel, zählt nur das Projekt mit weniger Stunden an diesem Tag.",
                "Die Tabelle zeigt pro Kostenträger, was im SAP zu buchen ist. Mit den Pfeilen wechselst du den Monat.",
                "Ob ein Projekt extern ist, legst du unter „Projekte“ fest."),

            new HelpTopic("", "Projektcontrolling", "Controlling",
                "SAP-Kosten zu deinen Kostenträgern ansehen – getrennt von deinen gebuchten Stunden.",
                "„Excel importieren“ liest eine SAP-Projektcontrolling-Datei (.xlsx). Importiert werden nur PSP-Elemente, die in einem Kostenträger deiner Projekte enthalten sind: Der Kostenträger muss die PSP-Element-ID enthalten, aber nicht genau so heißen. Es werden keine Projekte, Kostenträger oder Buchungen angelegt oder verändert.",
                "Die Vorschau zeigt vor dem Speichern, was übernommen wird, samt Warnungen und Fehlern mit Blatt und Zelle. Der SAP-Stand (Datum und Uhrzeit der Daten) kommt aus der Datei; fehlt er, gibst du ihn an. Die Währung ist optional – ohne Angabe bleibt sie „nicht angegeben“.",
                "Die Datei enthält Geldbeträge (Plan und Ist), keine Stunden. Ein fehlender Betrag heißt „Nicht geliefert“ und ist nicht null. Rest ist Plan minus Ist und kann negativ sein; „Budget überschritten“ steht da, wenn Ist den positiven Plan übersteigt.",
                "Die erste Spalte „Stand“ zeigt den Budgetverbrauch in Prozent (Ist ÷ Plan) mit einem kleinen Balken und bleibt beim Scrollen nach rechts stehen. Ab 90 % wechselt der Balken die Farbe, bei einer Überschreitung ist er voll und die Zahl nennt den echten Wert. Ohne positiven Plan steht dort stattdessen die Begründung, z. B. „Kein positives Budget“.",
                "„Diagramm“ (neben „Tabelle“) zeigt den Stand je PSP-Element über die SAP-Daten: X ist das SAP-Datum, Y entweder relativ in Prozent des Plans (100 % = Plan) oder in Stunden. In Stunden erscheinen Ist (aus Geld umgerechnet, braucht den Stundensatz des Projekts) und für die gewählte Zeile zusätzlich Plan in Stunden sowie deine lokal gebuchten Stunden, über die Zeit aufsummiert. Die SAP-Werte sind schon kumuliert bis zum jeweiligen Stand; die Punkte sind die importierten Stände, ein einzelner Import ergibt einen einzelnen Punkt.",
                "Im Diagramm zeigt die Maus über einem Punkt den Stand dort, daneben an der Senkrechten alle Werte zum nächsten SAP-Datum (lokal gebuchte Stunden als Stand bis zu diesem Tag). Ein Klick auf einen Punkt oder die Legende wählt die Zeile. Es werden höchstens 10 Zeilen gezeichnet (die mit dem höchsten Stand, die gewählte immer); mit Suche und Filtern grenzt du ein. Der Knopf „Kostenträger“ (n/m) öffnet eine Liste mit Häkchen, um einzelne Kostenträger im Diagramm ein- oder auszublenden – mit Suche sowie „Alle einblenden“ und „Alle ausblenden“ für die gerade gefundenen. Sind Kostenträger gezielt ausgeblendet, zeigt das Diagramm bis zu 16 Linien. Die Tabelle bleibt für die genauen Werte.",
                "Stundensatz: Er gehört zum Projekt, nicht zu TimeTrack insgesamt. In den Details trägst du ihn je lokalem Projekt ein (gilt für die Währung des Imports). Damit rechnet TimeTrack Plan, Ist und Rest zusätzlich in rechnerische Stunden um (Betrag ÷ Stundensatz, Spalten „… (h, umger.)“). Haben Projekte am selben Kostenträger unterschiedliche Sätze, steht „Satz uneinheitlich“ – dann wird nicht umgerechnet. Das ist eine Schätzung aus Geld, keine gebuchte Zeit; ein leeres Feld schaltet die Umrechnung für das Projekt aus. Mit „Berechnete Stunden (h) zeigen“ blendest du alle umgerechneten Stunden aus oder wieder ein; die Wahl bleibt gespeichert.",
                "Jeder Import ist ein Stand. Der Kostenverlauf zeigt die Stände nach SAP-Datum; die Differenz ist die Veränderung zwischen zwei Ständen, keine Buchung pro Tag oder Monat. Ein einzelner Import ergibt einen einzelnen Punkt.",
                "Derselbe Stand wird nicht doppelt gespeichert. Eine Korrektur mit gleichem SAP-Stand kommt als neue Revision dazu und ersetzt in der Anzeige die ältere; gelöscht wird sie über „Import löschen“.",
                "„Lokaler Stundenverlauf“ zeigt die in TimeTrack gebuchten Stunden der verknüpften Projekte je Tag, Woche oder Monat – nicht aus SAP und nicht damit abgestimmt. Parallele Buchungen zählen mehrfach.",
                "„Letzter bekannter Stand“ zeigt Kostenträger, die im gewählten Import fehlen, mit ihrem eigenen, älteren Datum.",
                "Die importierten Daten liegen in der TimeTrack-Datenbank und sind in Sicherungen enthalten. Die Excel-Datei selbst wird nicht behalten oder verändert."),

            new HelpTopic("", "Tätigkeitsnachweis", "Export",
                "Buchungen eines Projekts und Zeitraums als PDF oder CSV exportieren, als Nachweis oder für die Abrechnung.",
                "Links wählst du den Zeitraum (Schnellwahl wie „Letzter Monat“ oder Von/Bis) und mindestens ein Projekt. Mit Suche und Kunden-Auswahl findest du Projekte schnell; „Alle gezeigten wählen“ nimmt alle Projekte der Liste. Unter jedem Projekt steht, wie viele Buchungen und Stunden im Zeitraum liegen. Es wird nur exportiert, was du wählst.",
                "Rechts siehst du vor dem Export, was im Nachweis steht: Gesamtzeit, Buchungen, Arbeitstage, Durchschnitt und jede Buchung. Warnungen weisen auf Buchungen ohne Beschreibung, Überschneidungen im selben Projekt, nicht lesbare Zeiten (sie fehlen im Nachweis), Dauer 0 und Tage über 12 Stunden hin. Betroffene Zeilen sind hinterlegt.",
                "Inhalt: Gliederung nach Tag, Projekt oder Kalenderwoche mit Zwischensummen; bei „Nach Tag“ lässt sich die Tagessumme (unter Tagen mit mehreren Buchungen) mit „Tagessumme zeigen“ abschalten. Die Dauer zeigst du dezimal (1,50), als h:mm (1:30) oder beides. Uhrzeiten, Projekt- und Kostenträger-Spalte schaltest du ein und aus. „Extern“ oder „Intern“ begrenzt die Projekte nach dem Kennzeichen unter „Projekte“.",
                "Rundung: Standard ist „Nicht runden“, jede Minute zählt. Nur wenn du es ausdrücklich wählst, wird die Dauer jeder Buchung kaufmännisch auf 5, 10, 15 oder 30 Minuten gerundet; das Dokument nennt dann die ungerundete Summe, damit nichts stillschweigend abweicht. Die Rundung wird nie gemerkt, jeder Nachweis beginnt wieder ohne. Für die genaue Minute wähle bei der Dauer „h:mm“ oder „Beides“: Dezimalstunden sind auf zwei Stellen gerundet (10 Minuten sind 0,17 h). Jede Buchung zählt einzeln; läuft Zeit parallel in mehreren Projekten, zählt sie in jedem voll. Eine Buchung über Mitternacht zählt am Tag ihres Beginns.",
                "„PDF speichern“ erzeugt ein A4-Dokument mit Titel und Zeitraum, Auftragnehmer und Auftraggeber, Zusammenfassung, Einzelnachweis mit Gesamtsumme und Bemerkung. Die Kopfzeile der Tabelle wiederholt sich auf jeder Seite, die Fußzeile nennt die Seitenzahl. Unter „Dokument“ ändern Titel, Bemerkung, Querformat und Fußzeile das Aussehen.",
                "„Auftragnehmer & Auftraggeber“ nimmt die Angaben für den Briefkopf auf. Dein Name und deine Anschrift bleiben für den nächsten Nachweis gespeichert; Beides ist optional: Der Auftragnehmer steht nur im Dokument, wenn du ihn einträgst, der Auftraggeber nur mit „Auftraggeber im Dokument zeigen“ (vorbelegt mit dem Kunden der gewählten Projekte). Der Kostenträger steht immer im Dokument. Auftrags-/Bestellnummer, Auftraggeber und Anschrift merkt sich TimeTrack für das Projekt, sobald du einen Nachweis mit genau einem Projekt gespeichert hast, und füllt sie beim nächsten Mal wieder ein. Ein Logo (PNG oder JPEG) steht oben rechts im PDF; die Datei bleibt dort, wo sie liegt.",
                "„PDF-Vorschau“ öffnet den Nachweis als temporäre Datei im PDF-Programm, ohne zu speichern und ohne etwas zu merken. Die letzte gespeicherte Auswahl (Optionen, Projekte, Zeitraum-Schnellwahl) ist beim nächsten Öffnen wieder da; „Letzter Monat“ gilt dann für den dann letzten Monat.",
                "„CSV speichern“ schreibt eine Zeile je Buchung (Datum, Wochentag, Von, Bis, Dauer, Projekt, Kunde, Kostenträger, extern/intern, Beschreibung) als UTF-8-Datei. Standard sind Semikolon und Dezimalkomma, wie das deutsche Excel sie erwartet; „International“ nimmt Komma und Dezimalpunkt. Beginnt ein Text mit = + - oder @, setzt TimeTrack ein Hochkomma davor, damit Excel ihn nicht als Formel ausführt.",
                "Nach dem Speichern öffnest du die Datei oder ihren Ordner direkt aus der Seite. Ist die Datei noch in einem PDF-Programm oder Excel geöffnet, meldet TimeTrack das und schreibt nichts Halbes."),

            new HelpTopic("", "Projekte", "Projects",
                "Projekte anlegen, bearbeiten und auswerten.",
                "„Neues Projekt“ öffnet das Formular: Projektname, Kunde, Kostenträger und ob das Projekt extern ist.",
                "Über die Symbole in einer Zeile bearbeitest oder löschst du ein Projekt.",
                "Der Schalter „Quick Switch“ bestimmt, ob ein Projekt im Quick-Switch-Fenster wählbar ist. Neue Projekte sind sofort dabei, Buchungen bleiben auch bei ausgeschaltetem Projekt erhalten.",
                "Die Suche filtert nach Name, Kunde und Kostenträger. Mit Von/Bis und „Filtern“ siehst du die Stunden in einem Zeitraum.",
                "Das Diagramm-Symbol in einer Zeile öffnet das „Projektcontrolling“ mit dem Kostenträger des Projekts.",
                "Das Dokument-Symbol in einer Zeile öffnet den „Tätigkeitsnachweis“ mit diesem Projekt."),

            new HelpTopic("", "Einstellungen", "Settings",
                "TimeTrack an dich anpassen. Das Design gilt sofort, alles andere nach „Speichern“.",
                "Design: Motiv und Helligkeit (wie Windows, hell, dunkel) wählen. Saisonale Designs erscheinen rund um Halloween, Weihnachten und Ostern von selbst, wenn du das nicht abschaltest.",
                "Start & Fenster: mit Windows starten, normal oder minimiert starten, auf Wunsch gleich mit Quick Switch.",
                "Automatisierung: Buchungen bei Sperrbildschirm oder Ruhemodus nach einer Wartezeit beenden und erinnern lassen, wenn kein Projekt läuft.",
                "Updates: TimeTrack sucht regelmäßig nach neuen Versionen. „Installieren“ spielt ein Update ein, die App startet danach neu. Alle Versionen und eine Anleitung stehen auch auf github.com/jkkSofting/TimeTrack-Releases.",
                "Datensicherung: Sicherungen erstellen und eine vorhandene Sicherung wiederherstellen."),

            new HelpTopic("", "Tray-Symbol & Hinweise", null,
                "TimeTrack läuft im Infobereich der Taskleiste weiter.",
                "Ein Doppelklick auf das Tray-Symbol öffnet das Hauptfenster, das Kontextmenü bietet „Öffnen“, „Quick Switch öffnen“ und „Beenden“.",
                "Hinweise erscheinen, wenn eine Buchung automatisch beendet wurde, wenn länger kein Projekt läuft oder wenn ein Update bereitsteht.",
                "Ein Klick auf einen Hinweis öffnet das Passende, meist den Quick Switch, bei Updates die Einstellungen."),

            new HelpTopic("", "Der Pinguin", "Settings",
                "Das Maskottchen von TimeTrack.",
                "Ab und zu schaut der Pinguin hinter der Taskleiste hervor. Klick ihn rechtzeitig an, dann wirft er dir einen Fisch zu – und den musst du erwischen, bevor er herunterrutscht.",
                "Wie oft er auftaucht, stellst du in den Einstellungen ein. Dort steht auch deine Fangstatistik.",
                "Zu jedem Design trägt er etwas Passendes."),
        };
    }
}
