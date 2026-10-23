#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Installiert den InputFusion-Client als Windows-Dienst (Entwicklungsstand bis zum WiX-Installer).

.DESCRIPTION
    Kopiert die Client-Dateien nach Programme\InputFusion\Client, schreibt die Konfiguration nach
    %ProgramData%\InputFusion\client.json (ACL nur SYSTEM und Administratoren), legt den Dienst
    "InputFusionClient" an (LocalSystem, Start automatisch, Neustart nach Absturz) und startet ihn.
    Ein vorhandener Dienst wird vorher gestoppt und ersetzt. Läuft auch unter Windows PowerShell 5.1.

.PARAMETER HostName
    Rechnername des PCs mit dem InputFusion-Host, wird per DNS aufgelöst.

.PARAMETER Ip
    Optionale feste IP des PCs, die zusätzlich zum DNS-Ergebnis versucht wird.

.PARAMETER Source
    Ordner mit InputFusion.Client.exe. Standard: der Ordner dieses Skripts, falls die exe dort liegt,
    sonst die Build-Ausgabe im Repo (_out\InputFusion.Client).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-client.ps1 -HostName PC-NAME
#>
param(
    [Parameter(Mandatory = $true)][string]$HostName,
    [string]$Ip,
    [string]$Source
)

$ErrorActionPreference = 'Stop'
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

# Vorhandenen Dienst stoppen, sonst sind die Dateien gesperrt. Der Injector läuft als eigener Prozess
# weiter, bis der Dienst ihn beendet; zur Sicherheit auch ihn beenden.
$existing = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stoppe vorhandenen Dienst $serviceName ..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
}
Get-Process -Name 'InputFusion.Client' -ErrorAction SilentlyContinue | Stop-Process -Force

# Dateien kopieren. Programme\ ist nur für Administratoren beschreibbar; das ist wichtig, weil der Dienst
# die exe als SYSTEM ausführt.
Write-Host "Kopiere $Source nach $target ..."
New-Item -ItemType Directory -Path $target -Force | Out-Null
Get-ChildItem -Path $target -Force | Remove-Item -Recurse -Force
Copy-Item -Path (Join-Path $Source '*') -Destination $target -Recurse -Force

# Konfiguration schreiben, ACL nur SYSTEM und Administratoren (Vererbung aus).
New-Item -ItemType Directory -Path $configDir -Force | Out-Null
$configFile = Join-Path $configDir 'client.json'
$config = [ordered]@{ host = $HostName }
if ($Ip) { $config.ip = $Ip }
$json = $config | ConvertTo-Json
[System.IO.File]::WriteAllText($configFile, $json, (New-Object System.Text.UTF8Encoding $false))

$acl = New-Object System.Security.AccessControl.FileSecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($sid in 'S-1-5-18', 'S-1-5-32-544') {   # SYSTEM, Administratoren
    $identity = New-Object System.Security.Principal.SecurityIdentifier $sid
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule $identity, 'FullControl', 'Allow'
    $acl.AddAccessRule($rule)
}
Set-Acl -Path $configFile -AclObject $acl
Write-Host "Konfiguration: $configFile"
Write-Host $json

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
