# Eine neue Version veröffentlichen

Damit `Helpers/UpdateChecker.cs` bei installierten Clients automatisch eine neue Version
erkennt, müssen bei jedem Release **alle** der folgenden Schritte durchgeführt werden. Der
Update-Check fragt `GET https://api.github.com/repos/jkkSofting/TimeTrack-Releases/releases/latest`
ab und vergleicht dessen `tag_name` mit der lokal installierten `AssemblyVersion`.

## Zwei Repositories

- **`jkkSofting/TimeTrack`** — der Quellcode. Hier landen Commits und die Versions-Tags.
- **`jkkSofting/TimeTrack-Releases`** — öffentlich. Hier liegen die GitHub Releases mit den
  Installern (dort sucht der `UpdateChecker`) und eine `README.md` mit Anleitung und Changelog für
  Anwender.

Die README im Release-Repo wird **nie von Hand** bearbeitet, sondern von
`Scripts\Update-ReleasesReadme.ps1` aus `ReleasesRepo\README.template.md` und
`Helpers\Changelog.cs` erzeugt (siehe Schritt 6).

## Kurzform: `Scripts\Release.ps1`

Führt alle Schritte unten automatisch aus (Version bumpen, Release-Build, Installer
kompilieren, committen/taggen/pushen, GitHub Release samt `.exe`-Anhang anlegen, README im
Release-Repo aktualisieren):

```powershell
.\Scripts\Release.ps1 -Version 2026.40.0
.\Scripts\Release.ps1 -Version 2026.40.0 -Notes "Kurze Beschreibung der Änderungen"
```

Das Skript bricht ab, wenn `Helpers/Changelog.cs` keinen Eintrag für die Version hat (siehe
Schritt 0), setzt das Datum dieses Eintrags auf den Release-Tag und erzeugt ohne `-Notes` die
Release-Notes auf GitHub aus dem Eintrag. Es bricht außerdem **vor dem Build** ab, wenn das
Release-Repo noch keinen Commit hat (siehe „Einmalig“ unten).

Voraussetzungen: Visual Studio (MSBuild), Inno Setup 6 (`ISCC.exe`), und ein im Git Credential
Manager gecachter GitHub-Login für `jkkSofting` mit Schreibrechten auf **beide** Repos (wird per
`git credential fill` gelesen, siehe `-PushUser`-Parameter). Bricht bei jedem fehlgeschlagenen
Schritt sofort ab, statt halbfertig weiterzumachen. Die manuellen Einzelschritte unten sind der
Referenz-Ablauf, den das Skript nachbildet.

## Einmalig: Umzug auf `TimeTrack-Releases` (Brückenversion)

Bis einschließlich **2026.45.0** haben installierte Clients in `jkkSofting/TimeTrack` nach
Updates gesucht. Damit sie ohne Zutun des Nutzers zum neuen Repo wechseln, gibt es eine
**Brückenversion** — die erste Version, deren `UpdateChecker` auf `TimeTrack-Releases` zeigt
(`2026.46.0`):

1. **README ins Release-Repo bringen** (einmalig; erzeugt auch den ersten Commit, ohne den dort
   kein Release angelegt werden kann). Mit der zuletzt tatsächlich veröffentlichten Version:
   ```powershell
   .\Scripts\Update-ReleasesReadme.ps1 -UpToVersion 2026.44.0
   ```
2. **Brückenversion in beiden Repos veröffentlichen:**
   ```powershell
   .\Scripts\Release.ps1 -Version 2026.46.0 -AlsoPublishToLegacyRepo
   ```
   Das Skript legt das Release erst in `TimeTrack-Releases` und danach in `TimeTrack` an.
3. **Ab der nächsten Version** wie gewohnt ohne `-AlsoPublishToLegacyRepo`, nur noch im
   Release-Repo.

Was bei den Nutzern passiert: Ein Client ≤ 2026.45.0 sieht in `TimeTrack` die Brückenversion,
installiert sie über den gewohnten Weg und sucht ab da nur noch in `TimeTrack-Releases`. Von dort
kommen alle weiteren Updates.

**Das alte Repo `jkkSofting/TimeTrack` muss dafür öffentlich bleiben und das Release der
Brückenversion behalten, solange es Clients ≤ 2026.45.0 gibt.** Wird es auf privat gestellt oder
das Release gelöscht, bekommen diese Clients kein Update mehr und bleiben auf ihrer Version
stehen; sie müssten den Installer dann von Hand aus dem Release-Repo holen.

## 0. Neuigkeiten und Hilfe pflegen

Die Seite „Hilfe & Neuigkeiten“ in der App zeigt, was sich mit jeder Version geändert hat, und
erklärt alle Bereiche. Beides wird **mit jedem Feature** gepflegt, nicht erst beim Release:

- `Helpers/Changelog.cs` — jede für Nutzer sichtbare Änderung als `Neu(...)`, `Verbessert(...)`
  oder `Behoben(...)`, neueste Version oben. Noch nicht veröffentlichte Änderungen kommen in einen
  Eintrag für die kommende Version (Format wie der Git-Tag, z. B. `2026.44.0`); die App zeigt ihn
  in Entwicklungs-Builds als „In Arbeit“. Derselbe Changelog füllt auch die README im Release-Repo.
