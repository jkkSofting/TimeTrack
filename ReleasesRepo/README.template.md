<!--
  Diese Datei wird nicht von Hand gepflegt: Im Quell-Repo erzeugt Scripts\Update-ReleasesReadme.ps1 sie aus
  ReleasesRepo\README.template.md und traegt Version und Changelog aus Helpers\Changelog.cs ein.
  Aenderungen direkt hier gehen beim naechsten Lauf verloren.
-->

# TimeTrack

**Zeiterfassung für Windows.** Projekte anlegen, Stunden buchen, auswerten – und mit einem Klick zwischen Projekten wechseln. TimeTrack läuft unauffällig im Infobereich der Taskleiste.

Aktuelle Version: **{{LATEST_VERSION}}** &nbsp;·&nbsp; [**Zum Download**](https://github.com/jkkSofting/TimeTrack-Releases/releases/latest)

Dieses Repository enthält die fertigen Installer (unter [Releases](https://github.com/jkkSofting/TimeTrack-Releases/releases)), die Anleitung und die Versionshistorie von TimeTrack.

**Inhalt:** [Installation](#installation) · [Updates](#updates) · [Erste Schritte](#erste-schritte) · [Funktionen](#funktionen) · [Deine Daten](#deine-daten) · [Deinstallation](#deinstallation) · [Fragen und Probleme](#fragen-und-probleme) · [Changelog](#changelog)

## Installation

**Voraussetzungen:** Windows 10 (ab Version 1903) oder Windows 11. Das benötigte .NET Framework 4.8 ist dort bereits enthalten.

1. Öffne die [neueste Version](https://github.com/jkkSofting/TimeTrack-Releases/releases/latest) und lade unter **Assets** die Datei `TimeTracker-<Version>.exe` herunter.
2. Starte die heruntergeladene Datei. Windows fragt einmal nach der Administratorfreigabe (UAC), weil TimeTrack nach `Programme` installiert wird.
   Der Installer ist nicht digital signiert, deshalb zeigt Windows SmartScreen unter Umständen „Der Computer wurde durch Windows geschützt“. Wähle dann **Weitere Informationen → Trotzdem ausführen**.
3. Folge dem Installationsassistenten. Optional kannst du ein Desktop-Symbol anlegen lassen und festlegen, dass TimeTrack mit Windows startet.
4. Am Ende startet TimeTrack. Beim ersten Start legt es seine Datenbank selbst an.

## Updates

TimeTrack aktualisiert sich selbst – du musst nichts von Hand herunterladen.

- Die App sucht kurz nach dem Start und danach alle 10 Minuten nach einer neueren Version. Gibt es eine, erscheint ein Hinweis am Tray-Symbol.
- **Einstellungen → Updates → Installieren** lädt die neue Version herunter und spielt sie im Hintergrund ein. TimeTrack beendet sich dafür kurz und startet danach von selbst wieder. Windows fragt dabei einmal nach der Administratorfreigabe.
- „Nach Updates suchen“ prüft sofort, ohne auf die nächste Runde zu warten. Die automatische Suche lässt sich in den Einstellungen abschalten.
- Einstellungen und Buchungen bleiben bei einem Update erhalten.
- Du kannst auch jederzeit den neuesten Installer von Hand herunterladen und über die vorhandene Installation spielen.

Die Updates kommen aus den [Releases](https://github.com/jkkSofting/TimeTrack-Releases/releases) dieses Repositories. Ältere Versionen (bis 2026.45.0) haben früher in einem anderen Repository gesucht und wechseln mit dem nächsten Update von selbst hierher – dafür ist nichts zu tun.

## Erste Schritte

1. Lege unter **Projekte** deine Projekte an – mit Kunde und Kostenträger, und als „extern“ markiert, wenn der Kunde die Zeit bezahlt.
2. Öffne in der Seitenleiste **Quick Switch** und wähle ein Projekt: Die Buchung startet sofort. Mit Pause, Stop und Start wechselst du zwischen Projekten.
3. Jede Buchung landet automatisch in der **Stundenbuchung**, wo du sie korrigieren oder nachtragen kannst.
4. Für den Alltag praktisch: Das kleine **Quick-Switch-Fenster** kann immer sichtbar bleiben.

Eine ausführlichere Erklärung aller Bereiche findest du in TimeTrack selbst unter **Hilfe & Neuigkeiten**.

## Funktionen

| Bereich | Wofür |
| --- | --- |
| **Dashboard** | Heute gebuchte Zeit, Durchschnitt der letzten Werktage, Top-Projekt und eine Aktivitäts-Heatmap der letzten 12 Monate. |
| **Quick Switch** | Kleines Fenster zum Starten, Pausieren und Stoppen von Projekten – bis zu 4 gleichzeitig, auf Wunsch immer sichtbar. Jeder beendete Abschnitt wird sofort als Buchung gespeichert. Bekommt einen roten Rahmen, solange du in Teams deinen Bildschirm teilst. |
| **Stundenbuchung** | Alle Buchungen eines Tages als Zeitstrahl ansehen, nachtragen und korrigieren. Zeigt Pausen (auch die gesetzliche Pause) und die real gearbeitete Zeit. |
| **Fragmentierung** | Wie zersplittert war dein Tag, deine Woche oder dein Monat? |
| **SAP-Verrechnung** | Was am Monatsende wirklich im SAP gebucht werden muss: Externe Zeit deckt parallele interne Zeit ab. |
| **Projektcontrolling** | SAP-Plan- und Ist-Kosten aus einer Excel-Datei zu deinen Kostenträgern ansehen (nur lesend), mit Kostenverlauf nach SAP-Stand. Mit deinem Stundensatz lassen sich die Beträge auch als rechnerische Stunden anzeigen. Daneben der lokale Stundenverlauf aus deinen Buchungen – getrennt von den Geldbeträgen. |
| **Projekte** | Projekte anlegen, bearbeiten und auswerten; festlegen, welche im Quick Switch wählbar sind. |
| **Einstellungen** | Design (hell/dunkel, mehrere Motive, saisonale Designs), Programmstart, Automatisierung, Updates und Datensicherung. |
| **Tray-Symbol** | TimeTrack läuft im Infobereich weiter und meldet sich, wenn eine Buchung automatisch beendet wurde, länger kein Projekt läuft oder ein Update bereitsteht. |
| **Der Pinguin** | Das Maskottchen von TimeTrack. Klick ihn rechtzeitig an, dann wirft er dir einen Fisch zu. |

## Deine Daten

- Die Buchungen liegen lokal in einer Datenbank: `C:\ProgramData\Kaltenmark-Engineering\TimeTracker\timetracker.db`. TimeTrack schickt keine Daten ins Netz; Verbindung zum Internet braucht nur die Suche nach Updates.
- **Datensicherung:** Unter **Einstellungen → Datensicherung** erstellst du Sicherungen und spielst eine vorhandene wieder ein. Sie liegen im Ordner `Backups` neben der Datenbank (`*.timetrack-backup`). Fehlt beim Start die Datenbank, bietet TimeTrack an, die neueste Sicherung zu laden.
- Das **Projektcontrolling** speichert importierte SAP-Kosten ebenfalls in dieser Datenbank (und damit in Sicherungen). Die Excel-Datei wird nur gelesen, nicht verändert oder behalten, und es werden nur Zeilen zu deinen eigenen Kostenträgern übernommen.
- Updates und Deinstallation löschen die Datenbank nicht.

## Deinstallation

**Windows-Einstellungen → Apps → Installierte Apps → TimeTracker → Deinstallieren.** Deine Daten bleiben dabei bewusst erhalten. Wenn du sie nicht mehr brauchst, kannst du den Ordner `C:\ProgramData\Kaltenmark-Engineering\TimeTracker` anschließend selbst löschen.

## Fragen und Probleme

- **Windows blockiert den Installer** → siehe Hinweis zu SmartScreen unter [Installation](#installation).
- **Das Update lässt sich nicht installieren** → lade den neuesten Installer von Hand herunter und starte ihn; er aktualisiert die vorhandene Installation.
- **Die Datenbank ist weg** → starte TimeTrack neu und lade die angebotene Sicherung, oder spiele eine Sicherung unter Einstellungen → Datensicherung ein.
- Weitere Fragen: [Kontakt und Support](https://sites.google.com/view/janick-kaltenmark/startseite).

## Changelog

Die Einträge entsprechen der Seite **Hilfe & Neuigkeiten** in der App. Die neueste Version steht oben.

{{CHANGELOG}}

---

© Janick Kaltenmark 2026 · Kaltenmark-Engineering
