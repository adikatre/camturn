#!/usr/bin/env bash
# Acceptance criterion 7: CamTurn must not use, require, bundle or tell users to install OBS. The only allowed
# mention is the README FAQ line explaining that CamTurn does not need it.
set -euo pipefail
cd "$(dirname "$0")/.."
hits=$(git grep -n -I -i -w -E 'obs|obs-studio|obs virtual' -- . ':!scripts/check-no-obs.sh' || true)
allowed=$(printf '%s\n' "$hits" | grep -c '^README.md:.*does not need' || true)
other=$(printf '%s\n' "$hits" | grep -v '^README.md:.*does not need' | grep -v '^$' || true)
if [ -n "$other" ]; then
  echo "Forbidden OBS references:"
  echo "$other"
  exit 1
fi
if [ "$allowed" -gt 1 ]; then
  echo "More than one README FAQ mention of OBS"
  exit 1
fi
echo "OK: no OBS references outside the single README FAQ line."
