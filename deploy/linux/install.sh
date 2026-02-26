#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
SRC_DIR="${SRC_DIR:-/opt/elrucio-src}"
APP_DIR="${APP_DIR:-/opt/elrucio}"
ENV_DIR="${ENV_DIR:-/etc/elrucio}"
ENV_FILE="${ENV_FILE:-$ENV_DIR/elrucio.env}"
UNIT_FILE="/etc/systemd/system/${APP_NAME}.service"
RUN_PREFLIGHT="${RUN_PREFLIGHT:-1}"

if [[ "${EUID}" -ne 0 ]]; then
  echo "Run as root (sudo)."
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "dotnet is required but not installed. Install .NET SDK/runtime first."
  exit 1
fi

if [[ ! -f "$SRC_DIR/ElRucio.slnx" ]]; then
  echo "Source repo not found at $SRC_DIR (missing ElRucio.slnx)."
  exit 1
fi

if [[ "$RUN_PREFLIGHT" == "1" ]] && [[ -x "$SRC_DIR/deploy/linux/preflight.sh" ]]; then
  echo "[0/6] Running preflight"
  SRC_DIR="$SRC_DIR" ENV_FILE="$ENV_FILE" "$SRC_DIR/deploy/linux/preflight.sh"
fi

echo "[1/6] Publishing ElRucio.Host to $APP_DIR"
mkdir -p "$APP_DIR"
dotnet publish "$SRC_DIR/src/ElRucio.Host/ElRucio.Host.csproj" -c Release -o "$APP_DIR"

echo "[2/6] Installing environment file"
mkdir -p "$ENV_DIR"
if [[ ! -f "$ENV_FILE" ]]; then
  cp "$SRC_DIR/deploy/linux/elrucio.env.example" "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  echo "Created $ENV_FILE from template."
fi

echo "[3/6] Installing systemd unit"
cp "$SRC_DIR/deploy/linux/elrucio.service" "$UNIT_FILE"

echo "[4/6] Reloading systemd"
systemctl daemon-reload

echo "[5/6] Enabling service"
systemctl enable "$APP_NAME"

echo "[6/6] Starting service"
systemctl restart "$APP_NAME"

echo
echo "Install complete."
echo "Next: edit $ENV_FILE with your real Telegram/OpenAI keys and allowed chat id, then run:"
echo "  systemctl restart $APP_NAME"
echo "  systemctl status $APP_NAME --no-pager"
echo "  journalctl -u $APP_NAME -f"
