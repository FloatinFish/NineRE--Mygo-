param([string]$Root = "")

$ErrorActionPreference = "Continue"
if (-not $Root) {
    $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}

$PgBin  = Join-Path $Root "pgsql\bin"
$PgData = Join-Path $Root "pgdata"

Get-Process -Name "nine-regions-backend" -ErrorAction SilentlyContinue |
    ForEach-Object { try { $_.Kill(); $_.WaitForExit(5000) } catch {} }

$deadline = (Get-Date).AddSeconds(8)
while ((Get-Date) -lt $deadline) {
    $busy = Get-NetTCPConnection -LocalPort 8800, 4010 -State Listen -ErrorAction SilentlyContinue
    if (-not $busy) { break }
    Start-Sleep -Milliseconds 300
}
Write-Host "[OK] game server stopped" -ForegroundColor Green

$pgCtl = Join-Path $PgBin "pg_ctl.exe"
if ((Test-Path $pgCtl) -and (Test-Path $PgData)) {
    Start-Process -FilePath $pgCtl -ArgumentList @("-D", "`"$PgData`"", "-m", "fast", "stop") `
        -WorkingDirectory $PgBin -WindowStyle Hidden -Wait | Out-Null
    Write-Host "[OK] PostgreSQL stopped" -ForegroundColor Green
} else {
    Write-Host "[WARN] pg_ctl.exe or pgdata missing, skipped" -ForegroundColor Yellow
}
