param(
    [string]$DeployDir = "",
    [switch]$NoDeploy
)

$ErrorActionPreference = "Stop"
$env:GOFLAGS = "-buildvcs=false"
Set-Location $PSScriptRoot

bun run build -- -skip-installer
if ($LASTEXITCODE -ne 0) { throw "构建失败" }

$exe = Join-Path $PSScriptRoot "build\windows-amd64\NineRE Starter.exe"
if (-not (Test-Path $exe)) { throw "构建产物不存在：$exe" }
Write-Host ("构建完成：" + $exe) -ForegroundColor Green

if (-not $NoDeploy -and $DeployDir) {
    if (-not (Test-Path $DeployDir)) { throw "部署目录不存在：$DeployDir" }
    $dest = Join-Path $DeployDir "NineRE Starter.exe"
    Copy-Item $exe $dest -Force
    Write-Host ("已部署：" + $dest) -ForegroundColor Green
}
