#!/usr/bin/env sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if [ -f "$SCRIPT_DIR/LightingShowcase.CommandLine.dll" ]; then
  exec dotnet "$SCRIPT_DIR/LightingShowcase.CommandLine.dll" "$@"
else
  exec dotnet run --project "$SCRIPT_DIR/LightingShowcase.CommandLine.Linux.csproj" -- "$@"
fi
