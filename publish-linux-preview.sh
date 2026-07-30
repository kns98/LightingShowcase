#!/usr/bin/env bash
set -euo pipefail

root="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
out="${1:-$root/publish/preview-linux-x64}"

cd "$root"
rm -rf "$out"

dotnet restore ./LightingShowcase.Preview.Linux/LightingShowcase.Preview.Linux.csproj \
  --runtime linux-x64

dotnet publish ./LightingShowcase.Preview.Linux/LightingShowcase.Preview.Linux.csproj \
  --configuration Release \
  --runtime linux-x64 \
  --self-contained false \
  -p:SelfContained=false \
  -p:UseAppHost=true \
  --output "$out" \
  --no-restore

forbidden_runtime_files=(
  coreclr.dll clrjit.dll hostfxr.dll hostpolicy.dll System.Private.CoreLib.dll dotnet.exe
  libcoreclr.so libclrjit.so libhostfxr.so libhostpolicy.so createdump dotnet
  libcoreclr.dylib libclrjit.dylib libhostfxr.dylib libhostpolicy.dylib
)

for runtime_file in "${forbidden_runtime_files[@]}"; do
  if find "$out" -type f -name "$runtime_file" -print -quit | grep -q .; then
    printf 'Bundled .NET runtime file found: %s\n' "$runtime_file" >&2
    exit 1
  fi
done

"$out/LightingShowcase.Preview" --help >/dev/null
printf 'Linux preview frontend published to %s\n' "$out"
