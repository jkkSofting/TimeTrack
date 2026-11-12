# Plugins für TimeTrack schreiben

TimeTrack lädt Erweiterungen aus einem Plugin-Ordner. Ein Plugin kann eigene Seiten in die Navigation bringen,
Einstellungsbereiche ergänzen, Projekte und Buchungen lesen und eigene Daten in der TimeTrack-Datenbank ablegen.
Buchungen ändern kann es nicht.

Ein lauffähiges Beispiel liegt in [Samples/TimeTrack.Plugin.Sample](Samples/TimeTrack.Plugin.Sample/SamplePlugin.cs).
Die vollständige Referenz aller Typen und Mitglieder der Plugin-API steht in
[TimeTrack.Plugins.Abstractions/README.md](TimeTrack.Plugins.Abstractions/README.md); diese Datei hier ist die Anleitung.

## Aufbau

| Teil | Wo |
| --- | --- |
| Vertrag | `TimeTrack.Plugins.Abstractions` (net48, C# 7.3). Das Einzige, was ein Plugin referenziert. |
| Lader | `Helpers/Plugins/PluginLoader.cs`: findet Plugins, lädt die aktivierten, fängt jeden Fehler pro Plugin ab. |
| Host | `Helpers/Plugins/PluginHost.cs`: der `IPluginHost` eines Plugins; Daten und Ablage laufen auf dem UI-Thread. |
| Verwaltung | `Helpers/Plugins/PluginManager.cs`: Aktivierung, Seiten, Einstellungsbereiche, Beenden. |
| Ablage | `TimeTracker.Plugins.cs` (Tabelle `plugin_records`) und `Helpers/Plugins/PluginStorage.cs` (DPAPI für Geheimnisse). |
| Oberfläche | `ViewModel/PluginPageViewModel.cs`, `ViewModel/PluginSettingsViewModel.cs`, `Views/PluginViewHost.cs`, Gruppe „Plugins“ in `MainWindow.xaml`, Karte „Plugins“ in `Views/SettingsView.xaml`. |

## Ein Plugin anlegen

1. Klassenbibliothek für .NET Framework 4.8 anlegen, `UseWPF` einschalten, wenn das Plugin eigene XAML-Ansichten hat.
2. `TimeTrack.Plugins.Abstractions` referenzieren, und zwar mit `Private="false"`: Die DLL kommt zur Laufzeit immer aus
   TimeTrack. Liegt eine eigene Kopie im Plugin-Ordner, meldet TimeTrack das als Fehler.
3. Eine öffentliche Klasse von `TimeTrackPlugin` ableiten (genau eine je DLL, parameterloser Konstruktor):

```csharp
public sealed class MyPlugin : TimeTrackPlugin
{
    public override string Id => "my-plugin";          // stabil, a-z 0-9 . - _
    public override string Name => "Mein Plugin";

    public override void Initialize(IPluginHost host)
    {
        host.Ui.RegisterPage(new PluginPage("main", "Meine Seite", () => new MyView(host)) { Glyph = "\uE734" });
        host.Ui.RegisterSettingsSection(new PluginSettingsSection("Verbindung", () => new MySettingsView(host)));
    }
}
```

Seiten und Einstellungsbereiche lassen sich nur in `Initialize` registrieren. Der Konstruktor soll nichts tun.

## Verteilen und installieren

Ein Plugin liegt in `C:\ProgramData\Kaltenmark-Engineering\TimeTracker\Plugins\<Name>\` und besteht dort aus
`<Name>.dll` (Ordnername = Name der DLL) plus ihren Abhängigkeiten. Der Ordner liegt neben der Datenbank, übersteht
Updates und braucht keine Administratorrechte.

- TimeTrack sucht beim Start, bei jedem Öffnen der Einstellungen und mit „Nach Plugins suchen“ (Einstellungen →
  Plugins) nach Plugins. Das Suchen lädt nichts; ein neu abgelegtes Plugin erscheint sofort zum Aktivieren.
- Ein neu gefundenes Plugin ist deaktiviert. Vor dem Aktivieren führt TimeTrack keinen Code daraus aus; Name, Version
  und Beschreibung in den Einstellungen stammen aus den Dateieigenschaften (`Product`, `Version`, `Description`).
- **Neue Version eines Plugins:** TimeTrack beenden und die Dateien ersetzen. Bei laufendem TimeTrack ist die DLL
  gesperrt, lässt sich aber umbenennen; die neue daneben legen. „Nach Plugins suchen“ zeigt dann „neue Fassung“
  (erkannt am Änderungsdatum der DLL) und bietet den Neustart an.
- Aktivieren und Deaktivieren wirken nach einem Neustart (.NET Framework kann Assemblies nicht entladen). Die Wahl steht
  in `plugin_records` unter `plugin_id = '$host'`, `collection = 'enabled'`, Schlüssel = Ordnername.
- Aus dem Internet geladene DLLs blockiert Windows; die Einstellungen sagen dann, wie man sie freigibt.

## Regeln für Plugins

- **Threads:** `host.Data` und `host.Storage` dürfen von jedem Thread aufgerufen werden, laufen aber auf dem UI-Thread.
  Netzwerkzugriffe gehören in den Hintergrund (`async`/`await`), Datenzugriffe davor oder danach.
- **Ablage:** `host.Storage` ist eine Schlüssel-Wert-Ablage je Plugin (Sammlung + Schlüssel → Text, meist JSON).
  Sammlungen mit `$` sind TimeTrack vorbehalten. `SetSecret`/`GetSecret` verschlüsseln mit DPAPI für den Windows-Benutzer;
  nach einer Wiederherstellung auf einem anderen Rechner liefert `GetSecret` `null`.
- **Design:** Farben über `{DynamicResource Theme.*}` (z. B. `Theme.Text`, `Theme.TextMuted`, `Theme.Card`,
  `Theme.Accent`, `Theme.Danger`), Stile über `{StaticResource …}` (`Page`, `PageHeader`, `Card`, `Text.PageTitle`,
  `Text.Subtitle`, `Text.Section`, `Text.Body`, `Text.Muted`, `Button.Primary`, `Button.Ghost`, `Switch`). Eigene
  Ressourcen in die Ressourcen der Ansicht, nie in `Application.Resources` (dort tauscht der `ThemeManager` die Palette).
- **Ansichten** entstehen beim ersten Öffnen und werden danach wiederverwendet. `PluginPage.Refresh` läuft bei jedem
  Öffnen der Seite.
- **Fehler:** Eine Ausnahme in `Initialize` deaktiviert nur dieses Plugin. Ausnahmen in `Refresh` und `Shutdown` werden
  protokolliert und ignoriert.

## Versionen

`IPluginHost.HostApiVersion` steigt, wenn der Vertrag neue Mitglieder bekommt; bestehende ändern sich nicht. Ein Plugin,
das mehr braucht, überschreibt `MinimumHostApiVersion`; ist TimeTrack älter, wird es mit Hinweis nicht geladen.
Die Assembly-Version der Abstractions bleibt fest bei 1.0.0.0.

| HostApiVersion | Inhalt |
| --- | --- |
| 1 | Projekte und Buchungen lesen, Ablage mit Geheimnissen, Seiten, Einstellungsbereiche, Navigation, Tray-Hinweise |

## Tests

`Zeitmanagement.Tests` enthält `PluginStorageTests`, `PluginLoaderTests` und `PluginManagerTests`. Die Test-Plugins
werden zur Laufzeit mit CodeDom übersetzt (`TestPluginCompiler`), es liegen keine Binärdateien im Repo.
