#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
APP_DIR="${APP_DIR:-/opt/elrucio}"
ENV_FILE="${ENV_FILE:-/etc/elrucio/elrucio.env}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/elrucio}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"

timestamp="$(date -u +%Y%m%d-%H%M%SZ)"
work_dir="$BACKUP_DIR/$timestamp"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root (sudo)."
  exit 1
fi

mkdir -p "$work_dir"

echo "[1/4] Backup SQLite DB"
db_candidates=(
  "$APP_DIR/data/elrucio.db"
  "/opt/elrucio-src/src/ElRucio.Host/data/elrucio.db"
)

db_found=""
for p in "${db_candidates[@]}"; do
  if [[ -f "$p" ]]; then
    db_found="$p"
    break
  fi
done

if [[ -n "$db_found" ]]; then
  cp "$db_found" "$work_dir/elrucio.db"
  echo "DB copied from: $db_found"
else
  echo "DB not found in expected paths; skipping DB copy."
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
