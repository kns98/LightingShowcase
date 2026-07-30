#!/usr/bin/env bash
set -euo pipefail

dotnet restore ./LightingShowcase.Linux.sln
dotnet build ./LightingShowcase.Linux.sln -c Debug --no-restore
