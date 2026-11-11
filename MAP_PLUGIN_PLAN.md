# MAP-Plugin für TimeTrack: Vorgehensplan

Status: nur Plan, es wurde kein Anwendungsverhalten geändert.
Branch: `feature/map-plugin`.
Grundlage: Stand `main` von TimeTrack (2026.46.1) und Stand `develop` von MAP (2026.45.0), gelesen am 2026-11-11.

## 1. Kurzfassung

Ein Plugin ist sinnvoll, und zwar aus zwei Gründen:

1. **Doppelte Erfassung fällt weg.** TimeTrack kennt bereits jede Buchung mit Datum, Dauer, Projekt und Beschreibung. In MAP werden dieselben Stunden heute von Hand pro Ticket unter `/working-hours` nachgetragen.
2. **Die Trennung passt zur Veröffentlichung.** TimeTrack wird öffentlich verteilt (`TimeTrack-Releases`), MAP ist ein internes Werkzeug. Als Plugin bleibt alles MAP-spezifische außerhalb der öffentlichen Anwendung; wer MAP nicht nutzt, bekommt davon nichts mit.

Der Preis: TimeTrack hat bisher **kein** Plugin-System und MAP **keine** Schnittstelle für Programme. Beides muss zuerst entstehen. Das Vorhaben besteht deshalb aus drei Teilen:

| Teil | Wo | Inhalt |
| --- | --- | --- |
| A | TimeTrack (dieses Repo) | Plugin-Infrastruktur: Vertrag, Lader, Navigation, Speicher, Einstellungen |
| B | MAP (`Project.SES.ES.MAP`) | Token-Anmeldung und eine kleine JSON-API für Tickets und Arbeitsstunden |
| C | eigenes Projekt | Das MAP-Plugin selbst: Zuordnung, Vorschau, Übertragung |

Umfang der ersten Version: **TimeTrack → MAP**. Das Plugin liest Projekte und Tickets aus MAP und schreibt die eigenen Arbeitsstunden dorthin. MAP schreibt nichts nach TimeTrack.

## 2. Ausgangslage

### 2.1 TimeTrack

