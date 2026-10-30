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
                "Wähle im „Quick Select“ die Projekte, an denen du regelmäßig arbeitest. Die Buchung startet, sobald ein Projekt gewählt ist.",
                "Wechsle zwischen Projekten mit Pause, Stop und Start. Jede Buchung landet automatisch in der „Stundenbuchung“, wo du sie korrigieren oder ergänzen kannst.",
                "Für den Alltag praktisch: das kleine Quick-Switch-Fenster, das immer sichtbar bleibt."),

            new HelpTopic("", "Dashboard", "Dashboard",
                "Dein Tag und dein Jahr auf einen Blick.",
                "Oben siehst du die heute gebuchte Zeit, deinen Durchschnitt der letzten 5 Werktage und dein Top-Projekt der letzten 30 Tage.",
                "Die Heatmap zeigt die Aktivität der letzten 12 Monate. Ein Klick auf einen Tag öffnet ihn in der Stundenbuchung.",
                "Der Vergleich übersetzt deine Arbeitszeit in etwas Greifbares. Der Würfel holt einen neuen Vergleich."),

            new HelpTopic("", "Quick Select", "QuickSelect",
                "Projekte starten, pausieren und stoppen – bis zu 4 gleichzeitig.",
                "Wähle in einer Zeile ein Projekt: Die Buchung startet sofort. Darunter erscheint automatisch eine neue, leere Zeile.",
                "Start setzt eine Buchung fort, Pause beendet den laufenden Abschnitt und hält die Zeile frei, Stop beendet die Buchung ganz.",
                "Jeder beendete Abschnitt wird sofort als Buchung gespeichert. Die Laufzeit zählt über Pausen hinweg weiter, bis du stoppst.",
                "Solange eine Zeile läuft, lässt sich ihr Projekt nicht ändern. Mit dem X entfernst du eine Zeile.",
                "„Quick Switch öffnen“ zeigt das kleine Schnellwechsel-Fenster."),

            new HelpTopic("", "Quick Switch", "QuickSelect",
                "Das kleine Fenster für den schnellen Projektwechsel nebenbei.",
                "Öffnen über „Quick Switch öffnen“ im Quick Select oder per Klick auf einen Hinweis des Tray-Symbols. In den Einstellungen kann es auch beim Programmstart aufgehen.",
                "„Projekt starten“ bietet die Projekte an, die du im Quick Select eingerichtet hast. Die Buchung startet, sobald du eins wählst.",
                "Jedes laufende Projekt hat eigene Knöpfe zum Pausieren, Fortsetzen und Stoppen.",
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

            new HelpTopic("", "Projekte", "Projects",
                "Projekte anlegen, bearbeiten und auswerten.",
                "„Neues Projekt“ öffnet das Formular: Projektname, Kunde, Kostenträger und ob das Projekt extern ist.",
                "Über die Symbole in einer Zeile bearbeitest oder löschst du ein Projekt.",
                "Die Suche filtert nach Name, Kunde und Kostenträger. Mit Von/Bis und „Filtern“ siehst du die Stunden in einem Zeitraum."),

            new HelpTopic("", "Einstellungen", "Settings",
                "TimeTrack an dich anpassen. Das Design gilt sofort, alles andere nach „Speichern“.",
                "Design: Motiv und Helligkeit (wie Windows, hell, dunkel) wählen. Saisonale Designs erscheinen rund um Halloween, Weihnachten und Ostern von selbst, wenn du das nicht abschaltest.",
                "Start & Fenster: mit Windows starten, normal oder minimiert starten, auf Wunsch gleich mit Quick Switch.",
                "Automatisierung: Buchungen bei Sperrbildschirm oder Ruhemodus nach einer Wartezeit beenden und erinnern lassen, wenn kein Projekt läuft.",
                "Updates: TimeTrack sucht regelmäßig nach neuen Versionen. „Installieren“ spielt ein Update ein, die App startet danach neu.",
                "Datensicherung: Sicherungen erstellen und eine vorhandene Sicherung wiederherstellen."),

            new HelpTopic("", "Tray-Symbol & Hinweise", null,
                "TimeTrack läuft im Infobereich der Taskleiste weiter.",
                "Ein Doppelklick auf das Tray-Symbol öffnet das Hauptfenster, das Kontextmenü bietet „Öffnen“ und „Beenden“.",
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
