#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installiert den InputFusion-Client als Windows-Dienst (Entwicklungsstand bis zum WiX-Installer).

.DESCRIPTION
    Kopiert die Client-Dateien nach Programme\InputFusion\Client, schreibt die Konfiguration nach
    %ProgramData%\InputFusion\client.json (ACL nur SYSTEM und Administratoren), legt den Dienst
    "InputFusionClient" an (LocalSystem, Start automatisch, Neustart nach Absturz) und startet ihn.
    Ein vorhandener Dienst wird vorher gestoppt und ersetzt. Läuft auch unter Windows PowerShell 5.1.

    Der Client kann mehrere PCs kennen, etwa zu Hause und im Büro, und verbindet sich mit dem, der gerade
    erreichbar ist. Das Skript trägt einen PC ein: Steht einer mit demselben Namen schon in client.json,
    wird er ersetzt, die übrigen bleiben. Für den nächsten PC das Skript erneut aufrufen.

.PARAMETER HostName
    Rechnername des PCs mit dem InputFusion-Host, wird per DNS aufgelöst.

.PARAMETER Key
    Gemeinsamer Schlüssel aus den Einstellungen des Hosts (Reiter Verbindung), mindestens 16 Zeichen.

.PARAMETER Ip
    Optionale feste IP des PCs, die zusätzlich zum DNS-Ergebnis versucht wird.

.PARAMETER Port
    TCP-Port des Hosts, wie in dessen Einstellungen (Reiter Verbindung). Standard 15111.

.PARAMETER Source
    Ordner mit InputFusion.Client.exe. Standard: der Ordner dieses Skripts, falls die exe dort liegt,
    sonst die Build-Ausgabe im Repo (_out\InputFusion.Client).

.PARAMETER Replace
    Die übrigen PCs aus client.json verwerfen; danach steht nur dieser eine drin.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-client.ps1 -HostName PC-NAME -Key <Schlüssel>
#>
param(
    [Parameter(Mandatory = $true)][string]$HostName,
    [Parameter(Mandatory = $true)][string]$Key,
    [string]$Ip,
    [ValidateRange(1, 65535)][int]$Port = 15111,
    [string]$Source,
    [switch]$Replace
)

$ErrorActionPreference = 'Stop'
if ($Key.Length -lt 16) { throw 'Der Schlüssel braucht mindestens 16 Zeichen.' }
$serviceName = 'InputFusionClient'
$target = Join-Path $env:ProgramFiles 'InputFusion\Client'
$configDir = Join-Path $env:ProgramData 'InputFusion'

if (-not $Source) {
    if (Test-Path (Join-Path $PSScriptRoot 'InputFusion.Client.exe')) { $Source = $PSScriptRoot }
    else { $Source = Join-Path $PSScriptRoot '..\_out\InputFusion.Client' }
}
$Source = (Resolve-Path $Source).Path
if (-not (Test-Path (Join-Path $Source 'InputFusion.Client.exe'))) {
    throw "InputFusion.Client.exe nicht gefunden in $Source"
}

# PCs für client.json: die vorhandenen behalten, einen mit demselben Namen ersetzen. Vor dem Stoppen des Dienstes,
# damit eine unlesbare Datei abbricht, bevor etwas verändert ist. Bis 0.11 stand dort ein PC flach statt unter "hosts".
$configFile = Join-Path $configDir 'client.json'
$entry = [ordered]@{ host = $HostName; key = $Key }
if ($Ip) { $entry.ip = $Ip }
$entry.port = $Port
$hosts = New-Object System.Collections.Generic.List[object]
if (-not $Replace -and (Test-Path $configFile)) {
    try { $old = [IO.File]::ReadAllText($configFile) | ConvertFrom-Json }
    catch { throw "$configFile ist nicht lesbar ($($_.Exception.Message)). Von Hand korrigieren oder mit -Replace neu schreiben." }
    $oldHosts = if ($old.hosts) { @($old.hosts) } elseif ($old.host) { @($old) } else { @() }
    foreach ($h in $oldHosts) {
        if ($h.host -eq $HostName) { continue }
        $kept = [ordered]@{ host = $h.host; key = $h.key }
        if ($h.ip) { $kept.ip = $h.ip }
        $kept.port = if ($h.port) { [int]$h.port } else { 15111 }
        $hosts.Add($kept)
    }
}
$hosts.Insert(0, $entry)

