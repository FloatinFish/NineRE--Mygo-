param([string]$Root = "")

$ErrorActionPreference = "Continue"
if (-not $Root) {
    $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}

$PgBin     = Join-Path $Root "pgsql\bin"
$PgData    = Join-Path $Root "pgdata"
$PgLog     = Join-Path $Root "pgdata.log"
$ServerDir = Join-Path $Root "Server"
$ServerExe = Join-Path $ServerDir "nine-regions-backend.exe"
$RunLog    = Join-Path $ServerDir "run.log"
$VerUrl    = "http://127.0.0.1:4010/r/1.0.50/ver"

function Test-Port([int]$Port) {
    try {
        $c = New-Object Net.Sockets.TcpClient
        $ar = $c.BeginConnect("127.0.0.1", $Port, $null, $null)
        if (-not $ar.AsyncWaitHandle.WaitOne(400)) { $c.Close(); return $false }
        $c.EndConnect($ar); $c.Close(); return $true
    } catch { return $false }
}

function Test-Http([string]$Url) {
    try {
        $r = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 2
        return ($r.StatusCode -ge 200 -and $r.StatusCode -lt 500)
    } catch { return $false }
}

function Wait-Postgres([int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $isReady = Join-Path $PgBin "pg_isready.exe"
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $isReady) {
            & $isReady -h 127.0.0.1 -p 5432 | Out-Null
            if ($LASTEXITCODE -eq 0) { return $true }
        } elseif (Test-Port 5432) {
            return $true
        }
        Start-Sleep -Milliseconds 400
    }
    return $false
}

if (-not (Test-Path (Join-Path $PgBin "pg_ctl.exe"))) {
    Write-Host "[ERROR] pg_ctl.exe not found: $PgBin" -ForegroundColor Red
    exit 1
}
if (-not (Test-Path $ServerExe)) {
    Write-Host "[ERROR] server exe not found: $ServerExe" -ForegroundColor Red
    exit 1
}

if (-not (Wait-Postgres 0)) {
    Write-Host "[..] starting PostgreSQL" -ForegroundColor Yellow
    $pgArgs = @("-D", "`"$PgData`"", "-l", "`"$PgLog`"", "-o", "`"-p 5432`"", "start")
    Start-Process -FilePath (Join-Path $PgBin "pg_ctl.exe") -ArgumentList $pgArgs `
        -WorkingDirectory $PgBin -WindowStyle Hidden -Wait | Out-Null
    if (-not (Wait-Postgres 60)) {
        Write-Host "[ERROR] PostgreSQL not ready in 60s (see pgdata.log)" -ForegroundColor Red
        exit 1
    }
}
Write-Host "[OK] PostgreSQL 5432" -ForegroundColor Green

if (-not ((Test-Http $VerUrl) -and (Test-Port 8800))) {
    Write-Host "[..] starting game server" -ForegroundColor Yellow
    $serverArgs = @("/c", "`"`"$ServerExe`" >> run.log 2>&1`"")
    Start-Process -FilePath "cmd.exe" -ArgumentList $serverArgs `
        -WorkingDirectory $ServerDir -WindowStyle Hidden | Out-Null
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Date) -lt $deadline) {
        if ((Test-Http $VerUrl) -and (Test-Port 8800)) { break }
        Start-Sleep -Milliseconds 500
    }
}
if ((Test-Http $VerUrl) -and (Test-Port 8800)) {
    Write-Host "[OK] game server 8800 / 4010" -ForegroundColor Green
    exit 0
}

Write-Host "[ERROR] game server not ready in 60s (see Server\run.log)" -ForegroundColor Red
exit 1
