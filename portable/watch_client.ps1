# NineRE 本地整合版：服务监视器（可选）
# 直接双击 Game\The Nine Regions\MC.exe 时，自动确保 PostgreSQL(5432) 与服务端(8800)。
# 由同目录 VBS 以隐藏窗口启动；日志：同目录 watch_client.log
$ErrorActionPreference = 'SilentlyContinue'
$base = Split-Path -Parent $MyInvocation.MyCommand.Path
$pgCtl = Join-Path $base 'pgsql\bin\pg_ctl.exe'
$pgData = Join-Path $base 'pgdata'
$pgLog = Join-Path $base 'pgdata.log'
$pack = Join-Path $base 'Server'
$serverExe = Join-Path $pack 'nine-regions-backend.exe'
$watchLog = Join-Path $base 'watch_client.log'

function Port([int]$p) {
    try {
        $c = New-Object Net.Sockets.TcpClient
        $ar = $c.BeginConnect('127.0.0.1', $p, $null, $null)
        if (-not $ar.AsyncWaitHandle.WaitOne(300)) { $c.Close(); return $false }
        $c.EndConnect($ar); $c.Close(); return $true
    } catch { return $false }
}
function Log($m) {
    try { Add-Content -Path $watchLog -Value ((Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' ' + $m) -Encoding UTF8 } catch {}
}

Log '===== 监视器启动 ====='
while ($true) {
    $mc = Get-Process -Name MC -ErrorAction SilentlyContinue
    if ($mc) {
        if (-not (Port 5432)) {
            Log '检测到客户端启动，拉起 PostgreSQL …'
            Start-Process -FilePath $pgCtl -ArgumentList @('-D', ('"' + $pgData + '"'), '-l', ('"' + $pgLog + '"'), '-o', '"-p 5432"', 'start') -WindowStyle Hidden
        }
        if (-not (Port 8800)) {
            Log '检测到客户端启动，拉起服务端 …'
            Start-Process -FilePath 'cmd.exe' -ArgumentList ('/c ""' + $serverExe + '" >> run.log 2>&1"') -WorkingDirectory $pack -WindowStyle Hidden
        }
    }
    Start-Sleep -Seconds 2
}
