#!/usr/bin/env bash
set -euo pipefail

UNIT_FILE="${UNIT_FILE:-/etc/systemd/system/elrucio.service}"

warn() { echo "[WARN] $*"; }
ok() { echo "[OK]   $*"; }
fail() { echo "[FAIL] $*"; exit 1; }

echo "== El Rucio hardening check =="

[[ -f "$UNIT_FILE" ]] || fail "systemd unit not found: $UNIT_FILE"
ok "unit file found: $UNIT_FILE"

required_lines=(
  "User=elrucio"
  "Group=elrucio"
  "NoNewPrivileges=true"
  "PrivateTmp=true"
  "PrivateDevices=true"
  "ProtectHome=true"
  "ProtectSystem=strict"
  "ReadWritePaths=/var/lib/elrucio"
  "RestrictSUIDSGID=true"
  "LockPersonality=true"
)

missing=0
for line in "${required_lines[@]}"; do
  if grep -q "^${line}$" "$UNIT_FILE"; then
    ok "unit contains: $line"
  else
    warn "unit missing: $line"
    missing=1
  fi
done

if command -v systemctl >/dev/null 2>&1; then
  echo
  echo "Loaded unit properties:"
  systemctl show elrucio \
    -p User \
    -p Group \
    -p NoNewPrivileges \
    -p PrivateTmp \
    -p PrivateDevices \
    -p ProtectHome \
    -p ProtectSystem \
    -p ReadWritePaths \
    -p RestrictSUIDSGID \
    -p LockPersonality || true
fi

if [[ "$missing" -eq 1 ]]; then
  fail "hardening check failed"
fi

echo
echo "hardening check passed"
