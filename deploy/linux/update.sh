#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
SRC_DIR="${SRC_DIR:-/opt/elrucio-src}"
APP_DIR="${APP_DIR:-/opt/elrucio}"
RUN_BACKUP="${RUN_BACKUP:-1}"
RUN_PREFLIGHT="${RUN_PREFLIGHT:-1}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root (sudo)."
  exit 1
fi

if [[ ! -d "$SRC_DIR/.git" ]]; then
  echo "Expected git repo at $SRC_DIR"
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but not installed."
  exit 1
fi

if [[ "$RUN_PREFLIGHT" == "1" ]] && [[ -x "$SRC_DIR/deploy/linux/preflight.sh" ]]; then
  echo "[pre] Running preflight"
  "$SRC_DIR/deploy/linux/preflight.sh"
fi

if [[ "$RUN_BACKUP" == "1" ]] && [[ -x "$SRC_DIR/deploy/linux/backup.sh" ]]; then
  echo "[0/5] Running backup"
  "$SRC_DIR/deploy/linux/backup.sh"
fi

echo "[1/5] Pulling latest source"
git -C "$SRC_DIR" pull --ff-only

echo "[2/5] Restoring"
dotnet restore "$SRC_DIR/ElRucio.slnx"

echo "[3/5] Publishing"
mkdir -p "$APP_DIR"
dotnet publish "$SRC_DIR/src/ElRucio.Host/ElRucio.Host.csproj" -c Release -o "$APP_DIR"

echo "[4/5] Restarting service"
systemctl restart "$APP_NAME"

echo "[5/5] Verifying status"
systemctl status "$APP_NAME" --no-pager

echo "[post] Hardening drift check (non-blocking)"
if [[ -f "$SRC_DIR/deploy/linux/hardening-check.sh" ]]; then
  if bash "$SRC_DIR/deploy/linux/hardening-check.sh"; then
    echo "hardening: PASS"
  else
    echo "[WARN] hardening drift detected; review deploy/linux/elrucio.service and reload systemd"
  fi
else
  echo "[WARN] hardening-check script missing: $SRC_DIR/deploy/linux/hardening-check.sh"
fi

echo
echo "Update complete. Tail logs with: journalctl -u $APP_NAME -f"
