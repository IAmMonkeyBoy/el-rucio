#!/usr/bin/env bash
set -euo pipefail

SRC_DIR="${SRC_DIR:-/opt/elrucio-src}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root (sudo)."
  exit 1
fi

if [[ ! -f "$SRC_DIR/deploy/linux/elrucio-backup.service" ]]; then
  echo "Missing $SRC_DIR/deploy/linux/elrucio-backup.service"
  exit 1
fi

if [[ ! -f "$SRC_DIR/deploy/linux/elrucio-backup.timer" ]]; then
  echo "Missing $SRC_DIR/deploy/linux/elrucio-backup.timer"
  exit 1
fi

cp "$SRC_DIR/deploy/linux/elrucio-backup.service" /etc/systemd/system/elrucio-backup.service
cp "$SRC_DIR/deploy/linux/elrucio-backup.timer" /etc/systemd/system/elrucio-backup.timer

systemctl daemon-reload
systemctl enable --now elrucio-backup.timer

echo "Backup timer enabled."
systemctl list-timers --all | grep -E 'elrucio-backup|NEXT|LAST' || true
