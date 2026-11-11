#Requires -Version 5.1
<#
    Führt den kompletten Release-Ablauf aus RELEASING.md aus:
    Version bumpen -> Release-Build -> Installer kompilieren -> committen/taggen/pushen
    -> GitHub Release samt Installer-.exe anlegen -> README im Release-Repo aktualisieren.

    Commit und Tag gehen ins Quell-Repo (TimeTrack), das Release samt Installer liegt im oeffentlichen
    Repo TimeTrack-Releases - dort sucht der UpdateChecker der App.

    Beispiel:
        .\Scripts\Release.ps1 -Version 2026.40.0
        .\Scripts\Release.ps1 -Version 2026.40.0 -Notes "Bugfix fuer die Erinnerungen"
        .\Scripts\Release.ps1 -Version 2026.46.0 -AlsoPublishToLegacyRepo   # Bruecken-Version, siehe RELEASING.md
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [string]$Notes = "",

    # Der lokal gecachte Git-Login fuer github.com weicht vom Repo-Owner ab
    # (janick3110 vs. jkkSofting) - deswegen wird explizit als dieser User gepusht.
    [string]$PushUser = "jkkSofting",

    [string]$RepoOwner = "jkkSofting",

    # Quell-Repo: hier landen Commit und Tag
    [string]$RepoName = "TimeTrack",

    # Oeffentliches Repo, in dem die Installer als GitHub Release liegen
    [string]$ReleasesRepoName = "TimeTrack-Releases",

    # Legt das Release zusaetzlich im Quell-Repo an. Clients bis 2026.45.0 suchen noch dort, deshalb muss
    # die erste Version mit dem neuen UpdateChecker (die Bruecken-Version) in beiden Repos liegen.
    [switch]$AlsoPublishToLegacyRepo
)

$ErrorActionPreference = "Stop"

function Find-Tool {
    param([string]$DefaultPath, [string]$SearchRoot, [string]$FileName)

    if (Test-Path $DefaultPath) { return $DefaultPath }

    $found = Get-ChildItem -Path $SearchRoot -Filter $FileName -Recurse -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $found) {
        throw "$FileName wurde nicht gefunden (weder unter $DefaultPath noch unter $SearchRoot)."
    }
    return $found
}

function Invoke-NativeCommand {
    param([string]$FilePath, [string[]]$ArgumentList, [string]$StepName)

    Write-Host "==> $StepName" -ForegroundColor Cyan
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$StepName ist fehlgeschlagen (Exit code $LASTEXITCODE)."
    }
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$assemblyInfoPath = Join-Path $repoRoot "Properties\AssemblyInfo.cs"
$changelogPath = Join-Path $repoRoot "Helpers\Changelog.cs"
$issPath = Join-Path $repoRoot "Installer\installer.iss"
$slnPath = Join-Path $repoRoot "Zeitmanagement.sln"
$installerExePath = Join-Path $repoRoot "Installer\Output\TimeTracker-$Version.exe"
$pushUrl = "https://$PushUser@github.com/$RepoOwner/$RepoName.git"

# --- 0. Neuigkeiten pruefen -------------------------------------------------

# Jede Version braucht einen Eintrag in Helpers\Changelog.cs, sonst sehen die Nutzer auf der Seite
# "Hilfe & Neuigkeiten" nicht, was sich geaendert hat.
$changelog = Get-Content $changelogPath -Raw -Encoding UTF8
$entryPattern = 'new ChangelogRelease\("' + [regex]::Escape($Version) + '"(?<body>.*?)(?=new ChangelogRelease\(|\};)'
$entry = [regex]::Match($changelog, $entryPattern, 'Singleline')
if (-not $entry.Success) {
    throw "Helpers\Changelog.cs hat keinen Eintrag fuer $Version. Erst die Neuigkeiten eintragen, siehe RELEASING.md."
}

# Im Release-Repo muss es schon einen Commit geben (die README), sonst kann GitHub dort keinen Tag fuer das
# Release anlegen. Besser hier abbrechen als nach Build und Push.
try {
    Invoke-RestMethod -Uri "https://api.github.com/repos/$RepoOwner/$ReleasesRepoName/branches/main" `
        -Headers @{ "User-Agent" = "TimeTrack-Release-Script" } | Out-Null
}
catch {
    throw "Das Release-Repo $RepoOwner/$ReleasesRepoName hat keinen Branch 'main'. Erst .\Scripts\Update-ReleasesReadme.ps1 ausfuehren."
}

# Ohne -Notes werden die Release-Notes auf GitHub aus dem Changelog-Eintrag erzeugt
if (-not $Notes) {
    $changes = [regex]::Matches($entry.Groups['body'].Value, '(?<kind>Neu|Verbessert|Behoben)\("(?<text>[^"]*)"\)')
    $Notes = ($changes | ForEach-Object { "- **$($_.Groups['kind'].Value):** $($_.Groups['text'].Value)" }) -join "`n"
}

# --- 1. Version bumpen ------------------------------------------------------

Write-Host "==> Version auf $Version setzen" -ForegroundColor Cyan

$assemblyInfo = Get-Content $assemblyInfoPath -Raw
$assemblyInfo = $assemblyInfo -replace 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version`")"
$assemblyInfo = $assemblyInfo -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$Version`")"
Set-Content -Path $assemblyInfoPath -Value $assemblyInfo -NoNewline

