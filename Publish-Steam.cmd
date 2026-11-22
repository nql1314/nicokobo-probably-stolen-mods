@echo off
where pwsh.exe >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 is required. Install it, then run this file again.
  pause
  exit /b 1
)
pwsh.exe -NoProfile -File "%~dp0Publish-Steam.ps1" %*
set "publish_exit=%errorlevel%"
pause
exit /b %publish_exit%
