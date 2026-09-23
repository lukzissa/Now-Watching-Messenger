# Compila o NowWatchingMessenger.exe usando o csc do .NET Framework (vem com o Windows).
$ErrorActionPreference = "Stop"
$src = $PSScriptRoot
$out = Join-Path (Split-Path $src) "NowWatchingMessenger.exe"
$fw  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wm  = "$env:WINDIR\System32\WinMetadata"

Get-Process NowWatching, NowWatchingMessenger -ErrorAction SilentlyContinue | Stop-Process -Force

& "$fw\csc.exe" /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ "/out:$out" `
    "/win32icon:$src\icon.ico" `
    "/resource:$src\icon.ico,NowWatching.icon.ico" `
    "/resource:$src\about.png,NowWatching.about.png" `
    "/resource:$src\media-placeholders.txt,NowWatching.media-placeholders.txt" `
    "/r:$fw\System.Runtime.dll" "/r:$fw\System.Runtime.InteropServices.WindowsRuntime.dll" `
    "/r:$wm\Windows.Foundation.winmd" "/r:$wm\Windows.Media.winmd" `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/r:$fw\WPF\UIAutomationClient.dll" "/r:$fw\WPF\UIAutomationTypes.dll" `
    "$src\NowWatching.cs" "$src\Strings.cs" "$src\Forms.cs" "$src\Updater.cs" "$src\MaskedMedia.cs" "$src\TabReader.cs"
if ($LASTEXITCODE -ne 0) { throw "Falha na compilacao" }
"OK: $out"
