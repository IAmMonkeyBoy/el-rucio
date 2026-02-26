#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
APP_DIR="${APP_DIR:-/opt/elrucio}"
ENV_FILE="${ENV_FILE:-/etc/elrucio/elrucio.env}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/elrucio}"
TEMP_DIR="$(mktemp -d)"

cleanup() {
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root (sudo)."
  exit 1
fi

archive="${1:-}"
if [[ -z "$archive" ]]; then
  echo "Usage: sudo bash restore.sh /var/backups/elrucio/elrucio-backup-<timestamp>.tar.gz"
  echo
  echo "Available backups:"
  ls -1 "$BACKUP_DIR"/elrucio-backup-*.tar.gz 2>/dev/null || true
  exit 1
fi

if [[ ! -f "$archive" ]]; then
  echo "Backup archive not found: $archive"
  exit 1
fi

echo "[1/7] Stopping service"
systemctl stop "$APP_NAME" || true

echo "[2/7] Extracting backup"
tar -xzf "$archive" -C "$TEMP_DIR"

root_dir="$(find "$TEMP_DIR" -mindepth 1 -maxdepth 1 -type d | head -n1)"
if [[ -z "$root_dir" ]]; then
  echo "Invalid backup archive format."
  exit 1
fi

echo "[3/7] Restoring DB"
mkdir -p "$APP_DIR/data"
if [[ -f "$root_dir/elrucio.db" ]]; then
  cp "$root_dir/elrucio.db" "$APP_DIR/data/elrucio.db"
  chmod 600 "$APP_DIR/data/elrucio.db"
  echo "DB restored to $APP_DIR/data/elrucio.db"
else
  echo "No elrucio.db in archive; skipping DB restore."
fi

echo "[4/7] Restoring env file"
if [[ -f "$root_dir/elrucio.env" ]]; then
  mkdir -p "$(dirname "$ENV_FILE")"
  cp "$root_dir/elrucio.env" "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  echo "Env restored to $ENV_FILE"
else
  echo "No elrucio.env in archive; skipping env restore."
fi

echo "[5/7] Starting service"
systemctl start "$APP_NAME"

echo "[6/7] Service status"
systemctl status "$APP_NAME" --no-pager | sed -n '1,14p'

echo "[7/7] Done"
echo "Restore complete from: $archive"
echo "Tail logs with: journalctl -u $APP_NAME -f"
