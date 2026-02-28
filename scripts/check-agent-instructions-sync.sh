#!/usr/bin/env bash
set -euo pipefail

canonical="AGENTS.md"
mirror=".github/copilot-instructions.md"

if [[ ! -f "$canonical" ]]; then
  echo "Missing canonical instructions file: $canonical" >&2
  exit 1
fi

if [[ ! -f "$mirror" ]]; then
  echo "Missing mirror instructions file: $mirror" >&2
  exit 1
fi

if ! cmp -s "$canonical" "$mirror"; then
  echo "Instruction files are out of sync:" >&2
  echo "  - $canonical" >&2
  echo "  - $mirror" >&2
  echo >&2
  echo "Run this command to inspect drift:" >&2
  echo "  diff -u $canonical $mirror" >&2
  exit 1
fi

echo "Instruction files are synchronized."