param(
    [switch]$Check,
    [switch]$DryRun,
    [switch]$LoginOnly,
    [switch]$Watch,
    [switch]$Tray,
    [switch]$SendNow,
    [switch]$PublishRelease,
    [switch]$UsePublishedExe,
    [string]$Config = "appsettings.json"
)

$ErrorActionPreference = "Stop"

$scriptsRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptsRoot
$projectPath = Join-Path $root "src\HeartPing\HeartPing.csproj"
$dotnetHome = Join-Path $root ".dotnet-home"
$nugetHome = Join-Path $dotnetHome "AppData\Roaming\NuGet"

New-Item -ItemType Directory -Force -Path $nugetHome | Out-Null

$env:DOTNET_CLI_HOME = $dotnetHome
$env:HOME = $dotnetHome
$env:USERPROFILE = $dotnetHome
$env:APPDATA = Join-Path $dotnetHome "AppData\Roaming"

$runArgs = @()

if ($Check) {
    $runArgs += "--check"
}

if ($DryRun) {
    $runArgs += "--dry-run"
}

if ($LoginOnly) {
    $runArgs += "--login-only"
}

if ($Watch) {
    $runArgs += "--watch"
}

if ($Tray) {
    $runArgs += "--tray"
}

if ($SendNow) {
    $runArgs += "--send-now"
}

if ($Config) {
    $runArgs += "--config"
    $runArgs += $Config
}

$argsList = @("run", "--project", $projectPath, "--")
$argsList += $runArgs

if ($PublishRelease) {
    & dotnet publish $projectPath -c Release -o (Join-Path $root ".artifacts\release")
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    Write-Host "Release build is ready at .artifacts/release/HeartPing.exe"
}

if ($UsePublishedExe) {
    $exePath = Join-Path $root ".artifacts\release\HeartPing.exe"
    if (-not (Test-Path -LiteralPath $exePath)) {
        throw "Published executable was not found at $exePath. Run with -PublishRelease first."
    }

    & $exePath @runArgs
    exit $LASTEXITCODE
}

& dotnet @argsList
exit $LASTEXITCODE
