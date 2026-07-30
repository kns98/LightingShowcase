#!/usr/bin/env bash
set -Eeuo pipefail

SDK_VERSION="${SDK_VERSION:-8.0.423}"
DOTNET_DIR="${DOTNET_DIR:-$HOME/.dotnet}"

log() { printf '[INFO] %s\n' "$*"; }
ok() { printf '[ OK ] %s\n' "$*"; }

if [[ ${EUID:-$(id -u)} -eq 0 ]]; then
  echo 'Run this script as your normal Linux user, not with sudo.' >&2
  exit 1
fi

command -v apt-get >/dev/null 2>&1 || {
  echo 'This setup script currently supports apt-based Linux distributions.' >&2
  exit 1
}

log 'Installing Linux build and Vulkan prerequisites.'
sudo apt-get update
sudo DEBIAN_FRONTEND=noninteractive apt-get install -y \
  ca-certificates curl tar gzip libicu-dev libvulkan1 vulkan-tools mesa-vulkan-drivers

if [[ ! -x "$DOTNET_DIR/dotnet" ]] || \
   ! "$DOTNET_DIR/dotnet" --list-sdks 2>/dev/null | awk '{print $1}' | grep -Fxq "$SDK_VERSION"; then
  installer="$(mktemp -t dotnet-install.XXXXXX.sh)"
  trap 'rm -f "${installer:-}"' EXIT
  curl --fail --location --silent --show-error --retry 3 \
    https://dot.net/v1/dotnet-install.sh -o "$installer"
  chmod +x "$installer"
  mkdir -p "$DOTNET_DIR"
  "$installer" --version "$SDK_VERSION" --install-dir "$DOTNET_DIR" --no-path
fi

begin='# >>> LightingShowcase dotnet >>>'
end='# <<< LightingShowcase dotnet <<<'
for rc in "$HOME/.bashrc" "$HOME/.profile"; do
  touch "$rc"
  sed -i "\|^$begin$|,\|^$end$|d" "$rc"
  cat >> "$rc" <<EOF

$begin
export DOTNET_ROOT="$DOTNET_DIR"
export DOTNET_ROOT_X64="$DOTNET_DIR"
export PATH="\$DOTNET_ROOT:\$DOTNET_ROOT/tools:\$PATH"
$end
EOF
done

export DOTNET_ROOT="$DOTNET_DIR"
export DOTNET_ROOT_X64="$DOTNET_DIR"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"
hash -r

ok "Installed .NET SDK $(dotnet --version)."
dotnet --info

if vulkaninfo --summary >/dev/null 2>&1; then
  ok 'Vulkan loader found at least one Vulkan device.'
  vulkaninfo --summary
else
  echo '[WARN] Vulkan device detection failed. CPU rendering can still be used.' >&2
  echo '[WARN] WSL requires a current Windows GPU driver and WSL graphics support.' >&2
fi

printf '\nRun the Linux build with:\n  ./build.sh\n'
