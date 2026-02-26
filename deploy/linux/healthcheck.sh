#!/usr/bin/env bash
set -euo pipefail

APP_NAME="elrucio"
ENV_FILE="${ENV_FILE:-/etc/elrucio/elrucio.env}"
APP_DIR="${APP_DIR:-/opt/elrucio}"

echo "== El Rucio healthcheck =="

echo
echo "[1] systemd status"
if systemctl is-active --quiet "$APP_NAME"; then
  echo "service: active"
else
  echo "service: NOT active"
fi
systemctl status "$APP_NAME" --no-pager | sed -n '1,12p'

echo
echo "[2] process"
pgrep -af "ElRucio.Host.dll|dotnet .*ElRucio.Host" || echo "no matching process"

echo
echo "[3] files"
[[ -f "$APP_DIR/ElRucio.Host.dll" ]] && echo "ok: $APP_DIR/ElRucio.Host.dll" || echo "missing: $APP_DIR/ElRucio.Host.dll"
[[ -f "$ENV_FILE" ]] && echo "ok: $ENV_FILE" || echo "missing: $ENV_FILE"

echo
echo "[4] env keys presence (values hidden)"
if [[ -f "$ENV_FILE" ]]; then
  grep -q '^Telegram__BotToken=' "$ENV_FILE" && echo "Telegram__BotToken: present" || echo "Telegram__BotToken: missing"
  grep -q '^Voice__OpenAiApiKey=' "$ENV_FILE" && echo "Voice__OpenAiApiKey: present" || echo "Voice__OpenAiApiKey: missing"
  grep -q '^ElRucio__AllowedChatIds__0=' "$ENV_FILE" && echo "AllowedChatIds: present" || echo "AllowedChatIds: missing"
fi

echo
echo "[5] recent logs"
journalctl -u "$APP_NAME" -n 30 --no-pager || true
