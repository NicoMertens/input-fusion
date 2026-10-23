#Requires -RunAsAdministrator
<#
.SYNOPSIS
    Entfernt den InputFusion-Client-Dienst und seine Dateien. Die Konfiguration bleibt, außer mit -RemoveConfig.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\uninstall-client.ps1 -RemoveConfig
#>
param(
    [switch]$RemoveConfig
)

$ErrorActionPreference = 'Stop'
$serviceName = 'InputFusionClient'
$target = Join-Path $env:ProgramFiles 'InputFusion\Client'
$configFile = Join-Path $env:ProgramData 'InputFusion\client.json'

$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service) {
    Write-Host "Stoppe und entferne Dienst $serviceName ..."
    Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
    $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(15))
    & sc.exe delete $serviceName | Out-Null
}
Get-Process -Name 'InputFusion.Client' -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $target) {
    Write-Host "Entferne $target ..."
    Remove-Item -Path $target -Recurse -Force
    $parent = Split-Path $target
    if (-not (Get-ChildItem -Path $parent -Force)) { Remove-Item -Path $parent -Force }
}
if ($RemoveConfig -and (Test-Path $configFile)) {
    Write-Host "Entferne $configFile ..."
    Remove-Item -Path $configFile -Force
}
Write-Host 'Fertig.'
