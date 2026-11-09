# -*- coding: utf-8 -*-
<#
  NineRE 便携版 · 运行环境检查 / 部署工具
  ------------------------------------------------------------
  为什么需要它：启动器本身是 .NET Framework 4.8 的 WPF 程序，
  如果目标电脑缺 .NET / VC++ 运行库，连启动器都打不开——所以把检查
  单独做成脚本，用系统自带的 PowerShell 跑，不依赖启动器。

  用法：
    · 双击根目录的「环境检查.bat」
    · 或在 PowerShell 里：powershell -ExecutionPolicy Bypass -File tools\env_setup.ps1
    · 只出报告不提问（自动化用）：... env_setup.ps1 -Auto
    · 自动化安装 WebView2（无交互）：... env_setup.ps1 -Auto -FixWebView2

  检查项：
    1) Windows 版本           2) .NET Framework 4.8
    3) WebView2 Runtime       4) VC++ 2015-2022 x64
    5) DirectX 11             6) 便携目录可写
    7) 端口 5432/8800/4010    8) PostgreSQL 数据目录
    9) 关键文件完整性

  WebView2：MyGo 启动器依赖它。缺的时候优先用 tools\ 里自带的安装包，
  没有则联网下载微软 Evergreen 安装器（约 2 MB）后静默安装。
#>
param(
    [switch]$Auto,
    [switch]$FixWebView2
)

$ErrorActionPreference = 'Continue'
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$LogPath = Join-Path $Root 'env_check.log'
$script:Issues = @()
$script:Fixed = @()

function Write-Log([string]$msg) {
    $line = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss') + ' ' + $msg
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
}

function Say([string]$msg, [string]$color = 'Gray') {
    Write-Host $msg -ForegroundColor $color
    Write-Log $msg
}

function Ok([string]$name, [string]$detail = '') {
    Say ("  [正常] " + $name + $(if ($detail) { "  ($detail)" } else { '' })) 'Green'
}

function Fail([string]$name, [string]$detail = '', [string]$fix = '') {
    Say ("  [缺失] " + $name + $(if ($detail) { "  ($detail)" } else { '' })) 'Red'
    if ($fix) { Say ("         处理：" + $fix) 'Yellow' }
    $script:Issues += $name
}

function Ask([string]$question) {
    if ($Auto) { return $false }
    $ans = Read-Host ($question + ' [y/N]')
    return ($ans -eq 'y' -or $ans -eq 'Y')
}

Say '=============================================='
Say ' NineRE 便携版 · 运行环境检查'
Say '=============================================='
Say ("目录：" + $Root)
Say ''

# ── 1. Windows 版本 ────────────────────────────────────────────
$os = Get-CimInstance Win32_OperatingSystem
$ver = [Version]$os.Version
if ($ver.Major -ge 10) { Ok 'Windows 版本' ($os.Caption + ' ' + $ver) }
elseif ($ver.Major -eq 6 -and $ver.Minor -ge 1) { Ok 'Windows 版本' ($os.Caption + ' ' + $ver + '（较旧，建议 Win10 以上）') }
else { Fail 'Windows 版本' $os.Caption '需要 Windows 7 及以上' }

