#!/usr/bin/env bash
set -euo pipefail

SRC_DIR="${SRC_DIR:-/opt/elrucio-src}"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/elrucio}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root (sudo)."
  exit 1
fi

echo "== El Rucio doctor =="
echo "time (UTC): $(date -u +%Y-%m-%dT%H:%M:%SZ)"
echo

echo "--- preflight ---"
if [[ -x "$SRC_DIR/deploy/linux/preflight.sh" ]]; then
  if SRC_DIR="$SRC_DIR" "$SRC_DIR/deploy/linux/preflight.sh"; then
    echo "preflight: PASS"
  else
    echo "preflight: FAIL"
  fi
else
  echo "preflight script not found/executable: $SRC_DIR/deploy/linux/preflight.sh"
fi

echo
echo "--- healthcheck ---"
if [[ -x "$SRC_DIR/deploy/linux/healthcheck.sh" ]]; then
  "$SRC_DIR/deploy/linux/healthcheck.sh" || true
else
  echo "healthcheck script not found/executable: $SRC_DIR/deploy/linux/healthcheck.sh"
fi

echo
echo "--- hardening ---"
if [[ -f "$SRC_DIR/deploy/linux/hardening-check.sh" ]]; then
  if bash "$SRC_DIR/deploy/linux/hardening-check.sh"; then
    echo "hardening: PASS"
  else
    echo "hardening: FAIL"
  fi
else
  echo "hardening check script not found: $SRC_DIR/deploy/linux/hardening-check.sh"
fi

echo
echo "--- backup status ---"
latest_backup="$(ls -1t "$BACKUP_DIR"/elrucio-backup-*.tar.gz 2>/dev/null | head -n1 || true)"
if [[ -n "$latest_backup" ]]; then
  echo "latest backup: $latest_backup"
  if command -v stat >/dev/null 2>&1; then
    mtime_epoch="$(stat -c %Y "$latest_backup")"
    now_epoch="$(date +%s)"
    age_hours="$(( (now_epoch - mtime_epoch) / 3600 ))"
    echo "backup age: ${age_hours}h"
    if (( age_hours > 36 )); then
      echo "[WARN] Latest backup is older than 36h"
    else
      echo "[OK] Backup recency within 36h"
    fi
  fi
else
  echo "[WARN] No backups found in $BACKUP_DIR"
fi

echo
echo "doctor complete."
