@echo off
setlocal
if "%~1"=="" (
  echo Usage: install-native-host.bat ^<extension-id^>
  exit /b 1
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-native-host.ps1" -ExtensionId "%~1"
exit /b %ERRORLEVEL%