- `Helpers/HelpContent.cs` — das Hilfethema der betroffenen Seite anpassen bzw. für neue Seiten
  ein Thema ergänzen.

Nach einem Update zeigt die App einen Punkt am Navigationseintrag und einen Tray-Hinweis, bis die
Neuigkeiten geöffnet wurden.

## 1. Version an beiden Stellen erhöhen

Beide Dateien müssen exakt dieselbe Versionsnummer bekommen (Format `Jahr.Minor.Patch`, z. B.
`2026.39.2`; für einen Hotfix einer schon veröffentlichten Version geht auch eine vierte Stelle,
z. B. `2026.47.1.1`):

- `Properties/AssemblyInfo.cs` — `AssemblyVersion` **und** `AssemblyFileVersion`
- `Installer/installer.iss` — `#define MyAppVersion "..."`

## 2. Release-Build erzeugen

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe"
& $msbuild "Zeitmanagement.sln" /t:Restore /p:RestorePackagesConfig=true
& $msbuild "Zeitmanagement.sln" /t:Build /p:Configuration=Release
```

Der Restore holt die NuGet-Pakete (`packages\` liegt nicht im Git, z. B. PDFsharp/MigraDoc für den Tätigkeitsnachweis); er
geht bei diesem `packages.config`-Projekt nur über die Projektmappe. Erzeugt `bin\Release\Zeitmanagement.exe` mit der neuen Version.

## 3. Installer kompilieren

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "Installer\installer.iss"
```

Erzeugt `Installer\Output\TimeTracker-<Version>.exe` (packt `bin\Release\*`, also Schritt 2
vorher nicht vergessen).

## 4. Committen, taggen, pushen

Das geht ins Quell-Repo `jkkSofting/TimeTrack`. Der Git-Tag muss **exakt** der Versionsnummer
entsprechen (keine `v`-Vorsilbe nötig, kein `Version_`-Präfix wie bei den alten lokalen Tags —
`UpdateChecker` entfernt nur ein optionales führendes `v`/`V` und parst den Rest über
`System.Version.TryParse`):

```bash
git add Properties/AssemblyInfo.cs Installer/installer.iss
git commit -m "Bump version to <Version>"
git tag -a <Version> -m "TimeTrack <Version>"
git push origin main
git push origin <Version>
```

## 5. GitHub Release anlegen (der eigentlich entscheidende Schritt)

Ein reiner Git-Tag reicht **nicht** — `/releases/latest` liefert nur etwas, wenn zu dem Tag
auch ein **Release**-Objekt existiert. Das Release gehört ins Repo **`jkkSofting/TimeTrack-Releases`**
(nicht ins Quell-Repo): *Releases → Draft a new release*, als Tag `<Version>` angeben (wird dort
beim Veröffentlichen angelegt), und die in Schritt 3 gebaute `TimeTracker-<Version>.exe` als Asset
hochladen. Dabei beachten:

- **Nicht** als *pre-release* markieren — `UpdateChecker` überspringt Pre-Releases bewusst.
- Es muss mindestens ein Asset geben, dessen Dateiname auf `.exe` endet — genau das lädt der
  Client herunter und startet es. Bei mehreren `.exe`-Assets wird das erste genommen, also nur
  eins pro Release hochladen.
- Das Release-Repo braucht mindestens einen Commit (die README), sonst lässt sich dort kein Tag
  anlegen.
- Alternativ per API möglich, siehe `Scripts\Release.ps1`.

## 6. README im Release-Repo aktualisieren

```powershell
.\Scripts\Update-ReleasesReadme.ps1 -UpToVersion <Version>
```

Trägt die neue Version und den Changelog bis einschließlich `<Version>` in die README des
Release-Repos ein und pusht sie (`Release.ps1` macht das automatisch). Änderungen an der Anleitung
gehören in `ReleasesRepo\README.template.md`; danach das Skript mit der zuletzt veröffentlichten
Version laufen lassen. Mit `-OutFile <Datei>` wird die README nur lokal erzeugt, zur Vorschau.

## Was der Client danach automatisch macht

- Prüft 5s nach dem Start und danach alle 10 Minuten im Hintergrund (abschaltbar in den
  Einstellungen), sowie sofort per Klick auf "Nach Updates suchen".
- Zeigt bei einer neueren Version einen Tray-Hinweis; die eigentliche Installation stößt der
  Nutzer bewusst über "Update installieren" in den Einstellungen an.
- Lädt die `.exe` herunter und startet sie mit
  `/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS /LANG=german`
  — kein Wizard, keine Dialoge. Die App beendet sich selbst, der Installer ersetzt die Dateien
  und startet die App danach automatisch neu (Inno-Setup-`[Run]`-Eintrag ohne `skipifsilent`).
- Einzige verbleibende Interaktion: **ein** UAC-Prompt, weil der Installer nach
  `Program Files` schreibt und Administratorrechte anfordert (`PrivilegesRequired` ist nicht auf
  `lowest` gesetzt). Um auch das zu vermeiden, müsste die Installation auf einen
  Pro-Benutzer-Pfad umgestellt werden — das ist bisher nicht gemacht.
