# TimeTrack Plugin-API (`TimeTrack.Plugins.Abstractions`)

Referenz der Schnittstelle zwischen TimeTrack und seinen Plugins. Diese Bibliothek ist das Einzige, was ein Plugin
referenziert. Eine Anleitung zum Schreiben, Verteilen und Testen eines Plugins steht in [../PLUGINS.md](../PLUGINS.md),
ein lauffähiges Beispiel in [../Samples/TimeTrack.Plugin.Sample](../Samples/TimeTrack.Plugin.Sample/SamplePlugin.cs).

| | |
| --- | --- |
| Assembly | `TimeTrack.Plugins.Abstractions.dll`, Version fest `1.0.0.0` |
| Namespace | `TimeTrack.Plugins` |
| Zielplattform | .NET Framework 4.8, C# 7.3, WPF |
| Schnittstellen-Version | `HostApiVersion` = **1** |
| Referenz im Plugin | mit `Private="false"`: die DLL kommt zur Laufzeit immer aus TimeTrack |

## Inhalt

1. [Überblick](#1-überblick)
2. [Lebenszyklus](#2-lebenszyklus)
3. [`TimeTrackPlugin`](#3-timetrackplugin)
4. [`IPluginHost`](#4-ipluginhost)
5. [`ITimeTrackData`](#5-itimetrackdata)
6. [`IPluginStorage`](#6-ipluginstorage)
7. [`IPluginUi`, `PluginPage`, `PluginSettingsSection`](#7-ipluginui-pluginpage-pluginsettingssection)
8. [Threads](#8-threads)
9. [Design und Ressourcen](#9-design-und-ressourcen)
10. [Fehlerverhalten](#10-fehlerverhalten)
11. [Versionierung und Kompatibilität](#11-versionierung-und-kompatibilität)
12. [Was die API bewusst nicht bietet](#12-was-die-api-bewusst-nicht-bietet)

## 1. Überblick

```
TimeTrackPlugin  ──Initialize(host)──▶  IPluginHost
                                         ├── Data     : ITimeTrackData   Projekte und Buchungen lesen
                                         ├── Storage  : IPluginStorage   eigene Daten und Geheimnisse ablegen
                                         └── Ui       : IPluginUi        Seiten, Einstellungen, Navigation, Hinweise
```

```csharp
using System.Windows;
using TimeTrack.Plugins;

public sealed class MyPlugin : TimeTrackPlugin
{
    public override string Id => "my-plugin";
    public override string Name => "Mein Plugin";

    public override void Initialize(IPluginHost host)
    {
        host.Ui.RegisterPage(new PluginPage("main", "Meine Seite", () => new MyView(host))
        {
            Glyph = "\uE734",
            Refresh = () => { /* bei jedem Öffnen der Seite */ }
        });
        host.Ui.RegisterSettingsSection(new PluginSettingsSection("Verbindung", () => new MySettingsView(host)));
    }
}
```

## 2. Lebenszyklus

| Schritt | Wer | Was passiert |
| --- | --- | --- |
| Finden | TimeTrack beim Start, beim Öffnen der Einstellungen und mit „Nach Plugins suchen“ | Jeder Unterordner von `C:\ProgramData\Kaltenmark-Engineering\TimeTracker\Plugins\` ist ein Plugin, `<Ordner>\<Ordner>.dll` seine Assembly. Ohne Aktivierung wird **kein Code** daraus ausgeführt; Name, Version und Beschreibung in den Einstellungen kommen aus den Dateieigenschaften (`Product`, `Version`, `Description`) |
| Aktivieren | Benutzer | Einstellungen → Plugins, Schalter mit Rückfrage; wirkt nach einem Neustart |
| Laden | TimeTrack | `Assembly.LoadFrom`, Suche nach genau einer öffentlichen, nicht abstrakten Ableitung von `TimeTrackPlugin` mit parameterlosem Konstruktor, Prüfung von `Id` und `MinimumHostApiVersion` |
| `Initialize(host)` | TimeTrack, UI-Thread | Einmal, nach dem Aufbau der eingebauten Seiten. Nur hier darf das Plugin Seiten und Einstellungsbereiche registrieren |
| Seite öffnen | Benutzer | Beim ersten Öffnen `PluginPage.CreateView()`, bei jedem Öffnen `PluginPage.Refresh()` |
| Einstellungen öffnen | Benutzer | Beim ersten Anzeigen `PluginSettingsSection.CreateView()` |
| `Shutdown()` | TimeTrack beim Beenden, UI-Thread | Einmal für jedes geladene Plugin |

Deaktivieren wirkt ebenfalls erst nach einem Neustart: .NET Framework kann Assemblies nicht entladen. Aus demselben
Grund läuft eine neue Fassung eines Plugins erst nach einem Neustart; erneutes Suchen erkennt eine ausgetauschte DLL
(Änderungsdatum) und meldet sie in den Einstellungen.

## 3. `TimeTrackPlugin`

Abstrakte Basisklasse jedes Plugins. Eine Basisklasse statt eines Interfaces, damit neue virtuelle Mitglieder mit
Standardverhalten hinzukommen können, ohne bestehende Plugins zu brechen.

| Mitglied | Art | Bedeutung |
| --- | --- | --- |
| `string Id` | abstrakt | Stabile, eindeutige Kennung, z. B. `"map"`. Darunter liegen die Daten des Plugins; sie darf sich nie ändern. Erlaubt: `a–z`, `0–9`, `.`, `-`, `_`, höchstens 64 Zeichen, beginnt mit Buchstabe oder Ziffer |
| `string Name` | abstrakt | Anzeigename in Navigation und Einstellungen |
| `string Description` | virtuell, Standard `""` | Kurze Beschreibung für die Einstellungen |
| `Version Version` | virtuell, Standard Assembly-Version | Wird in den Einstellungen angezeigt |
| `int MinimumHostApiVersion` | virtuell, Standard `1` | Kleinste `HostApiVersion`, mit der das Plugin funktioniert |
| `void Initialize(IPluginHost host)` | abstrakt | Siehe Lebenszyklus |
| `void Shutdown()` | virtuell, leer | Aufräumen beim Beenden; Ausnahmen werden protokolliert und ignoriert |

Der Konstruktor soll nichts tun. Alles gehört in `Initialize`, dort sind Fehler dem Plugin zugeordnet und erscheinen in
den Einstellungen.

## 4. `IPluginHost`

Eine Instanz je Plugin, übergeben an `Initialize`. Das Plugin darf sie für seine ganze Laufzeit behalten.

| Mitglied | Typ | Bedeutung |
| --- | --- | --- |
| `HostApiVersion` | `int` | Version dieser Schnittstelle, derzeit `1` |
| `HostVersion` | `string` | Version von TimeTrack, z. B. `"2026.47.0"` |
| `PluginDirectory` | `string` | Ordner, aus dem das Plugin geladen wurde (für mitgelieferte Dateien) |
| `Data` | `ITimeTrackData` | Lesender Zugriff auf Projekte und Buchungen |
| `Storage` | `IPluginStorage` | Ablage des Plugins |
| `Ui` | `IPluginUi` | Oberfläche |

## 5. `ITimeTrackData`

Nur lesend. Darf von jedem Thread aufgerufen werden (siehe [Threads](#8-threads)).

| Methode | Ergebnis |
| --- | --- |
| `IReadOnlyList<PluginProject> GetProjects()` | Alle Projekte, nach Name sortiert |
| `IReadOnlyList<PluginTimeEntry> GetTimeEntries(DateTime fromInclusive, DateTime toInclusive)` | Alle abgeschlossenen Buchungen mit Datum im Zeitraum (nur der Datumsteil zählt), sortiert nach Datum und Beginn. Eine laufende Buchung im Quick Switch erscheint erst nach Pause oder Stop. Buchungen mit unlesbaren Zeiten oder Ende vor Beginn werden übersprungen |

### `PluginProject`

| Eigenschaft | Typ | Bedeutung |
| --- | --- | --- |
| `Id` | `int` | Interne Kennung, bleibt beim Umbenennen gleich. Geeignet als Schlüssel für eigene Zuordnungen |
| `Name` | `string` | Projektname (eindeutig) |
| `Customer` | `string` | Kunde |
| `CostCarrier` | `string` | Kostenträger, enthält in der Regel ein SAP-PSP-Element |
| `IsExternal` | `bool` | Vom Kunden bezahlt |

### `PluginTimeEntry`

| Eigenschaft | Typ | Bedeutung |
| --- | --- | --- |
| `Id` | `int` | Interne Kennung, bleibt beim Bearbeiten gleich, verschwindet beim Löschen |
| `Date` | `DateTime` | Tag der Buchung (ohne Uhrzeit) |
| `Start`, `End` | `TimeSpan` | Beginn und Ende als Uhrzeit des Tages, `End > Start` |
| `Duration` | `TimeSpan` | `End - Start` |
| `ProjectId`, `ProjectName`, `CostCarrier` | `int`, `string`, `string` | Projekt der Buchung |
| `Description` | `string` | Beschreibung, kann `null` sein. Buchungen aus dem Quick Switch tragen standardmäßig `"Quick Select"` |

## 6. `IPluginStorage`

Schlüssel-Wert-Ablage je Plugin in der TimeTrack-Datenbank (Tabelle `plugin_records`), damit in jeder Datensicherung
enthalten. Jedes Plugin sieht nur seine eigenen Einträge (abgegrenzt über `Id`).

| Methode | Bedeutung |
| --- | --- |
| `string Get(string collection, string key)` | Der Wert oder `null` |
| `void Set(string collection, string key, string value)` | Anlegen oder überschreiben; `null` löscht |
| `bool Delete(string collection, string key)` | Löschen; `true`, wenn es den Eintrag gab |
| `IReadOnlyDictionary<string, string> List(string collection)` | Alle Einträge einer Sammlung, Schlüssel → Wert (keine zugesicherte Reihenfolge) |
| `void SetSecret(string key, string value)` | Geheimnis speichern, mit Windows-DPAPI für den angemeldeten Benutzer verschlüsselt; `null`/leer löscht |
| `string GetSecret(string key)` | Entschlüsseltes Geheimnis oder `null` (nicht vorhanden oder nicht lesbar) |

Regeln:

- Werte sind Texte, üblicherweise JSON. Sammlung und Schlüssel: nicht leer, höchstens 200 Zeichen.
- Sammlungen, die mit `$` beginnen, sind TimeTrack vorbehalten (`ArgumentException`).
- Geheimnisse sind an Rechner und Windows-Benutzer gebunden. Nach dem Wiederherstellen einer Sicherung auf einem anderen
  Rechner liefert `GetSecret` `null`; das Plugin muss dann zur Neueingabe auffordern.
- Kein Mengenlimit, aber die Ablage ist für Einstellungen, Zuordnungen und Zustände gedacht, nicht für Massendaten.

Tabelle (zur Orientierung, kein Vertrag):

```sql
plugin_records(plugin_id, collection, key, json, updated_utc)  -- PRIMARY KEY (plugin_id, collection, key)
```

## 7. `IPluginUi`, `PluginPage`, `PluginSettingsSection`

Nur vom UI-Thread aufrufen.

| Methode | Bedeutung |
| --- | --- |
| `void RegisterPage(PluginPage page)` | Seite in der Navigation, Gruppe „Plugins“. Nur in `Initialize`; der Schlüssel muss im Plugin eindeutig sein |
| `void RegisterSettingsSection(PluginSettingsSection section)` | Bereich auf der Einstellungsseite unter dem Eintrag des Plugins. Nur in `Initialize` |
| `void Navigate(string pageKey)` | Zeigt eine eigene Seite (Groß-/Kleinschreibung egal); unbekannte Schlüssel werden ignoriert |
| `void OpenSettings()` | Öffnet die Einstellungsseite von TimeTrack |
| `void ShowNotification(string title, string message, Action onClick = null)` | Hinweis am Tray-Symbol. Ein Klick holt das Hauptfenster nach vorn und ruft `onClick` auf |

### `PluginPage`

| Mitglied | Bedeutung |
| --- | --- |
| `PluginPage(string key, string title, Func<FrameworkElement> createView)` | Schlüssel (z. B. `"sync"`), Text in der Navigation, Fabrik für die Ansicht |
| `Key`, `Title` | wie übergeben |
| `Glyph` | Optional: Icon aus „Segoe Fluent Icons“ / „Segoe MDL2 Assets“, z. B. `"\uE898"`; sonst ein Puzzle-Symbol |
| `CreateView` | Wird beim ersten Öffnen aufgerufen. Die Ansicht bleibt danach erhalten und wird bei jedem Öffnen wieder gezeigt; das Plugin setzt ihren `DataContext` selbst |
| `Refresh` | Optional, bei jedem Öffnen der Seite (wie das Neuladen der eingebauten Seiten) |

Der Navigationsschlüssel einer Seite ist intern `plugin:<id>:<key>` (klein geschrieben).

### `PluginSettingsSection`

| Mitglied | Bedeutung |
| --- | --- |
| `PluginSettingsSection(string title, Func<FrameworkElement> createView)` | Überschrift und Fabrik für die Ansicht (einmal erzeugt, danach wiederverwendet) |

Ein Einstellungsbereich speichert selbst. Die Schaltflächen „Speichern“/„Verwerfen“ der Einstellungsseite betreffen
ihn nicht.

## 8. Threads

- `Initialize`, `Shutdown`, `CreateView`, `Refresh` und alle Aufrufe von `IPluginUi` laufen auf dem UI-Thread.
- `Data` und `Storage` dürfen von jedem Thread aufgerufen werden. TimeTrack führt sie trotzdem auf dem UI-Thread aus,
  weil die Datenbank nur dort benutzt wird; ein Aufruf aus dem Hintergrund wartet also, bis der UI-Thread frei ist.
  Aus dem UI-Thread heraus nie synchron auf einen Hintergrund-Task warten, der seinerseits `Data` oder `Storage`
  aufruft (Deadlock).
- Netzwerk und andere langsame Arbeit gehören mit `async`/`await` in den Hintergrund; Daten vorher lesen bzw. danach
  schreiben.
- `ShowNotification`, `Navigate` und `OpenSettings` aus einem Hintergrund-Thread werden auf den UI-Thread umgeleitet.

## 9. Design und Ressourcen

Plugin-Ansichten erscheinen im Design von TimeTrack, wenn sie die gemeinsamen Ressourcen nutzen. Empfohlen ist
`DynamicResource`: Ein Designwechsel greift sofort, und ein fehlender Schlüssel lässt die Ansicht nicht scheitern.

| Art | Schlüssel |
| --- | --- |
| Farben (`Brush`) | `Theme.AppBackground`, `Theme.Card`, `Theme.CardBorder`, `Theme.Surface`, `Theme.Divider`, `Theme.Text`, `Theme.TextMuted`, `Theme.TextSubtle`, `Theme.Accent`, `Theme.AccentSoft`, `Theme.Success`, `Theme.Warning`, `Theme.Danger` (und die `…Soft`-Varianten) |
| Stile | `Page`, `PageHeader`, `Card`, `Text.PageTitle`, `Text.Subtitle`, `Text.Section`, `Text.Label`, `Text.Body`, `Text.Muted`, `Button.Primary`, `Button.Ghost`, `Button.Danger`, `Switch` |
| Implizit | `Button` (wie `Button.Secondary`), `TextBox`, `CheckBox`, `ComboBox`, `DataGrid` (samt Kopf, Zeilen, Zellen), `DatePicker`, `ScrollBar` und `ToolTip` bekommen den TimeTrack-Stil automatisch. `PasswordBox` nicht |

Eigene Ressourcen gehören in die Ressourcen der Ansicht, nie in `Application.Resources`: Dort tauscht der
`ThemeManager` die Palette aus. Die angehängte Eigenschaft `Ui.Glyph` der eingebauten Seiten liegt in der Anwendung und
ist für Plugins nicht verfügbar.

## 10. Fehlerverhalten

| Situation | Folge |
| --- | --- |
| Ausnahme in `Initialize` | Nur dieses Plugin wird deaktiviert; Grund in den Einstellungen; registrierte Seiten verfallen |
| Ausnahme in `CreateView` | An Stelle der Ansicht steht die Fehlermeldung |
| Ausnahme in `Refresh` oder `Shutdown` | Protokolliert und ignoriert |
| Registrierung nach `Initialize` | `InvalidOperationException` |
| Ungültige oder doppelte `Id`, mehrere Plugin-Klassen in einer DLL, zu neue `MinimumHostApiVersion` | Plugin wird nicht geladen, Grund in den Einstellungen |
| Eigene Kopie von `TimeTrack.Plugins.Abstractions.dll` im Plugin-Ordner, die nicht passt | Plugin wird nicht geladen, Hinweis in den Einstellungen |
| Aus dem Internet geladene, von Windows blockierte DLL | Plugin wird nicht geladen, Hinweis „Dateieigenschaften → Zulassen“ |

Abhängigkeiten eines Plugins werden in seinem eigenen Ordner gesucht.

## 11. Versionierung und Kompatibilität

- Die Assembly-Version bleibt fest bei `1.0.0.0`, damit gegen ältere Fassungen gebaute Plugins ohne Binding-Redirect laden.
- Neue Mitglieder kommen nur hinzu; bestehende werden nicht geändert oder entfernt. Neue Fähigkeiten erhöhen
  `HostApiVersion`. Ein Plugin, das sie braucht, überschreibt `MinimumHostApiVersion`.
- Für optionale Nutzung neuer Fähigkeiten kann ein Plugin zur Laufzeit `host.HostApiVersion` abfragen.

| `HostApiVersion` | TimeTrack | Inhalt |
| --- | --- | --- |
| 1 | 2026.47.0 | Projekte und Buchungen lesen, Ablage mit Geheimnissen, Seiten, Einstellungsbereiche, Navigation, Einstellungen öffnen, Tray-Hinweise |

## 12. Was die API bewusst nicht bietet

- **Kein Schreibzugriff auf Buchungen oder Projekte.** Ein fehlerhaftes Plugin soll die Zeiterfassung nicht beschädigen können.
- **Kein direkter Datenbankzugriff** und keine Herausgabe der SQLite-Verbindung.
- **Kein Zugriff auf die Einstellungen von TimeTrack** (`Properties.Settings`).
- **Keine Ereignisse** bei Änderungen an Buchungen; ein Plugin liest bei `Refresh` neu.

Erweiterungen dieser Art kommen, wenn ein Plugin sie braucht, als neue `HostApiVersion`.