# ── 2. .NET Framework 4.8 ────────────────────────────────────
$ndp = 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
$rel = 0
try { $rel = [int](Get-ItemProperty -Path $ndp -Name Release -ErrorAction Stop).Release } catch { $rel = 0 }
if ($rel -ge 528040) {
    Ok '.NET Framework 4.8' ('Release=' + $rel)
} elseif ($rel -ge 528040 - 0) {
    Ok '.NET Framework' ('Release=' + $rel)
} else {
    Fail '.NET Framework 4.8' ('当前 Release=' + $rel + '，需要 528040 以上') `
        '安装 .NET Framework 4.8（微软官网 ndp48-x86-x64-allos-enu.exe），装完重启再双击启动器'
}

# ── 3. WebView2 Runtime（MyGo 启动器依赖） ─────────────────────
function Get-WebView2Version {
    $guid = '{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}'
    $keys = @(
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\$guid",
        "HKLM:\SOFTWARE\Microsoft\EdgeUpdate\Clients\$guid",
        "HKCU:\Software\Microsoft\EdgeUpdate\Clients\$guid"
    )
    foreach ($k in $keys) {
        try {
            $v = (Get-ItemProperty -Path $k -Name pv -ErrorAction Stop).pv
            if ($v) { return [string]$v }
        } catch { }
    }
    # 固定版/绿色版可能没有注册表项：看安装目录
    try {
        $base = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft\EdgeWebView\Application'
        $dir = Get-ChildItem -LiteralPath $base -Directory -ErrorAction Stop |
               Sort-Object Name -Descending | Select-Object -First 1
        if ($dir -and (Test-Path -LiteralPath (Join-Path $dir.FullName 'msedgewebview2.exe'))) { return $dir.Name }
    } catch { }
    return $null
}

function Test-WebView2Version([string]$v) {
    if (-not $v) { return $false }
    try { return ([Version]$v -ge [Version]'1.0.1185') } catch { return $true }
}

$wv = Get-WebView2Version
if ($wv -and (Test-WebView2Version $wv)) {
    Ok 'WebView2 Runtime' ('版本 ' + $wv)
} else {
    $detail = if ($wv) { '版本过旧：' + $wv + '，需要 1.0.1185 以上' } else { '未安装' }
    # 优先用便携包自带的安装器（离线可用）
    $installer = @(
        (Join-Path $Root 'tools\MicrosoftEdgeWebView2Setup.exe'),
        (Join-Path $Root 'tools\WebView2Setup.exe'),
        (Join-Path $Root 'tools\MicrosoftEdgeWebView2RuntimeInstallerX64.exe')
    ) | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

    $doInstall = $false
    if ($installer) {
        Say '         发现便携包自带的 WebView2 安装器' 'Yellow'
        $doInstall = $true
    } elseif ($Auto) {
        $doInstall = [bool]$FixWebView2
    } else {
        $doInstall = Ask '         是否现在下载并安装 WebView2 Runtime？（需要联网，约 2 MB）'
    }

    if ($doInstall) {
        $downloaded = $false
        try {
            if (-not $installer) {
                [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
                $installer = Join-Path $env:TEMP ('MicrosoftEdgeWebView2Setup_' + [Guid]::NewGuid().ToString('N') + '.exe')
                Say '         正在下载 WebView2 安装器…' 'Yellow'
                Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $installer -UseBasicParsing -ErrorAction Stop
                $downloaded = $true
            }
            Say '         正在静默安装 WebView2 Runtime…' 'Yellow'
            $proc = Start-Process -FilePath $installer -ArgumentList '/silent','/install' -Wait -PassThru
            Say ('         安装器退出码：' + $proc.ExitCode) 'Gray'
            if ($downloaded) { Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue }
            # 注册表可能略有延迟：重试 3 次
            for ($i = 0; $i -lt 3; $i++) {
                $wvNow = Get-WebView2Version
                if ($wvNow -and (Test-WebView2Version $wvNow)) { break }
                Start-Sleep -Seconds 2
            }
        } catch {
            if ($downloaded) { Remove-Item -LiteralPath $installer -Force -ErrorAction SilentlyContinue }
            Say ('         安装失败：' + $_.Exception.Message) 'Red'
        }
    }

    $wv = Get-WebView2Version
    if ($wv -and (Test-WebView2Version $wv)) {
        Ok 'WebView2 Runtime' ('已安装，版本 ' + $wv)
        $script:Fixed += 'WebView2 Runtime'
    } else {
        Fail 'WebView2 Runtime' $detail `
            '运行本工具时选择自动安装；或手动安装 Microsoft Edge WebView2 Runtime（Evergreen），离线机器可把安装器放到 tools\ 下'
    }
}

