#Requires -Version 5.1
<#
    Führt den kompletten Release-Ablauf aus RELEASING.md aus:
    Version bumpen -> Release-Build -> Installer kompilieren -> committen/taggen/pushen
    -> GitHub Release samt Installer-.exe anlegen.

    Beispiel:
        .\Scripts\Release.ps1 -Version 2026.40.0
        .\Scripts\Release.ps1 -Version 2026.40.0 -Notes "Bugfix fuer die Erinnerungen"
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
    [string]$RepoName = "TimeTrack"
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
$issPath = Join-Path $repoRoot "Installer\installer.iss"
$slnPath = Join-Path $repoRoot "Zeitmanagement.sln"
$installerExePath = Join-Path $repoRoot "Installer\Output\TimeTracker-$Version.exe"
$pushUrl = "https://$PushUser@github.com/$RepoOwner/$RepoName.git"

# --- 1. Version bumpen ------------------------------------------------------

Write-Host "==> Version auf $Version setzen" -ForegroundColor Cyan

$assemblyInfo = Get-Content $assemblyInfoPath -Raw
$assemblyInfo = $assemblyInfo -replace 'AssemblyVersion\("[^"]+"\)', "AssemblyVersion(`"$Version`")"
$assemblyInfo = $assemblyInfo -replace 'AssemblyFileVersion\("[^"]+"\)', "AssemblyFileVersion(`"$Version`")"
Set-Content -Path $assemblyInfoPath -Value $assemblyInfo -NoNewline

$iss = Get-Content $issPath -Raw
$iss = $iss -replace '#define MyAppVersion "[^"]+"', "#define MyAppVersion `"$Version`""
Set-Content -Path $issPath -Value $iss -NoNewline

# --- 2. Release-Build --------------------------------------------------------

$msbuild = Find-Tool `
    -DefaultPath "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe" `
    -SearchRoot "C:\Program Files\Microsoft Visual Studio" `
    -FileName "MSBuild.exe"

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
    Invoke-NativeCommand -FilePath "git" -ArgumentList @("add", "Properties/AssemblyInfo.cs", "Installer/installer.iss") -StepName "git add"
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

Write-Host "==> GitHub Release $Version anlegen" -ForegroundColor Cyan

$releaseBody = @{
    tag_name   = $Version
    name       = $Version
    body       = $Notes
    draft      = $false
    prerelease = $false
} | ConvertTo-Json

$release = Invoke-RestMethod -Method Post `
    -Uri "https://api.github.com/repos/$RepoOwner/$RepoName/releases" `
    -Headers $headers -ContentType "application/json" -Body $releaseBody

$assetName = Split-Path $installerExePath -Leaf
Write-Host "==> $assetName hochladen" -ForegroundColor Cyan

$uploadUri = "https://uploads.github.com/repos/$RepoOwner/$RepoName/releases/$($release.id)/assets?name=$assetName"
$asset = Invoke-RestMethod -Method Post -Uri $uploadUri -Headers $headers `
    -ContentType "application/octet-stream" -InFile $installerExePath

Write-Host ""
Write-Host "Fertig. Release: $($release.html_url)" -ForegroundColor Green
Write-Host "Asset:   $($asset.browser_download_url)" -ForegroundColor Green
