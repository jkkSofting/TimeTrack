# Eine neue Version veröffentlichen

Damit `Helpers/UpdateChecker.cs` bei installierten Clients automatisch eine neue Version
erkennt, müssen bei jedem Release **alle** der folgenden Schritte durchgeführt werden. Der
Update-Check fragt `GET https://api.github.com/repos/jkkSofting/TimeTrack/releases/latest` ab
und vergleicht dessen `tag_name` mit der lokal installierten `AssemblyVersion`.

## Kurzform: `Scripts\Release.ps1`

Führt alle Schritte unten automatisch aus (Version bumpen, Release-Build, Installer
kompilieren, committen/taggen/pushen, GitHub Release samt `.exe`-Anhang anlegen):

```powershell
.\Scripts\Release.ps1 -Version 2026.40.0
.\Scripts\Release.ps1 -Version 2026.40.0 -Notes "Kurze Beschreibung der Änderungen"
```

Das Skript bricht ab, wenn `Helpers/Changelog.cs` keinen Eintrag für die Version hat (siehe
Schritt 0), setzt das Datum dieses Eintrags auf den Release-Tag und erzeugt ohne `-Notes` die
Release-Notes auf GitHub aus dem Eintrag.

Voraussetzungen: Visual Studio (MSBuild), Inno Setup 6 (`ISCC.exe`), und ein im Git Credential
Manager gecachter GitHub-Login für `jkkSofting` (wird per `git credential fill` gelesen, siehe
`-PushUser`-Parameter). Bricht bei jedem fehlgeschlagenen Schritt sofort ab, statt halbfertig
weiterzumachen. Die manuellen Einzelschritte unten sind der Referenz-Ablauf, den das Skript
nachbildet.

## 0. Neuigkeiten und Hilfe pflegen

Die Seite „Hilfe & Neuigkeiten“ in der App zeigt, was sich mit jeder Version geändert hat, und
erklärt alle Bereiche. Beides wird **mit jedem Feature** gepflegt, nicht erst beim Release:

- `Helpers/Changelog.cs` — jede für Nutzer sichtbare Änderung als `Neu(...)`, `Verbessert(...)`
  oder `Behoben(...)`, neueste Version oben. Noch nicht veröffentlichte Änderungen kommen in einen
  Eintrag für die kommende Version (Format wie der Git-Tag, z. B. `2026.44.0`); die App zeigt ihn
  in Entwicklungs-Builds als „In Arbeit“.
- `Helpers/HelpContent.cs` — das Hilfethema der betroffenen Seite anpassen bzw. für neue Seiten
  ein Thema ergänzen.

Nach einem Update zeigt die App einen Punkt am Navigationseintrag und einen Tray-Hinweis, bis die
Neuigkeiten geöffnet wurden.

## 1. Version an beiden Stellen erhöhen

Beide Dateien müssen exakt dieselbe Versionsnummer bekommen (Format `Jahr.Minor.Patch`, z. B.
`2026.39.2`):

- `Properties/AssemblyInfo.cs` — `AssemblyVersion` **und** `AssemblyFileVersion`
- `Installer/installer.iss` — `#define MyAppVersion "..."`

## 2. Release-Build erzeugen

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" `
  "Zeitmanagement.sln" /t:Build /p:Configuration=Release
```

Erzeugt `bin\Release\Zeitmanagement.exe` mit der neuen Version.

## 3. Installer kompilieren

```powershell
& "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" "Installer\installer.iss"
```

Erzeugt `Installer\Output\TimeTracker-<Version>.exe` (packt `bin\Release\*`, also Schritt 2
vorher nicht vergessen).

## 4. Committen, taggen, pushen

Der Git-Tag muss **exakt** der Versionsnummer entsprechen (keine `v`-Vorsilbe nötig, kein
`Version_`-Präfix wie bei den alten lokalen Tags — `UpdateChecker` entfernt nur ein optionales
führendes `v`/`V` und parst den Rest über `System.Version.TryParse`):

```bash
git add Properties/AssemblyInfo.cs Installer/installer.iss
git commit -m "Bump version to <Version>"
git tag -a <Version> -m "TimeTrack <Version>"
git push origin main
git push origin <Version>
```

## 5. GitHub Release anlegen (der eigentlich entscheidende Schritt)

Ein reiner Git-Tag reicht **nicht** — `/releases/latest` liefert nur etwas, wenn zu dem Tag
auch ein **Release**-Objekt existiert. Auf GitHub: *Releases → Draft a new release*, den Tag
`<Version>` auswählen, und die in Schritt 3 gebaute `TimeTracker-<Version>.exe` als Asset
hochladen. Dabei beachten:

- **Nicht** als *pre-release* markieren — `UpdateChecker` überspringt Pre-Releases bewusst.
- Es muss mindestens ein Asset geben, dessen Dateiname auf `.exe` endet — genau das lädt der
  Client herunter und startet es. Bei mehreren `.exe`-Assets wird das erste genommen, also nur
  eins pro Release hochladen.
- Alternativ per API/`gh` möglich, siehe die Kommandos, die in diesem Chat für 2026.39.1 /
  2026.39.2 verwendet wurden.

## Was der Client danach automatisch macht

- Prüft 5s nach dem Start und danach alle 6h im Hintergrund (abschaltbar in den
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