- WPF / MVVM, .NET Framework 4.8, C# 7.3, altes Projektformat mit expliziten Datei-Einträgen und `packages.config`.
- Seiten sind fest verdrahtet: `MainViewModel.SelectViewCommandExecute` (`switch` über den Seitenschlüssel), `MainWindow.xaml` (ein `RadioButton` je Seite), `Views/Templates.xaml` (ein `DataTemplate` je ViewModel).
- Eine gemeinsame SQLite-Verbindung (`MainViewModel.DbInstance`), die nicht aus Hintergrund-Threads benutzt werden darf.
- `time_entries` hat `datum`, `startzeit`, `endzeit`, `projekt_id`, `beschreibung`. Ein Ticket-Bezug existiert nicht.
- `projects.kostentraeger` enthält die PSP-ID (siehe `Helpers/CarrierMatching.cs`).
- Es gibt keinerlei Mechanismus zum Nachladen von Assemblies; `ViewModel`-Klassen sind `internal`.
- Der Installer packt `bin\Release\*` nach `Program Files`; die Datenbank liegt unter `C:\ProgramData\Kaltenmark-Engineering\TimeTracker\`.

### 2.2 MAP

- ASP.NET Core 10, MVC mit Razor-Views, EF Core, SQL Server, gehostet bei IONOS.
- Anmeldung nur über das Identity-Cookie; alle schreibenden Aktionen verlangen ein Anti-Forgery-Token. Ein Desktop-Programm kann sich damit nicht sauber anmelden.
- `WorkingHours` = `MemberId`, `TicketId`, `DateWorkedOn`, `HoursWorkedOn` (Dezimalstunden), `Description`, `TimeOfEntry`. Projekt und Bestellung ergeben sich über `Ticket → Order → FrameworkContract → OrderCondition → Project`.
- Beim Anlegen (`WorkingHoursController.Create`) wird der `Member` aus angemeldetem Benutzer und Projekt des Tickets ermittelt. Wer kein Mitglied des Projekts ist, bekommt `404`.
- Jede Änderung schreibt `WorkingHoursHistory` über `WorkingHoursHistoryHelper`.
- JSON liefern bisher nur Hilfs-Endpunkte für die eigenen Seiten (`/Tickets/Lookup`, `/api/projects` usw. in `TicketsController`), alle hinter dem Cookie.
- `IfsActivity.IfsLabel` ist das PSP-Element einer Bestellung. Das ist derselbe Schlüssel wie der Kostenträger in TimeTrack.

### 2.3 Die fachliche Lücke

TimeTrack bucht auf **Projekt + Zeitspanne**, MAP auf **Ticket + Stundenzahl**. Das Plugin muss also zwei Dinge leisten, die keine der beiden Anwendungen heute kann:

1. Buchung → Ticket zuordnen.
2. Zeitspannen zu Stunden pro Tag und Ticket zusammenfassen.

Dafür gibt es zwei Anknüpfungspunkte, die ohne Änderung an der Zeiterfassung auskommen:

- **Kostenträger ↔ PSP-Element** grenzt ein, zu welcher MAP-Bestellung ein TimeTrack-Projekt gehört.
- **Ticket-Schlüssel in der Beschreibung** (Muster `[A-Z][A-Z0-9]+-\d+`, z. B. `ABC-123 Review`) ordnet die einzelne Buchung zu. Fehlt er, wählt man das Ticket in der Vorschau von Hand.

## 3. Zielbild

```
TimeTrack.exe  ──lädt──▶  Plugins\Map\TimeTrack.Plugin.Map.dll
     │                              │
     │ IPluginHost                  │ HTTPS + Bearer-Token
     ▼                              ▼
 timetracker.db                MAP  /api/v1/...
 (plugin_records)              (ApiTokens, WorkingHours)
