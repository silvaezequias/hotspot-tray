# Instala o HotspotTray em %USERPROFILE%\HotspotTray e inicia (ele se registra para iniciar com o Windows).
# Fora de AppData de proposito: apps empacotados (MSIX) tem AppData virtualizado e o Windows nao enxergaria o exe.
# Use "install.ps1 -Uninstall" para remover.
param([switch]$Uninstall)
$ErrorActionPreference = 'Stop'
$dir = Join-Path $env:USERPROFILE 'HotspotTray'
$exe = Join-Path $dir 'HotspotTray.exe'

Get-Process HotspotTray -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

if ($Uninstall) {
    Start-Process schtasks.exe -ArgumentList '/Delete /TN HotspotTray /F' -Verb RunAs -Wait -WindowStyle Hidden -WorkingDirectory $env:WINDIR
    Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name HotspotTray -ErrorAction SilentlyContinue
    Remove-Item 'HKCU:\Software\HotspotTray' -Recurse -ErrorAction SilentlyContinue
    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'HotspotTray removido.'
    return
}

$src = Join-Path $PSScriptRoot 'HotspotTray.exe'
if (-not (Test-Path $src)) { & (Join-Path $PSScriptRoot 'build.ps1') }
New-Item -ItemType Directory -Force $dir | Out-Null
Copy-Item $src $exe -Force
Copy-Item (Join-Path $PSScriptRoot 'Program.cs') $dir -Force
Copy-Item (Join-Path $PSScriptRoot 'Ativar inicializacao rapida.cmd') $dir -Force

# Registrar a tarefa de logon exige administrador (o Windows mostra o aviso do UAC uma vez).
# O app em si continua rodando como usuario comum.
Start-Process $exe -ArgumentList '/register-task' -Wait
schtasks /Query /TN HotspotTray *> $null
if ($LASTEXITCODE -ne 0) { Write-Warning "Nao foi possivel registrar a tarefa de logon (veja $dir\log.txt)." }

Start-Process $exe
Write-Host "Instalado em $exe e iniciado."
