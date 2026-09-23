@echo off
set "ROOT=%~dp0"
set "EXE=%ROOT%bin\Release\SolidWorksTeamRenameTool.exe"

echo Checking and building SolidWorks Team Rename Tool...
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%ROOT%Build-NoInstall.ps1"
if errorlevel 1 (
  echo.
  echo Build failed. Please check the messages above.
  pause
  exit /b 1
)

if not exist "%EXE%" (
  echo.
  echo Cannot find generated program: %EXE%
  pause
  exit /b 1
)

start "" /D "%ROOT%" "%EXE%"
