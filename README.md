# TimeTrack

**Zeiterfassung für Windows.** Projekte anlegen, Stunden buchen, auswerten und mit einem Klick zwischen Projekten
wechseln. TimeTrack läuft im Infobereich der Taskleiste und speichert alles lokal.

Dieses Repository enthält den **Quellcode**. Installer, Anleitung für Anwender und Versionshistorie liegen im
Release-Repository: [**jkkSofting/TimeTrack-Releases**](https://github.com/jkkSofting/TimeTrack-Releases)
([neueste Version](https://github.com/jkkSofting/TimeTrack-Releases/releases/latest)).

**Inhalt:** [Funktionen](#funktionen) · [Technik](#technik) · [Aufbau](#aufbau) · [Bauen und starten](#bauen-und-starten) ·
[Tests](#tests) · [Daten](#daten) · [Plugins](#plugins) · [Veröffentlichen](#veröffentlichen) · [Dokumentation](#dokumentation)

## Funktionen

| Bereich | Wofür |
| --- | --- |
| Dashboard | Heute gebuchte Zeit, Durchschnitt der letzten Werktage, Top-Projekt, Aktivitäts-Heatmap der letzten 12 Monate |
| Quick Switch | Kleines Fenster zum Starten, Pausieren und Stoppen von bis zu 4 Projekten gleichzeitig; jeder beendete Abschnitt wird sofort gebucht |
| Stundenbuchung | Buchungen eines Tages als Zeitstrahl ansehen, nachtragen, korrigieren; Pausen und real gearbeitete Zeit |
| Fragmentierung | Wie zersplittert Tag, Woche oder Monat waren |
| SAP-Verrechnung | Was am Monatsende im SAP gebucht werden muss (externe Zeit deckt parallele interne Zeit ab) |
| Projektcontrolling | SAP-Plan- und Ist-Kosten aus einer Excel-Datei zu den eigenen Kostenträgern, lokaler Stundenverlauf |
| Tätigkeitsnachweis | Buchungen eines Zeitraums als PDF oder CSV |
| Projekte | Projekte mit Kunde, Kostenträger und intern/extern pflegen und auswerten |
| Einstellungen | Design (hell/dunkel, Motive, saisonal), Programmstart, Automatisierung, Updates, Datensicherung, Plugins |
| Plugins | Erweiterungen mit eigenen Seiten, siehe [Plugins](#plugins) |
| Tray-Symbol | Hinweise bei automatisch beendeten Buchungen, fehlender Buchung und Updates |

Die Hilfe in der App (Seite „Hilfe & Neuigkeiten“) kommt aus `Helpers/HelpContent.cs`, die Neuigkeiten aus
`Helpers/Changelog.cs`. Beide werden mit jedem Feature gepflegt.

## Technik

| | |
| --- | --- |
| Oberfläche | WPF, MVVM (`MVVM/BindableBase`, `DelegateCommand`) |
| Plattform | .NET Framework 4.8, C# 7.3, altes Projektformat (`Zeitmanagement.csproj` mit expliziten Dateieinträgen, `packages.config`) |
| Datenbank | SQLite (`System.Data.SQLite`), eine Verbindung auf dem UI-Thread (`MainViewModel.DbInstance`) |
| Berichte | PDFsharp/MigraDoc (PDF), Open-XML-SDK (Excel lesen) |
| Installer | Inno Setup (`Installer/installer.iss`) |
| Updates | `Helpers/UpdateChecker.cs` / `UpdateService.cs` fragen die Releases von `TimeTrack-Releases` ab |

Neue Quelldateien müssen von Hand in `Zeitmanagement.csproj` eingetragen werden (kein Globbing).

## Aufbau

```
Zeitmanagement.sln                    Anwendung + Plugin-Vertrag
Zeitmanagement.csproj                 Die Anwendung (WinExe "Zeitmanagement.exe", Produktname TimeTrack)
├── App.xaml(.cs), MainWindow.xaml    Start, Tray, Navigation
├── TimeTracker*.cs                   Datenzugriff (partielle Klasse je Bereich: Zeiterfassung, Controlling, Export, Plugins)
├── ViewModel/                        Ein ViewModel je Seite
├── Views/                            XAML-Ansichten, Konverter, Diagramme
├── Helpers/                          Fachlogik und Dienste (Export, Controlling, Backup, Updates, Themes …)
│   └── Plugins/                      Plugin-Lader, Host, Ablage, Verwaltung
├── Themes/                           Steuerelement-Stile und Farbpaletten
├── Installer/                        Inno-Setup-Skript und Texte
└── Scripts/                          Release-Skripte
TimeTrack.Plugins.Abstractions/       Plugin-Vertrag (eigene DLL, siehe Plugins)
Samples/TimeTrack.Plugin.Sample/      Beispiel-Plugin (nicht in der Solution)
Zeitmanagement.Tests/                 Tests (nicht in der Solution, siehe Tests)
ReleasesRepo/                         Vorlage der README im Release-Repo
```

## Bauen und starten

Voraussetzung: Visual Studio 2022 oder neuer mit der Workload „.NET-Desktopentwicklung“ (für das alte WPF-Projektformat
braucht es MSBuild aus Visual Studio, `dotnet build` reicht für die Anwendung nicht).

In Visual Studio `Zeitmanagement.sln` öffnen und starten. Auf der Kommandozeile:

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild Zeitmanagement.sln /t:Restore /p:RestorePackagesConfig=true   # NuGet-Pakete nach packages\
& $msbuild Zeitmanagement.sln /p:Configuration=Release                  # bin\Release\Zeitmanagement.exe
```

Achtung: Ein Debug-Start arbeitet mit der echten Datenbank unter `C:\ProgramData\…` (siehe [Daten](#daten)).

## Tests

```powershell
dotnet test Zeitmanagement.Tests
```

xUnit auf .NET Framework 4.8. Das Testprojekt verlinkt die zu testenden Quelldateien (kein Verweis auf das WPF-Projekt)
und gehört bewusst nicht zur Solution, damit der Release-Build es nicht mitbaut. Datenbanken sind Wegwerf-Dateien im
Temp-Ordner, Excel-Dateien entstehen synthetisch. Die Plugin-Tests übersetzen ihre Test-Plugins zur Laufzeit mit CodeDom.

## Daten

| Was | Wo |
| --- | --- |
| Datenbank | `C:\ProgramData\Kaltenmark-Engineering\TimeTracker\timetracker.db` |
| Sicherungen | `…\TimeTracker\Backups\*.timetrack-backup` |
| Plugins | `…\TimeTracker\Plugins\<Name>\<Name>.dll` |
| Einstellungen | .NET-Benutzereinstellungen (`Properties/Settings.settings`), werden bei Updates übernommen |

Das Schema legt `TimeTracker.EnsureSchema()` beim Start an und migriert ältere Datenbanken nur additiv. TimeTrack
schickt keine Daten ins Netz; Internet braucht nur die Update-Suche (und gegebenenfalls ein Plugin).

## Plugins

TimeTrack lädt Erweiterungen aus dem Plugin-Ordner. Ein Plugin bringt eigene Seiten und Einstellungen mit, liest
Projekte und Buchungen und legt eigene Daten in der TimeTrack-Datenbank ab; Buchungen ändern kann es nicht. Neu gefundene
Plugins sind ausgeschaltet, bis der Benutzer sie in den Einstellungen aktiviert.

| Dokument | Inhalt |
| --- | --- |
| [PLUGINS.md](PLUGINS.md) | Anleitung: Plugin anlegen, verteilen, installieren, Regeln, Tests |
| [TimeTrack.Plugins.Abstractions/README.md](TimeTrack.Plugins.Abstractions/README.md) | Referenz der Plugin-API: alle Typen und Mitglieder, Lebenszyklus, Threads, Design, Fehler, Versionierung |
| [Samples/TimeTrack.Plugin.Sample](Samples/TimeTrack.Plugin.Sample/SamplePlugin.cs) | Lauffähiges Beispiel |

## Veröffentlichen

Ein Release besteht aus Versions-Tag hier, Installer als GitHub Release in `TimeTrack-Releases` und aktualisierter
README dort. `Scripts\Release.ps1 -Version <yyyy.ww.n>` erledigt alles; Voraussetzung ist ein Eintrag für die Version in
`Helpers/Changelog.cs`. Details und Einzelschritte: [RELEASING.md](RELEASING.md).

Dieses Repository muss öffentlich bleiben, solange es Installationen bis Version 2026.45.0 gibt (die suchen ihre
Updates noch hier, siehe RELEASING.md).

## Dokumentation

| Datei | Inhalt |
| --- | --- |
| [RELEASING.md](RELEASING.md) | Version veröffentlichen, zwei Repositories, Update-Mechanismus |
| [PLUGINS.md](PLUGINS.md) | Plugins schreiben und verteilen |
| [TimeTrack.Plugins.Abstractions/README.md](TimeTrack.Plugins.Abstractions/README.md) | Referenz der Plugin-API |
| [ReleasesRepo/README.template.md](ReleasesRepo/README.template.md) | Anwender-Anleitung (wird ins Release-Repo übertragen) |
| `*_PLAN.md` | Planungsdokumente größerer Vorhaben |
| `Installer/license.txt` | Endbenutzer-Lizenzvereinbarung |
