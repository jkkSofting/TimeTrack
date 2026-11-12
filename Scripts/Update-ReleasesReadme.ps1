#Requires -Version 5.1
<#
    Erzeugt die README.md fuer das oeffentliche Release-Repository (jkkSofting/TimeTrack-Releases)
    aus ReleasesRepo\README.template.md und dem Changelog in Helpers\Changelog.cs und pusht sie dorthin.

    Wird von Release.ps1 nach jedem Release aufgerufen, kann aber auch allein laufen - z.B. um die
    Anleitung zu aendern, ohne eine neue Version zu veroeffentlichen. Die README im Release-Repo nie von
    Hand bearbeiten, sie wird bei jedem Lauf aus der Vorlage neu erzeugt.

    Beispiel:
        .\Scripts\Update-ReleasesReadme.ps1 -UpToVersion 2026.44.0
        .\Scripts\Update-ReleasesReadme.ps1 -UpToVersion 2026.44.0 -OutFile C:\Temp\README.md   # nur ansehen, nichts pushen
#>
param(
    # Nur Changelog-Eintraege bis einschliesslich dieser Version aufnehmen. Noch nicht veroeffentlichte
    # Versionen (die in Changelog.cs schon stehen) gehoeren nicht in die oeffentliche README.
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$UpToVersion,

    # Schreibt die README nur in diese Datei, ohne das Release-Repo anzufassen
    [string]$OutFile = "",

    # Der lokal gecachte Git-Login fuer github.com weicht vom Repo-Owner ab
    # (janick3110 vs. jkkSofting) - deswegen wird explizit als dieser User gepusht.
    [string]$PushUser = "jkkSofting",

    [string]$RepoOwner = "jkkSofting",
    [string]$RepoName = "TimeTrack-Releases"
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$StepName)

    Write-Host "==> $StepName" -ForegroundColor Cyan
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$StepName ist fehlgeschlagen (Exit code $LASTEXITCODE)."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$changelogPath = Join-Path $repoRoot "Helpers\Changelog.cs"
$templatePath = Join-Path $repoRoot "ReleasesRepo\README.template.md"
$cloneUrl = "https://$PushUser@github.com/$RepoOwner/$RepoName.git"
$german = [System.Globalization.CultureInfo]::GetCultureInfo("de-DE")
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# --- Changelog aus Changelog.cs lesen ---------------------------------------

$changelog = Get-Content $changelogPath -Raw -Encoding UTF8
$releasePattern = 'new ChangelogRelease\("(?<version>[^"]+)", new DateTime\((?<y>\d+), (?<m>\d+), (?<d>\d+)\), "(?<title>[^"]*)",(?<body>.*?)(?=new ChangelogRelease\(|\};)'
$changePattern = '(?<kind>Neu|Verbessert|Behoben)\("(?<text>[^"]*)"\)'

$releases = foreach ($match in [regex]::Matches($changelog, $releasePattern, 'Singleline')) {
    $version = [version]$match.Groups['version'].Value
    if ($version -gt [version]$UpToVersion) { continue }

    [pscustomobject]@{
        Version = $version
        Date    = New-Object DateTime ([int]$match.Groups['y'].Value), ([int]$match.Groups['m'].Value), ([int]$match.Groups['d'].Value)
        Title   = $match.Groups['title'].Value
        Changes = [regex]::Matches($match.Groups['body'].Value, $changePattern)
    }
}
$releases = @($releases | Sort-Object Version -Descending)

if ($releases.Count -eq 0) {
    throw "Helpers\Changelog.cs hat keine Eintraege bis Version $UpToVersion."
}

$sections = foreach ($release in $releases) {
    $heading = "### $($release.Version)"
    if ($release.Title) { $heading += " - $($release.Title)" }

    $lines = @($heading, "", "*$($release.Date.ToString('d. MMMM yyyy', $german))*", "")
    foreach ($change in $release.Changes) {
        $lines += "- **$($change.Groups['kind'].Value):** $($change.Groups['text'].Value)"
    }
    $lines -join "`n"
}
$changelogMarkdown = $sections -join "`n`n"

# --- README aus der Vorlage erzeugen ----------------------------------------

$readme = Get-Content $templatePath -Raw -Encoding UTF8
$readme = $readme.Replace("{{LATEST_VERSION}}", $releases[0].Version.ToString())
$readme = $readme.Replace("{{CHANGELOG}}", $changelogMarkdown)
$readme = $readme -replace "`r`n", "`n"

if ($readme -match '\{\{[A-Z_]+\}\}') {
    throw "In der README ist ein Platzhalter uebrig geblieben: $($Matches[0])"
}

if ($OutFile) {
    [System.IO.File]::WriteAllText($OutFile, $readme, $utf8NoBom)
    Write-Host "README geschrieben nach $OutFile (Version $($releases[0].Version), $($releases.Count) Changelog-Eintraege)." -ForegroundColor Green
    return
}

# --- In das Release-Repo committen und pushen -------------------------------

$cloneDir = Join-Path ([System.IO.Path]::GetTempPath()) ("TimeTrack-Releases-" + [guid]::NewGuid().ToString("N"))
try {
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("clone", $cloneUrl, $cloneDir) -StepName "Release-Repo klonen"
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("-C", $cloneDir, "checkout", "-B", "main") -StepName "Branch main"

    [System.IO.File]::WriteAllText((Join-Path $cloneDir "README.md"), $readme, $utf8NoBom)
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("-C", $cloneDir, "add", "README.md") -StepName "git add"

    & git -C $cloneDir diff --cached --quiet
    if ($LASTEXITCODE -eq 0) {
        Write-Host "README im Release-Repo ist bereits aktuell, nichts zu pushen." -ForegroundColor Green
        return
    }

    # Ein frisch angelegtes Repo hat noch keinen Commit
    $hasCommits = [bool](& git -C $cloneDir rev-list -n 1 --all)
    $message = if ($hasCommits) { "Update README (Changelog bis $($releases[0].Version))" } else { "Add README with installation guide and changelog" }

    Invoke-NativeCommand -FilePath "git" -ArgumentList @("-C", $cloneDir, "commit", "-m", $message) -StepName "git commit"
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("-C", $cloneDir, "push", "-u", "origin", "main") -StepName "git push"

    Write-Host "README aktualisiert: https://github.com/$RepoOwner/$RepoName" -ForegroundColor Green
}
finally {
    if (Test-Path $cloneDir) { Remove-Item $cloneDir -Recurse -Force }
}
