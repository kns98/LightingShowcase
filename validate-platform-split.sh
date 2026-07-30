#!/usr/bin/env bash
set -euo pipefail

fail() {
  echo "Platform split validation failed: $*" >&2
  exit 1
}

linux_files=(
  "LightingShowcase.Linux.sln"
  "LightingShowcase.CommandLine/LightingShowcase.CommandLine.Linux.csproj"
  "build.sh"
  "LightingShowcase.CommandLine/render.sh"
)

for file in "${linux_files[@]}"; do
  [[ -f "$file" ]] || fail "missing Linux file: $file"
done

for obsolete in \
  "LightingShowcase.sln" \
  "LightingShowcase.csproj" \
  "LightingShowcase.CommandLine/LightingShowcase.CommandLine.csproj"; do
  [[ ! -e "$obsolete" ]] || fail "obsolete mixed project still exists: $obsolete"
done

# Windows entry points must reference the Windows CLI project and must never
# point at the Linux solution or Linux CLI project.
windows_files=(
  "LightingShowcase.Windows.sln"
  "LightingShowcase.Windows.csproj"
  "build.ps1"
  "publish-windows.ps1"
)

for file in "${windows_files[@]}"; do
  [[ -f "$file" ]] || fail "missing Windows file: $file"
done

grep -Fq 'LightingShowcase.CommandLine\LightingShowcase.CommandLine.Windows.csproj' LightingShowcase.Windows.sln \
  || fail "Windows solution does not reference the Windows CLI project"

grep -Fq 'LightingShowcase.CommandLine/LightingShowcase.CommandLine.Windows.csproj' LightingShowcase.Windows.csproj \
  || fail "Windows desktop project does not reference the Windows CLI project"

for file in "${windows_files[@]}"; do
  if grep -nE 'LightingShowcase\.CommandLine\.Linux\.csproj|LightingShowcase\.Linux\.sln' "$file"; then
    fail "a Windows build entry point contains a Linux project reference"
  fi
done

# Linux build entry points must not load the Windows desktop project or use
# Windows target frameworks and UI stacks.
windows_patterns=(
  'LightingShowcase\.Windows'
  'net[0-9.]+-windows'
  'UseWindowsForms'
  'UseWPF'
  'EnableWindowsTargeting'
  'WinExe'
  '\.exe([[:space:]]|$)'
  '[A-Za-z]:\\'
)

for pattern in "${windows_patterns[@]}"; do
  if grep -nEi "$pattern" "${linux_files[@]}"; then
    fail "a Linux build entry point contains a Windows-only reference"
  fi
done

# Linux MSBuild and solution paths must use portable forward slashes.
if grep -n '\\' \
  LightingShowcase.Linux.sln \
  LightingShowcase.CommandLine/LightingShowcase.CommandLine.Linux.csproj; then
  fail "Linux solution/project contains a backslash path"
fi


# The CLI must expose exactly the four supported renderer families.
for renderer in raster raster-vulkan vulkan cpu; do
  grep -Fq "$renderer" LightingShowcase.CommandLine/RenderRequest.cs \
    || fail "command-line renderer option is missing: $renderer"
done
grep -Fq -- '--renderer <name>' LightingShowcase.CommandLine/Program.cs \
  || fail "command-line help does not document --renderer"

echo "Platform split validation passed."
