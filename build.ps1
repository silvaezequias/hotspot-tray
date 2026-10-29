# Compila HotspotTray.exe usando o compilador C# que ja vem com o Windows (.NET Framework 4.x).
$ErrorActionPreference = 'Stop'
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wmd = "$env:WINDIR\System32\WinMetadata"
$out = Join-Path $PSScriptRoot 'HotspotTray.exe'

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu "/out:$out" `
    "/r:$fw\System.Runtime.dll" `
    "/r:$wmd\Windows.Foundation.winmd" `
    "/r:$wmd\Windows.Networking.winmd" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll `
    (Join-Path $PSScriptRoot 'Program.cs')
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao" }
Write-Host "OK: $out"
