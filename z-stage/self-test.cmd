@echo off
setlocal
cd /d "%~dp0"
start "" /wait "%~dp0ZStage.exe" --self-test "%~dp0self-test-report.txt"
if errorlevel 1 (
  echo Tests FAILED. See self-test-report.txt
  exit /b 1
)
type "%~dp0self-test-report.txt"
exit /b 0
