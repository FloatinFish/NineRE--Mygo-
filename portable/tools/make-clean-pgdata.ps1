param(
    [string]$Root = "",
    [string]$Out = "",
    [int]$Port = 5432,
    [switch]$Force
)

$ErrorActionPreference = "Stop"
if (-not $Root) {
    $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}
if (-not $Out) { $Out = Join-Path $Root "pgdata-template" }

$PgBin = Join-Path $Root "pgsql\bin"
$InitDb = Join-Path $PgBin "initdb.exe"
$PgCtl = Join-Path $PgBin "pg_ctl.exe"
$Psql = Join-Path $PgBin "psql.exe"

if (-not (Test-Path $InitDb)) { throw "initdb.exe not found: $PgBin" }
if ((Test-Path $Out) -and -not $Force) {
    throw "target exists: $Out (use -Force to replace it)"
}

$tmp = Join-Path $env:TEMP ("NineRE-pgdata-" + [guid]::NewGuid().ToString("N"))
Write-Host "[..] initdb -> $tmp"
& $InitDb -D $tmp -U postgres --auth=trust --encoding=UTF8 --locale=C | Out-Null
if ($LASTEXITCODE -ne 0) { throw "initdb failed" }

try {
    Write-Host "[..] starting PostgreSQL on port $Port"
    & $PgCtl -D $tmp -l (Join-Path $tmp "init.log") -o "-p $Port" start -w | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "pg_ctl start failed" }

    Write-Host "[..] creating schema mc"
    & $Psql -h 127.0.0.1 -p $Port -U postgres -d postgres -v ON_ERROR_STOP=1 -c "CREATE SCHEMA IF NOT EXISTS mc;" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "CREATE SCHEMA mc failed" }

    Write-Host "[..] stopping PostgreSQL"
    & $PgCtl -D $tmp -m fast stop -w | Out-Null
} finally {
    if (Test-Path $tmp) {
        Remove-Item (Join-Path $tmp "init.log") -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $tmp "postmaster.pid") -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path $Out) { Remove-Item $Out -Recurse -Force }
Copy-Item $tmp $Out -Recurse -Force
Remove-Item $tmp -Recurse -Force

Write-Host ("[OK] clean template: " + $Out) -ForegroundColor Green
Write-Host "     schema mc created; backend creates tables on first run"
