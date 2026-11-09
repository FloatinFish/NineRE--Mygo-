param(
    [string]$Root = "",
    [string]$Out = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"
if (-not $Root) {
    $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
if (-not $Out) { $Out = Join-Path (Split-Path -Parent $Root) "NineRE-Portable.zip" }

$required = @(
    "NineRE Launcher.exe",
    "Server\nine-regions-backend.exe",
    "pgsql\bin\pg_ctl.exe",
    "pgdata-template\PG_VERSION",
    "assets\login_bg.mp4"
)
foreach ($f in $required) {
    if (-not (Test-Path (Join-Path $Root $f))) { throw "missing: $f" }
}
if ((Test-Path $Out) -and -not $Force) {
    throw "target exists: $Out (use -Force to replace it)"
}

$stage = Join-Path $env:TEMP ("NineRE-pack-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $stage | Out-Null
Write-Host "[..] staging -> $stage"

Copy-Item (Join-Path $Root "NineRE Launcher.exe") $stage -Force
Copy-Item (Join-Path $Root "README.txt") $stage -Force
Copy-Item (Join-Path $Root "assets") (Join-Path $stage "assets") -Recurse -Force
Remove-Item (Join-Path $stage "assets\announce.txt") -Force -ErrorAction SilentlyContinue
Copy-Item (Join-Path $Root "pgsql") (Join-Path $stage "pgsql") -Recurse -Force
Copy-Item (Join-Path $Root "pgdata-template") (Join-Path $stage "pgdata-template") -Recurse -Force

$serverDst = Join-Path $stage "Server"
Copy-Item (Join-Path $Root "Server") $serverDst -Recurse -Force
# A clean bundle carries no player data, logs or database credentials from this machine.
Remove-Item (Join-Path $serverDst "data") -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $serverDst "logs") -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $serverDst -Filter "*.log" -File -ErrorAction SilentlyContinue | Remove-Item -Force
New-Item -ItemType Directory -Force (Join-Path $serverDst "data") | Out-Null
# Never ship this machine's DB credentials or account hints.
Remove-Item (Join-Path $serverDst ".env") -Force -ErrorAction SilentlyContinue
$cleanDb = @{ host = "localhost"; username = "postgres"; password = "postgres"; database = "postgres" } | ConvertTo-Json
New-Item -ItemType Directory -Force (Join-Path $serverDst "setting") | Out-Null
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText((Join-Path $serverDst "setting\db.json"), $cleanDb, $utf8NoBom)

$toolsDst = Join-Path $stage "tools"
Copy-Item (Join-Path $Root "tools") $toolsDst -Recurse -Force
Remove-Item (Join-Path $toolsDst "legacy") -Recurse -Force -ErrorAction SilentlyContinue
Get-ChildItem $toolsDst -Filter "*.log" -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "[..] compressing -> $Out"
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $Out -CompressionLevel Optimal -Force
Remove-Item $stage -Recurse -Force

$size = [math]::Round((Get-Item $Out).Length / 1MB, 1)
Write-Host ("[OK] " + $Out + " (" + $size + " MB)") -ForegroundColor Green
Write-Host "     extract into the game folder, then run NineRE Launcher.exe"
