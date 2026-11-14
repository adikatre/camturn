#!/usr/bin/env bash
# Installs the .NET 10 SDK into ~/.dotnet (no root needed). Safe to re-run.
set -euo pipefail
if [ -x "$HOME/.dotnet/dotnet" ] && "$HOME/.dotnet/dotnet" --list-sdks | grep -q '^10\.'; then
  echo ".NET 10 SDK already installed in ~/.dotnet"
  exit 0
fi
script="$(mktemp)"
trap 'rm -f "$script"' EXIT
curl -sSL https://dot.net/v1/dotnet-install.sh -o "$script"
bash "$script" --channel 10.0 --install-dir "$HOME/.dotnet"
echo 'Add to PATH: export PATH="$HOME/.dotnet:$PATH"'
