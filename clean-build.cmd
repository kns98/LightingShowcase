@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0clean-build.ps1"
exit /b %ERRORLEVEL%
