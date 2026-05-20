param(
    [string]$RuntimeFolder = ".artifacts\startup-runtime"
)

$ErrorActionPreference = "Stop"

$scriptsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptsRoot
$runtimePath = Join-Path $root $RuntimeFolder
$exePath = Join-Path $runtimePath "HeartPing.exe"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "HeartPing.exe was not found at $exePath. Build the startup runtime first."
}

$startupFolder = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startupFolder "HeartPing.lnk"

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $exePath
$shortcut.Arguments = "--tray"
$shortcut.WorkingDirectory = $runtimePath
$shortcut.WindowStyle = 7
$shortcut.IconLocation = $exePath
$shortcut.Save()

Write-Host "Startup shortcut created at $shortcutPath"
Write-Host "Target: $exePath --tray"
