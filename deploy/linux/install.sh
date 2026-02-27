#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
SRC_DIR="${SRC_DIR:-/opt/elrucio-src}"
APP_DIR="${APP_DIR:-/opt/elrucio}"
ENV_DIR="${ENV_DIR:-/etc/elrucio}"
ENV_FILE="${ENV_FILE:-$ENV_DIR/elrucio.env}"
DATA_DIR="${DATA_DIR:-/var/lib/elrucio/data}"
SERVICE_USER="${SERVICE_USER:-elrucio}"
SERVICE_GROUP="${SERVICE_GROUP:-elrucio}"
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
  echo "[0/7] Running preflight"
  SRC_DIR="$SRC_DIR" ENV_FILE="$ENV_FILE" "$SRC_DIR/deploy/linux/preflight.sh"
fi

echo "[1/7] Publishing ElRucio.Host to $APP_DIR"
mkdir -p "$APP_DIR"
dotnet publish "$SRC_DIR/src/ElRucio.Host/ElRucio.Host.csproj" -c Release -o "$APP_DIR"
chown -R root:root "$APP_DIR"
chmod -R go-w "$APP_DIR"

echo "[2/7] Ensuring service user and runtime data directory"
if ! getent group "$SERVICE_GROUP" >/dev/null 2>&1; then
  groupadd --system "$SERVICE_GROUP"
fi
if ! id -u "$SERVICE_USER" >/dev/null 2>&1; then
  useradd --system --gid "$SERVICE_GROUP" --home /var/lib/elrucio --shell /usr/sbin/nologin "$SERVICE_USER"
fi
mkdir -p "$DATA_DIR"
chown -R "$SERVICE_USER:$SERVICE_GROUP" "$DATA_DIR"
chmod 750 "$DATA_DIR"

echo "[3/7] Installing environment file"
mkdir -p "$ENV_DIR"
if [[ ! -f "$ENV_FILE" ]]; then
  cp "$SRC_DIR/deploy/linux/elrucio.env.example" "$ENV_FILE"
  echo "Created $ENV_FILE from template."
fi
chmod 640 "$ENV_FILE"
chown root:"$SERVICE_GROUP" "$ENV_FILE"

echo "[4/7] Installing systemd unit"
cp "$SRC_DIR/deploy/linux/elrucio.service" "$UNIT_FILE"

echo "[5/7] Reloading systemd"
systemctl daemon-reload

echo "[6/7] Enabling service"
systemctl enable "$APP_NAME"

echo "[7/7] Starting service"
systemctl restart "$APP_NAME"

echo
echo "Install complete."
echo "Next: edit $ENV_FILE with your real Slack/OpenAI keys (or Telegram if you switch providers) and allowed chat id, then run:"
echo "  systemctl restart $APP_NAME"
echo "  systemctl status $APP_NAME --no-pager"
echo "  journalctl -u $APP_NAME -f"