# ── 4. VC++ 2015-2022 x64 运行库 ────────────────────────────────
$vcOk = $false
$vcDetail = ''
try {
    $vc = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction Stop
    if ($vc.Installed -eq 1) { $vcOk = $true; $vcDetail = 'v' + $vc.Major + '.' + $vc.Minor + '.' + $vc.Bld }
} catch { }
if (-not $vcOk) {
    $rt = Join-Path $env:WINDIR 'System32\vcruntime140.dll'
    $rt1 = Join-Path $env:WINDIR 'System32\vcruntime140_1.dll'
    if ((Test-Path $rt) -and (Test-Path $rt1)) { $vcOk = $true; $vcDetail = 'System32 已存在 vcruntime140(_1).dll' }
}
if ($vcOk) { Ok 'VC++ 2015-2022 x64 运行库' $vcDetail }
else {
    Fail 'VC++ 2015-2022 x64 运行库' '数据库与游戏客户端都依赖它' `
        '安装 VC_redist.x64.exe（微软官网 Visual C++ 2015-2022 Redistributable）'
}

# ── 5. DirectX 11 ────────────────────────────────────────────
if (Test-Path (Join-Path $env:WINDIR 'System32\d3d11.dll')) { Ok 'DirectX 11' }
else { Fail 'DirectX 11' '缺少 d3d11.dll' '安装 DirectX 运行库（win10 及以上通常自带）' }

# ── 6. 便携目录可写（放 Program Files 下会被拦） ─────────────────
try {
    $probe = Join-Path $Root ('.write_test_' + [Guid]::NewGuid().ToString('N') + '.tmp')
    Set-Content -LiteralPath $probe -Value 'ok' -Encoding ASCII -ErrorAction Stop
    Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
    Ok '目录可写'
} catch {
    Fail '目录可写' $Root '把整个便携包放到非系统目录（如 D:\NineRegions-Portable），不要放在 Program Files'
}

# ── 7. 端口占用 ─────────────────────────────────────────────
function Test-PortBusy([int]$port) {
    try { return (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop | Select-Object -First 1) }
    catch { return $null }
}
foreach ($p in @(@(5432, '数据库'), @(8800, '游戏服'), @(4010, 'HTTP 接口'))) {
    $busy = Test-PortBusy $p[0]
    if ($busy) {
        $procName = ''
        try { $procName = (Get-Process -Id $busy.OwningProcess -ErrorAction Stop).ProcessName } catch { }
        # 若是本便携包自己的进程，不算冲突
        $own = $procName -match 'postgres|nine-regions-backend'
        if ($own) { Ok ('端口 ' + $p[0] + '（' + $p[1] + '）') ('已被本包进程占用：' + $procName) }
        else {
            Fail ('端口 ' + $p[0] + '（' + $p[1] + '）') ('被 ' + $procName + ' 占用')
            Say '         处理：关闭占用该端口的程序，或重启电脑后再试' 'Yellow'
        }
    } else {
        Ok ('端口 ' + $p[0] + '（' + $p[1] + '）') '空闲'
    }
}

# ── 8. PostgreSQL 数据目录 ───────────────────────────────────
$PgData = Join-Path $Root 'pgdata'
$PgBin = Join-Path $Root 'pgsql\bin'
$pgVer = Join-Path $PgData 'PG_VERSION'
if ((Test-Path $pgVer) -and (Test-Path (Join-Path $PgBin 'pg_ctl.exe'))) {
    $v = (Get-Content -LiteralPath $pgVer -ErrorAction SilentlyContinue) -join ''
    Ok 'PostgreSQL 数据目录' ('版本 ' + $v)
    # 数据目录里有 postmaster.pid 但进程已死 = 上次异常退出，需要清理
    $pidFile = Join-Path $PgData 'postmaster.pid'
    if (Test-Path $pidFile) {
        $stalePid = 0
        try { $stalePid = [int]((Get-Content -LiteralPath $pidFile -TotalCount 1)) } catch { }
        if ($stalePid -gt 0 -and -not (Get-Process -Id $stalePid -ErrorAction SilentlyContinue)) {
            Say '        发现上次异常退出残留的 postmaster.pid（进程已不存在）' 'Yellow'
            if (Ask '         是否清理该残留文件？') {
                Remove-Item -LiteralPath $pidFile -Force -ErrorAction SilentlyContinue
                Say '         已清理' 'Green'
                $script:Fixed += 'postmaster.pid'
            }
        }
    }
} else {
    Fail 'PostgreSQL 数据目录' 'pgdata 缺失或损坏' '重新解压完整便携包（pgdata 不可单独复制，建议整包拷贝）'
}

# ── 9. 关键文件完整性 ───────────────────────────────────────
$need = @(
    'NineRE Launcher.exe',
    'Server\nine-regions-backend.exe',
    'pgsql\bin\pg_ctl.exe',
    'Game\The Nine Regions\MC.exe'
)
foreach ($f in $need) {
    $full = Join-Path $Root $f
    if (Test-Path $full) { Ok ('文件：' + $f) }
    else { Fail ('文件：' + $f) '缺失' '重新解压完整便携包' }
}

# ── 汇总 ────────────────────────────────────────────────────
Say ''
Say '=============================================='
if ($script:Issues.Count -eq 0) {
    Say (' 全部检查通过' + $(if ($script:Fixes -or $script:Fixed.Count) { '（已处理：' + ($script:Fixed -join '、') + '）' } else { '' })) 'Green'
    Say ' 现在可以双击「NineRE Launcher.exe」进入游戏。' 'Green'
} else {
    Say (' 有 ' + $script:Issues.Count + ' 项需要处理：' + ($script:Issues -join '、')) 'Red'
    Say ' 按上面的提示逐项处理后，再运行一次本工具确认。' 'Yellow'
}
Say (' 日志：' + $LogPath)
Say '=============================================='

if (-not $Auto) {
    Write-Host ''
    Read-Host '按回车关闭' | Out-Null
}