```

Ablauf für den Nutzer:

1. Plugin-DLL in den Plugin-Ordner legen, TimeTrack starten, Plugin in den Einstellungen aktivieren.
2. In MAP unter „Profil“ ein Zugriffstoken erzeugen, im Plugin MAP-Adresse und Token eintragen, „Verbindung testen“.
3. Neue Seite **MAP** in der Navigation: Zeitraum wählen, Vorschau prüfen (Tag, Ticket, Stunden, Status), fehlende Tickets zuordnen, „Übertragen“.
4. Spätere Änderungen an bereits übertragenen Buchungen erscheinen als „Geändert“ und werden beim nächsten Übertragen in MAP nachgezogen.

## 4. Teil A: Plugin-Infrastruktur in TimeTrack

### 4.1 Vertrag: neues Projekt `TimeTrack.Plugins.Abstractions`

Klassenbibliothek, .NET Framework 4.8, C# 7.3, ohne Abhängigkeit auf die Anwendung. Sie ist das Einzige, was ein Plugin referenziert.

| Typ | Aufgabe |
| --- | --- |
| `TimeTrackPlugin` (abstrakte Basisklasse) | `Id`, `Name`, `Version`, `Initialize(IPluginHost)`, `Shutdown()`. Basisklasse statt Interface, weil C# 7.3 keine Default-Implementierungen kennt und spätere Erweiterungen sonst jedes Plugin brechen. |
| `IPluginHost` | Zugang zu allem Folgenden plus `HostApiVersion`. |
| `ITimeTrackData` | Nur lesend: Projekte, Buchungen eines Zeitraums. Eigene DTOs (`PluginProject`, `PluginTimeEntry` mit `Id`), nicht die verschachtelten `TimeTracker.ProjectRow` / `TimeEntryRow`. |
| `IPluginStorage` | Schlüssel-Wert-Ablage je Plugin (`Get/Set/Delete/List` mit Sammlung + Schlüssel + JSON-Text) und `SetSecret/GetSecret` für Zugangsdaten. |
| `IPluginUi` | `RegisterPage(PluginPage)`, `RegisterSettingsSection(...)`, `Navigate(key)`, `ShowNotification(title, text)`. |
| `PluginPage` | Schlüssel, Titel, Icon-Glyph, Fabrik für das ViewModel, Fabrik für die Ansicht (`FrameworkElement`), `Refresh`-Rückruf. |

Bewusst **nicht** im Vertrag: schreibender Zugriff auf Buchungen, direkter SQL-Zugriff, Zugriff auf `Properties.Settings`. Das hält die Zeiterfassung vor fehlerhaften Plugins sicher und kann später gezielt ergänzt werden.

### 4.2 Host in der Anwendung

Neue Dateien (alle zusätzlich in `Zeitmanagement.csproj` eintragen):

| Datei | Inhalt |
| --- | --- |
| `Helpers/Plugins/PluginLoader.cs` | Durchsucht den Plugin-Ordner, lädt je Unterordner die Assembly, sucht Ableitungen von `TimeTrackPlugin`, prüft die Host-API-Version, fängt jeden Fehler pro Plugin ab. |
| `Helpers/Plugins/PluginHost.cs` | Implementiert `IPluginHost` und reicht an `TimeTracker`, Speicher und Oberfläche weiter. |
| `Helpers/Plugins/PluginStorage.cs` | Ablage in der Hauptdatenbank, Geheimnisse mit DPAPI (`ProtectedData`, Benutzerbereich). |
| `Helpers/Plugins/PluginInfo.cs` | Zustand je Plugin für die Einstellungsseite (geladen, deaktiviert, Fehlertext). |
| `TimeTracker.Plugins.cs` | Partielle Klasse wie `TimeTracker.Controlling.cs`: `EnsurePluginSchema(cmd)` und Zugriffe auf `plugin_records`. |

Änderungen an bestehenden Dateien:

| Datei | Änderung |
| --- | --- |
| `TimeTracker.cs` | In `EnsureSchema()` `EnsurePluginSchema(cmd)` aufrufen. Zusätzlich `GetTimeEntriesBetween` um Kostenträger ergänzen oder eine eigene Abfrage für den Plugin-Zugriff. |
| `ViewModel/MainViewModel.cs` | Plugins nach dem Aufbau der festen Seiten laden. `SelectViewCommandExecute`: im `default`-Zweig in den registrierten Plugin-Seiten nachschlagen. Neue Liste `PluginNavigationItems` für die Navigation. |
| `MainWindow.xaml` | Unter den festen Einträgen ein `ItemsControl` über `PluginNavigationItems` mit demselben `RadioButton`-Stil und `SelectViewCommand`. |
| `ViewModel/BaseViewModel.cs` | Kleiner Adapter `PluginPageViewModel : BaseViewModel`, der Ansicht und `Refresh` des Plugins kapselt. So bleibt `((BaseViewModel)RightSide).Refresh()` unverändert. |
| `Views/Templates.xaml` | Ein `DataTemplate` für `PluginPageViewModel`, das die vom Plugin gelieferte Ansicht in einem `ContentPresenter` zeigt. |
| `ViewModel/SettingsViewModel.cs`, `Views/SettingsView.xaml` | Abschnitt „Plugins“: Liste mit Name, Version, Status, Schalter zum Aktivieren, Fehlertext, Schaltfläche „Plugin-Ordner öffnen“. Darunter die von Plugins gelieferten Einstellungsbereiche. |
| `App.xaml.cs` | `Shutdown()` der Plugins in `OnExit`; Tray-Hinweise der Plugins an `TrayNotificationService` anbinden. |
| `Zeitmanagement.sln`, `Zeitmanagement.csproj` | Neues Projekt aufnehmen, Projektverweis auf die Abstractions-Bibliothek. |
| `Helpers/Changelog.cs`, `Helpers/HelpContent.cs` | Eintrag und Hilfethema „Plugins“. |
| `Zeitmanagement.Tests` | Neue Tests, siehe 4.5. |

Der Installer muss nicht angepasst werden: Die Abstractions-DLL landet in `bin\Release` und wird über `bin\Release\*` mitgepackt.

### 4.3 Entscheidungen

- **Plugin-Ordner:** `C:\ProgramData\Kaltenmark-Engineering\TimeTracker\Plugins\<PluginId>\`. Er liegt neben der Datenbank, übersteht Updates und braucht keine Administratorrechte. Ein Ordner unter `Program Files` würde bei jedem Update überschrieben.
- **Aktivierung:** Ein neu gefundenes Plugin ist zunächst **deaktiviert** und wird erst nach Bestätigung in den Einstellungen geladen. Ein Plugin läuft mit allen Rechten der Anwendung; es soll nicht reichen, eine DLL in einen Ordner zu legen.
- **Kein Entladen:** .NET Framework kann Assemblies nicht entladen. Aktivieren und Deaktivieren wirken nach einem Neustart; die Oberfläche sagt das.
- **Abhängigkeiten des Plugins:** `AppDomain.AssemblyResolve` sucht im Ordner des jeweiligen Plugins. Die Abstractions-Bibliothek kommt immer aus der Anwendung.
- **Heruntergeladene DLLs:** Windows markiert sie als „aus dem Internet“, `Assembly.LoadFrom` verweigert sie dann. Die Fehlermeldung in den Einstellungen nennt die Ursache („Datei in den Eigenschaften zulassen“).
- **Speicher:** eine Tabelle `plugin_records(plugin_id, collection, key, json, updated_utc)` mit Primärschlüssel über die ersten drei Spalten, in `timetracker.db`. Damit sind Zuordnungen und Übertragungsstände in der Datensicherung enthalten. Geheimnisse liegen DPAPI-verschlüsselt in derselben Tabelle und sind nach einer Wiederherstellung auf einem anderen Rechner nicht lesbar, das Token muss dann neu eingetragen werden.
- **Threads:** Datenzugriffe des Plugins laufen über den Host auf dem UI-Thread; nur der HTTP-Verkehr läuft im Hintergrund. Die SQLite-Verbindung wird nie an ein Plugin herausgegeben.
- **Themes:** Plugin-Ansichten nutzen `DynamicResource` auf die Schlüssel aus `Themes/Controls.xaml` und den Paletten. Eigene Ressourcen merged das Plugin in seiner Ansicht, nicht in `Application.Resources`, weil `ThemeManager` dort die Palette austauscht.
- **Versionierung:** `HostApiVersion` als Ganzzahl. Ein Plugin nennt die Mindestversion; ist der Host älter, wird es mit Hinweis nicht geladen.

### 4.4 Reihenfolge

1. Abstractions-Projekt und `PluginLoader` mit einem Beispiel-Plugin, das nur eine leere Seite registriert (Machbarkeitsprobe: Laden, Navigation, Theme).
2. `plugin_records` und `PluginStorage`.
3. Einstellungsabschnitt „Plugins“ mit Aktivierung und Fehleranzeige.
4. `ITimeTrackData`.
5. Hilfe, Changelog, Tests.

### 4.5 Tests

Im bestehenden `Zeitmanagement.Tests` (verlinkte Quelldateien, C# 7.3):

- Lader: leerer Ordner, fremde DLL ohne Plugin-Typ, zu neue Mindestversion, Ausnahme in `Initialize` (andere Plugins laden trotzdem), deaktiviertes Plugin wird nicht geladen.
- Speicher: Schreiben, Überschreiben, Löschen, Trennung zwischen zwei Plugin-IDs, Migration einer alten Datenbank ohne Tabelle.
- Geheimnisse: Rundlauf, verschlüsselter Wert steht nicht im Klartext in der Datenbank.

Prüfung der Oberfläche nur über Build und Code, ohne Screenshots.

## 5. Teil B: Schnittstelle in MAP

MAP gehört nicht mir allein. Dieser Teil braucht die Zustimmung des Teams und läuft über den dortigen Ablauf (Branch von `develop`, Pull Request, Änderungsdokument nach `.syspilot/templates/change-document.md`).

### 5.1 Anmeldung über persönliche Zugriffstoken

| Datei | Änderung |
| --- | --- |
| `Models/ApiToken.cs` (neu) | `Id`, `UserId`, `Name`, `TokenHash` (SHA-256), `CreatedAt`, `LastUsedAt`, `ExpiresAt`, `RevokedAt`. Der Klartext wird nie gespeichert. |
| `Data/ApplicationDbContext.cs` | `DbSet<ApiToken>`, eindeutiger Index auf `TokenHash`, Fremdschlüssel auf den Benutzer mit kaskadierendem Löschen. |
| `Migrations/` | `AddApiTokens`. |
| `Security/ApiTokenAuthenticationHandler.cs` (neu) | Eigenes Schema `ApiToken`: liest `Authorization: Bearer ...`, vergleicht den Hash, prüft Ablauf und Widerruf, baut die Identität samt Rollen des Benutzers. |
| `Program.cs` | Schema registrieren (`AddAuthentication().AddScheme(...)`). Das Cookie bleibt Standard, die Weboberfläche ändert sich nicht. |
| `Controllers/ProfileController.cs`, `Views/Profile/Index.cshtml` | Abschnitt „Zugriffstoken“: erzeugen (Klartext wird genau einmal angezeigt), auflisten, widerrufen. |

Verworfen: Anmeldung des Plugins mit Benutzername und Passwort gegen das Login-Formular. Das hieße, Anti-Forgery-Token aus HTML zu lesen und das Passwort lokal zu speichern.

### 5.2 API-Endpunkte

Neuer Controller `Controllers/Api/TimeTrackApiController.cs` mit `[ApiController]`, `[Authorize(AuthenticationSchemes = "ApiToken")]`, ohne Anti-Forgery (kein Cookie, also kein CSRF-Risiko).

| Endpunkt | Zweck |
| --- | --- |
| `GET /api/v1/me` | Anzeigename und Rollen; dient als Verbindungstest. |
| `GET /api/v1/projects` | Projekte, in denen der Benutzer `Member` ist, je Projekt die Bestellungen mit `OderLabel` und den PSP-Elementen (`IfsActivity.IfsLabel`). |
| `GET /api/v1/tickets?projectId=&orderId=&query=&keys=` | Tickets aus diesen Projekten: `Id`, `Key`, `Summary`, `Status`, Bestellung. `keys` löst mehrere Ticket-Schlüssel auf einmal auf. |
| `GET /api/v1/working-hours?from=&to=` | Eigene Arbeitsstunden im Zeitraum, mit `ExternalId`. |
| `PUT /api/v1/working-hours/{externalId}` | Anlegen oder Ändern anhand der externen Kennung. Mehrfaches Senden erzeugt keine Dubletten. |
| `DELETE /api/v1/working-hours/{externalId}` | Löschen einer vom Plugin angelegten Buchung. |

Regeln für alle Endpunkte: nur Projekte, in denen der Benutzer Mitglied ist; nur eigene Buchungen; nur Buchungen mit `ExternalSource = "TimeTrack"` dürfen über die API geändert oder gelöscht werden. Von Hand in MAP angelegte Stunden fasst die API nicht an.

### 5.3 Weitere Änderungen

| Datei | Änderung |
| --- | --- |
| `Models/WorkingHours.cs` | Zwei optionale Spalten `ExternalSource` (max. 32) und `ExternalId` (max. 64); eindeutiger gefilterter Index auf `(MemberId, ExternalSource, ExternalId)`. |
| `Migrations/` | `AddExternalIdToWorkingHours`. Bestehende Zeilen bleiben `NULL`. |
| `Services/WorkingHoursService.cs` (neu) | Anlegen, Ändern, Löschen samt Mitgliedsprüfung und `WorkingHoursHistory` aus `WorkingHoursController` herausziehen, damit Weboberfläche und API dieselbe Logik und dieselbe Historie verwenden. |
| `Controllers/WorkingHoursController.cs` | Nutzt den neuen Service; Verhalten unverändert. |
| `Views/WorkingHours/Index.cshtml` | Optional: Kennzeichen „aus TimeTrack“ an übertragenen Zeilen. |
| `MAP.Tests` | Token (gültig, abgelaufen, widerrufen, falsch), Zugriff nur auf eigene Projekte und Buchungen, Idempotenz von `PUT`, Historie wird geschrieben, API ohne Token liefert `401` statt Weiterleitung zum Login. |
| `Documentation/DEVELOPER_DOCUMENTATION.md`, `Views/Home/Help.cshtml` | §6 (Token), §7 (API), Migrationstabelle, Hilfe zum Erzeugen eines Tokens. |

## 6. Teil C: Das MAP-Plugin

Eigenes Projekt `TimeTrack.Plugin.Map` (.NET Framework 4.8, C# 7.3), referenziert nur die Abstractions-Bibliothek.

| Datei | Inhalt |
| --- | --- |
| `MapPlugin.cs` | Ableitung von `TimeTrackPlugin`; registriert Seite und Einstellungsbereich. |
| `MapApiClient.cs` | `HttpClient` mit Bearer-Token, TLS 1.2, Zeitlimit, verständliche Fehler (`401` = Token ungültig, `404` = kein Mitglied des Projekts, nicht erreichbar). JSON über `DataContractJsonSerializer`, damit kein zusätzliches Paket nötig ist. |
| `MapSettingsView.xaml` + ViewModel | MAP-Adresse, Token, „Verbindung testen“. |
| `ProjectMapping.cs` | TimeTrack-Projekt ↔ MAP-Projekt/Bestellung. Vorschlag über Kostenträger enthält PSP-Element (gleiche Regel wie `CarrierMatching`), Bestätigung durch den Nutzer. |
| `TicketResolver.cs` | Ticket-Schlüssel aus der Beschreibung lesen, gegen MAP auflösen, sonst gespeicherte manuelle Zuordnung. |
| `BookingAggregator.cs` | Buchungen zu Zeilen je Tag und Ticket zusammenfassen: Summe der Dauer als Dezimalstunden, Beschreibungen ohne Dubletten zusammengeführt. |
| `SyncPlanner.cs` | Vergleicht die Zeilen mit dem gespeicherten Übertragungsstand: Neu, Geändert, Unverändert, In TimeTrack gelöscht, Ohne Ticket, Projekt nicht zugeordnet. |
| `MapSyncView.xaml` + ViewModel | Zeitraum, Vorschau-Tabelle, Ticketauswahl für offene Zeilen, „Übertragen“, Ergebnis je Zeile. |

Festlegungen:

- **Externe Kennung:** eine Zeile ist `Datum + Ticket`, die Kennung also z. B. `tt:2026-11-10:<TicketId>`. Sie hängt nicht an einzelnen Buchungs-IDs, deshalb überstehen Teilen, Zusammenlegen und Löschen einzelner Buchungen die nächste Übertragung.
- **Übertragungsstand** in `plugin_records`: je Kennung die zuletzt gesendeten Stunden und der Text. Daraus ergibt sich „Geändert“ ohne Rückfrage bei MAP.
- **Nichts geschieht automatisch.** Übertragen wird nur auf Knopfdruck nach der Vorschau. Eine laufende Buchung und der heutige Tag sind standardmäßig ausgenommen.
- **Löschen in MAP** nur für Zeilen, die das Plugin selbst angelegt hat, und nur nach ausdrücklicher Bestätigung in der Vorschau.
- **Rundung:** Stunden werden ungerundet auf zwei Nachkommastellen übertragen. Eine Rundungsregel (z. B. Viertelstunden) ist eine offene Frage, siehe 8.
- **Kein Bezug zur SAP-Verrechnung:** Das Plugin sendet die tatsächlich gebuchten Zeiten, nicht die von `SapBookingCalculator` verteilten.

Tests als eigenes Testprojekt neben dem Plugin: Schlüsselerkennung in Beschreibungen, Zusammenfassung über mehrere Buchungen und Projekte, Planung der Änderungen (alle Zustände), API-Client gegen einen nachgebildeten `HttpMessageHandler`.

## 7. Reihenfolge und Abnahme

| Schritt | Ergebnis | Abhängig von |
| --- | --- | --- |
| 1 | Teil A, Schritte 1–3: Beispiel-Plugin erscheint in Navigation und Einstellungen | – |
| 2 | Teil B: Token und `GET /api/v1/me`, lokal gegen LocalDB | Zustimmung MAP-Team |
| 3 | Teil C: Einstellungen und Verbindungstest | 1, 2 |
| 4 | Teil B: Projekte, Tickets, Arbeitsstunden | 2 |
| 5 | Teil A, Schritt 4 und Teil C: Zuordnung, Vorschau, Übertragung | 3, 4 |
| 6 | Hilfe, Changelog, Dokumentation in beiden Repos, Verteilung des Plugins | 5 |

Schritt 1 und 2 sind unabhängig und können parallel laufen. Schritt 1 ist auch ohne MAP ein in sich abgeschlossenes Feature und kann eigenständig gemergt werden.

Fertig ist das Vorhaben, wenn:

- TimeTrack ohne Plugin-Ordner unverändert startet und ein fehlerhaftes Plugin den Start nicht verhindert;
- eine Woche Buchungen mit Ticket-Schlüssel in der Beschreibung ohne Handarbeit in MAP landet;
- zweimaliges Übertragen keine Dubletten erzeugt;
- eine nachträglich geänderte Buchung in MAP korrigiert wird und dort in der Historie steht;
- von Hand in MAP erfasste Stunden nie verändert werden;
- ein widerrufenes Token sofort zu einer verständlichen Meldung im Plugin führt.

## 8. Offene Fragen

1. **Wo liegt der Quellcode des Plugins?** Das Quell-Repo von TimeTrack muss laut `RELEASING.md` öffentlich bleiben. Vorschlag: Abstractions und Host hier, das MAP-Plugin in einem privaten Repo oder als eigenes Projekt im MAP-Repo. Dasselbe gilt für dieses Dokument: Vor einem Push entscheiden, ob Teil B öffentlich stehen soll.
2. **Trägt das MAP-Team Teil B mit?** Ohne Token-Anmeldung und API gibt es keinen sauberen Weg. Davon hängt alles ab Schritt 2 ab.
3. **Ticket-Schlüssel in der Beschreibung:** Reicht das als Zuordnung, oder soll TimeTrack später ein eigenes Ticket-Feld je Buchung bekommen (größerer Eingriff in `time_entries`, Quick Switch und Buchungsseite)?
4. **Rundung und Mindestdauer:** ungerundet, Viertelstunden oder wie in MAP üblich?
5. **Verteilung:** Plugin als ZIP zum Entpacken von Hand, oder soll der Update-Mechanismus später auch Plugins aktualisieren?
6. **Erreichbarkeit:** Ist MAP von allen Arbeitsplätzen per HTTPS erreichbar, auch aus dem Firmennetz mit Proxy?
7. **Spätere Ausbaustufe:** Tickets aus MAP als Vorschläge beim Buchen im Quick Switch. Das bräuchte eine Erweiterung des Vertrags und ist nicht Teil der ersten Version.