$iss = Get-Content $issPath -Raw
$iss = $iss -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$Version`""
Set-Content -Path $issPath -Value $iss -NoNewline

# Der Changelog-Eintrag bekommt das Datum des Releases
$today = Get-Date
$changelog = $changelog -replace `
    ('(new ChangelogRelease\("' + [regex]::Escape($Version) + '", )new DateTime\(\d+, \d+, \d+\)'), `
    ('${1}' + "new DateTime($($today.Year), $($today.Month), $($today.Day))")
Set-Content -Path $changelogPath -Value $changelog -NoNewline -Encoding UTF8

# --- 2. Release-Build --------------------------------------------------------

$msbuild = Find-Tool `
    -DefaultPath "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" `
    -SearchRoot "C:\Program Files\Microsoft Visual Studio" `
    -FileName "MSBuild.exe"

# Erst die NuGet-Pakete holen (packages\ liegt nicht im Git, z. B. PDFsharp/MigraDoc für den Tätigkeitsnachweis),
# dann bauen. Bei einem Projekt mit packages.config geht der Restore nur über die Projektmappe.
Invoke-NativeCommand -FilePath $msbuild `
    -ArgumentList @($slnPath, "/t:Restore", "/p:RestorePackagesConfig=true", "/v:minimal", "/nologo") `
    -StepName "NuGet-Restore"

Invoke-NativeCommand -FilePath $msbuild `
    -ArgumentList @($slnPath, "/t:Build", "/p:Configuration=Release", "/v:minimal", "/nologo") `
    -StepName "Release-Build"

# --- 3. Installer kompilieren ------------------------------------------------

$iscc = Find-Tool `
    -DefaultPath "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" `
    -SearchRoot "C:\Program Files (x86)" `
    -FileName "ISCC.exe"

Invoke-NativeCommand -FilePath $iscc -ArgumentList @($issPath) -StepName "Installer kompilieren"

if (-not (Test-Path $installerExePath)) {
    throw "Erwartete Installer-Datei wurde nicht gefunden: $installerExePath"
}

# --- 4. Committen, taggen, pushen --------------------------------------------

Push-Location $repoRoot
try {
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("add", "Properties/AssemblyInfo.cs", "Installer/installer.iss", "Helpers/Changelog.cs") -StepName "git add"
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("commit", "-m", "Bump version to $Version") -StepName "git commit"
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("tag", "-a", $Version, "-m", "TimeTrack $Version") -StepName "git tag"
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("push", $pushUrl, "main") -StepName "git push main"
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("push", $pushUrl, $Version) -StepName "git push tag"
}
finally {
    Pop-Location
}

# --- 5. GitHub Release anlegen + Installer hochladen -------------------------

Write-Host "==> GitHub-Zugangsdaten aus dem Credential Manager lesen" -ForegroundColor Cyan

$credentialInput = "protocol=https`nhost=github.com`nusername=$PushUser`n`n"
$credentialOutput = $credentialInput | git credential fill
$passwordLine = $credentialOutput | Where-Object { $_ -like "password=*" } | Select-Object -First 1
if (-not $passwordLine) {
    throw "Konnte kein GitHub-Token fuer $PushUser aus dem Credential Manager lesen."
}
$token = $passwordLine.Substring("password=".Length)

$headers = @{
    Authorization = "token $token"
    Accept        = "application/vnd.github+json"
    "User-Agent"  = "TimeTrack-Release-Script"
}

$releaseBody = @{
    tag_name   = $Version
    name       = $Version
    body       = $Notes
    draft      = $false
    prerelease = $false
} | ConvertTo-Json

# Als UTF-8-Bytes senden, sonst gehen Umlaute aus dem Changelog unter Windows PowerShell kaputt
$releaseBodyBytes = [System.Text.Encoding]::UTF8.GetBytes($releaseBody)
$assetName = Split-Path $installerExePath -Leaf

# Erst das Release-Repo, dann das alte: Sobald Clients im alten Repo die Bruecken-Version sehen, muss sie
# im neuen schon liegen. Im Release-Repo legt GitHub den Tag selbst an, im Quell-Repo gibt es ihn seit Schritt 4.
$releaseRepos = @($ReleasesRepoName)
if ($AlsoPublishToLegacyRepo) { $releaseRepos += $RepoName }

foreach ($releaseRepo in $releaseRepos) {
    Write-Host "==> GitHub Release $Version in $RepoOwner/$releaseRepo anlegen" -ForegroundColor Cyan

    $release = Invoke-RestMethod -Method Post `
        -Uri "https://api.github.com/repos/$RepoOwner/$releaseRepo/releases" `
        -Headers $headers -ContentType "application/json; charset=utf-8" `
        -Body $releaseBodyBytes

    Write-Host "==> $assetName nach $releaseRepo hochladen" -ForegroundColor Cyan

    $uploadUri = "https://uploads.github.com/repos/$RepoOwner/$releaseRepo/releases/$($release.id)/assets?name=$assetName"
    $asset = Invoke-RestMethod -Method Post -Uri $uploadUri -Headers $headers `
        -ContentType "application/octet-stream" -InFile $installerExePath

    Write-Host ""
    Write-Host "Release: $($release.html_url)" -ForegroundColor Green
    Write-Host "Asset:   $($asset.browser_download_url)" -ForegroundColor Green
}

# --- 6. README im Release-Repo aktualisieren ---------------------------------

try {
    & (Join-Path $PSScriptRoot "Update-ReleasesReadme.ps1") `
        -UpToVersion $Version -PushUser $PushUser -RepoOwner $RepoOwner -RepoName $ReleasesRepoName
}
catch {
    Write-Warning "Das Release ist veroeffentlicht, nur die README im Release-Repo ist nicht aktualisiert. Nachholen mit: .\Scripts\Update-ReleasesReadme.ps1 -UpToVersion $Version"
    throw
}

Write-Host ""
Write-Host "Fertig." -ForegroundColor Green