# Vorhandenen Dienst stoppen, sonst sind die Dateien gesperrt. Der Injector läuft als eigener Prozess
# weiter, bis der Dienst ihn beendet; zur Sicherheit auch ihn beenden.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stoppe vorhandenen Dienst $serviceName ..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}
Get-Process -Name 'InputFusion.Client' -ErrorAction SilentlyContinue | Stop-Process -Force
# Offene Einstellungen aus dem Installationsordner sperrten sonst ihre exe; die des Hosts anderswo bleiben offen.
Get-Process -Name 'InputFusion.Settings' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Split-Path $_.Path -Parent) -eq $target } |
    Stop-Process -Force

# Dateien kopieren. Programme\ ist nur für Administratoren beschreibbar; das ist wichtig, weil der Dienst
# die exe als SYSTEM ausführt.
Write-Host "Kopiere $Source nach $target ..."
New-Item -ItemType Directory -Path $target -Force | Out-Null
Get-ChildItem -Path $target -Force | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $Source '*') -Destination $target -Recurse -Force

# Konfiguration schreiben, ACL nur SYSTEM und Administratoren (Vererbung aus). Die Datei entsteht gleich mit der
# ACL; nachträglich gesetzt wäre sie mit dem Schlüssel kurz für alle Benutzer lesbar, weil ProgramData Lesen vererbt.
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
$bytes = (New-Object System.Text.UTF8Encoding $false).GetBytes(([ordered]@{ hosts = $hosts.ToArray() } | ConvertTo-Json -Depth 4))

$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in 'S-1-5-18', 'S-1-5-32-544') {   # SYSTEM, Administratoren
    $identity = New-Object System.Security.Principal.SecurityIdentifier $sid
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule $identity, 'FullControl', 'Allow'
    $acl.AddAccessRule($rule)
}
Remove-Item -Path $configFile -Force -ErrorAction SilentlyContinue
$stream = New-Object System.IO.FileStream($configFile, [System.IO.FileMode]::CreateNew,
    [System.Security.AccessControl.FileSystemRights]::Write, [System.IO.FileShare]::None, 4096,
    [System.IO.FileOptions]::None, $acl)
try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }

# Ohne Schlüssel ausgeben, die Konsole landet leicht in Protokollen oder Chats.
Write-Host "Konfiguration: $configFile"
foreach ($h in $hosts) {
    Write-Host "  Host: $($h.host)$(if ($h.ip) { ", IP $($h.ip)" }), Port $($h.port), Schlüssel $($h.key.Length) Zeichen (nicht angezeigt)"
}

# Dienst anlegen oder umkonfigurieren.
$binary = '"' + (Join-Path $target 'InputFusion.Client.exe') + '"'
if ($existing) {
    & sc.exe config $serviceName binPath= $binary start= auto obj= LocalSystem | Out-Null
} else {
    New-Service -Name $serviceName -BinaryPathName $binary -DisplayName 'InputFusion Client' `
        -Description 'Empfängt Maus und Tastatur vom InputFusion-Host und spielt sie in der aktiven Sitzung ab.' `
        -StartupType Automatic | Out-Null
}
# Nach Absturz neu starten: 5 s, 5 s, dann 30 s; Zähler nach einem Tag zurücksetzen.
& sc.exe failure $serviceName reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc.exe failure fehlgeschlagen ($LASTEXITCODE)" }

Write-Host "Starte Dienst $serviceName ..."
Start-Service -Name $serviceName
Get-Service -Name $serviceName | Format-Table -AutoSize Name, Status, StartType
Write-Host 'Protokoll: Ereignisanzeige > Windows-Protokolle > Anwendung, Quelle InputFusion.Client'
