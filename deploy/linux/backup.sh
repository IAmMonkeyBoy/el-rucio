#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
ENV_FILE="${ENV_FILE:-/etc/elrucio/elrucio.env}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/elrucio}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"
DEFAULT_DATA_DIR="${DEFAULT_DATA_DIR:-/var/lib/elrucio/data}"

timestamp="$(date -u +%Y%m%d-%H%M%SZ)"
work_dir="$BACKUP_DIR/$timestamp"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root (sudo)."
  exit 1
fi

mkdir -p "$work_dir"

data_dir="$DEFAULT_DATA_DIR"
if [[ -f "$ENV_FILE" ]]; then
  parsed_data_dir="$(grep -E '^ElRucio__DataDir=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
  if [[ -n "$parsed_data_dir" ]]; then
    data_dir="$parsed_data_dir"
  fi
fi

echo "[1/4] Backup runtime data directory"
if [[ -d "$data_dir" ]]; then
  cp -a "$data_dir" "$work_dir/data"
  echo "Data copied from: $data_dir"
else
  echo "Data directory not found: $data_dir"
fi

echo "[2/4] Backup env file"
if [[ -f "$ENV_FILE" ]]; then
  cp "$ENV_FILE" "$work_dir/elrucio.env"
  chmod 600 "$work_dir/elrucio.env"
else
  echo "Env file not found: $ENV_FILE"
fi

echo "[3/4] Capture recent logs"
journalctl -u "$APP_NAME" -n 500 --no-pager > "$work_dir/journal-last500.log" || true

echo "[4/4] Pack archive + rotate"
archive="$BACKUP_DIR/elrucio-backup-$timestamp.tar.gz"
tar -C "$BACKUP_DIR" -czf "$archive" "$timestamp"
rm -rf "$work_dir"

find "$BACKUP_DIR" -type f -name 'elrucio-backup-*.tar.gz' -mtime "+$RETENTION_DAYS" -delete || true

echo "Backup complete: $archive"
