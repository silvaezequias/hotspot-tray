@echo off
rem Registra a tarefa que abre o HotspotTray assim que voce entra no Windows e inicia o app.
rem O proprio HotspotTray pede a permissao de administrador (UAC) uma vez.
taskkill /IM HotspotTray.exe /F >nul 2>&1
start "" /wait "%~dp0HotspotTray.exe" /register-task
schtasks /Query /TN HotspotTray >nul 2>&1
if %errorlevel%==0 (
  echo Pronto! O HotspotTray vai abrir junto com o Windows, com prioridade alta.
) else (
  echo Nao foi possivel registrar. Veja o log em "%~dp0log.txt"
)
start "" "%~dp0HotspotTray.exe"
pause
