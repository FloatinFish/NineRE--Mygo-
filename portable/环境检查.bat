@echo off
chcp 65001 >nul
set "ROOT=%~dp0"
if not exist "%ROOT%tools\env_setup.ps1" (
  echo [ERROR] tools\env_setup.ps1 not found. Please re-extract the portable package.
  pause
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%tools\env_setup.ps1"
