#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROPS_FILE="$SCRIPT_DIR/Directory.Build.local.props"
OUT="$SCRIPT_DIR/SE2.Source/"

read_prop() {
  local name="$1"
  awk -v name="$name" '
    {
      pattern = "<" name ">[^<]+</" name ">"
      if (match($0, pattern)) {
        value = substr($0, RSTART + length(name) + 2, RLENGTH - (2 * length(name)) - 5)
        gsub(/^[ \t]+|[ \t]+$/, "", value)
        print value
        exit
      }
    }
  ' "$PROPS_FILE"
}

if [ ! -f "$PROPS_FILE" ]; then
  echo "Error: props file not found: $PROPS_FILE" >&2
  exit 1
fi

SE2_INSTALL_PATH="$(read_prop "SE2InstallPath")"
MANAGED="$(read_prop "SE2Game2Path")"
MANAGED="${MANAGED//\$(SE2InstallPath)/$SE2_INSTALL_PATH}"

if [ -z "$MANAGED" ]; then
  echo "Error: SE2Game2Path not found in $PROPS_FILE" >&2
  exit 1
fi

if [ ! -d "$MANAGED" ]; then
  echo "Error: SE2Game2Path does not exist: $MANAGED" >&2
  exit 1
fi

mkdir -p "$OUT"

for f in "$MANAGED"/*.dll; do
  [ -e "$f" ] || continue
  if ! ilspycmd -p --nested-directories -r "$MANAGED" -o "$OUT" "$f"; then
    echo "Warning: failed to decompile $f — skipping" >&2
  fi
done
