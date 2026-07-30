@echo off
setlocal
if exist "%~dp0LightingShowcase.CommandLine.dll" (
  dotnet "%~dp0LightingShowcase.CommandLine.dll" %*
) else (
  dotnet run --project "%~dp0LightingShowcase.CommandLine.Windows.csproj" -- %*
)
exit /b %ERRORLEVEL%
