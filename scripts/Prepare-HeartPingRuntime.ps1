param(
    [string]$OutputFolder = ".artifacts\startup-runtime"
)

$ErrorActionPreference = "Stop"

$scriptsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptsRoot
$projectPath = Join-Path $root "src\HeartPing\HeartPing.csproj"
$outputPath = Join-Path $root $OutputFolder
$publishPath = Join-Path $root ".artifacts\release"
$sessionSources = @(
    (Join-Path $root "data\WTelegram.session"),
    (Join-Path $root "src\HeartPing\data\WTelegram.session"),
    (Join-Path $root "src\HeartPing\bin\Debug\net8.0-windows\data\WTelegram.session"),
    (Join-Path $root "src\HeartPing\bin\Debug\net8.0\data\WTelegram.session")
)
$sessionSource = $sessionSources | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$sessionTargetDir = Join-Path $outputPath "data"
$sessionTarget = Join-Path $sessionTargetDir "WTelegram.session"

& dotnet publish $projectPath -c Release -o $publishPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
Copy-Item -Path (Join-Path $publishPath "*") -Destination $outputPath -Recurse -Force

if ($sessionSource) {
    New-Item -ItemType Directory -Force -Path $sessionTargetDir | Out-Null
    Copy-Item -LiteralPath $sessionSource -Destination $sessionTarget -Force
    Write-Host "Session copied to $sessionTarget"
}
else {
    Write-Warning "Session file was not found. Run login-only for the published exe before first real send."
}

Write-Host "Runtime folder is ready at $outputPath"
Write-Host "You can test it with:"
Write-Host "  $outputPath\HeartPing.exe --check"
Write-Host "  $outputPath\HeartPing.exe --tray --dry-run"
