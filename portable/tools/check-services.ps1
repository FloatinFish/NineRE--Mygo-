param([string]$Root = "")

$ErrorActionPreference = "Continue"
if (-not $Root) {
    $Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
}

$VerUrl = "http://127.0.0.1:4010/r/1.0.50/ver"

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

$pg   = Test-Port 5432
$ws   = Test-Port 8800
$http = Test-Http $VerUrl

Write-Host ("PostgreSQL 5432 : " + $(if ($pg) { "READY" } else { "DOWN" }))
Write-Host ("Server WS 8800  : " + $(if ($ws) { "READY" } else { "DOWN" }))
Write-Host ("HTTP /ver      : " + $(if ($http) { "READY" } else { "DOWN" }))
Write-Host ("Root           : " + $Root)

if ($pg -and $ws -and $http) { exit 0 }
exit 1
