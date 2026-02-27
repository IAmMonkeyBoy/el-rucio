#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
ENV_FILE="${ENV_FILE:-/etc/elrucio/elrucio.env}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/elrucio}"
DEFAULT_DATA_DIR="${DEFAULT_DATA_DIR:-/var/lib/elrucio/data}"
SERVICE_USER="${SERVICE_USER:-elrucio}"
SERVICE_GROUP="${SERVICE_GROUP:-elrucio}"
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

data_dir="$DEFAULT_DATA_DIR"
if [[ -f "$ENV_FILE" ]]; then
  parsed_data_dir="$(grep -E '^ElRucio__DataDir=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
  if [[ -n "$parsed_data_dir" ]]; then
    data_dir="$parsed_data_dir"
  fi
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

echo "[3/7] Restoring runtime data directory"
if [[ -d "$root_dir/data" ]]; then
  mkdir -p "$(dirname "$data_dir")"
  rm -rf "$data_dir"
  cp -a "$root_dir/data" "$data_dir"
  if id -u "$SERVICE_USER" >/dev/null 2>&1; then
    chown -R "$SERVICE_USER:$SERVICE_GROUP" "$data_dir"
  fi
  echo "Data restored to $data_dir"
else
  echo "No data directory in archive; skipping data restore."
fi

echo "[4/7] Restoring env file"
if [[ -f "$root_dir/elrucio.env" ]]; then
  mkdir -p "$(dirname "$ENV_FILE")"
  cp "$root_dir/elrucio.env" "$ENV_FILE"
  chmod 640 "$ENV_FILE"
  if getent group "$SERVICE_GROUP" >/dev/null 2>&1; then
    chown root:"$SERVICE_GROUP" "$ENV_FILE"
  fi
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
