#!/usr/bin/env bash
set -euo pipefail

SRC_DIR="${SRC_DIR:-/opt/elrucio-src}"
ENV_FILE="${ENV_FILE:-/etc/elrucio/elrucio.env}"

warn() { echo "[WARN] $*"; }
ok() { echo "[OK]   $*"; }
fail() { echo "[FAIL] $*"; exit 1; }

echo "== El Rucio preflight =="

command -v bash >/dev/null 2>&1 || fail "bash not found"
command -v systemctl >/dev/null 2>&1 || fail "systemctl not found"
command -v dotnet >/dev/null 2>&1 || fail "dotnet not found"
ok "bash/systemctl/dotnet present"

if [[ ! -f "$SRC_DIR/ElRucio.slnx" ]]; then
  fail "source repo missing at $SRC_DIR (ElRucio.slnx not found)"
fi
ok "source repo found at $SRC_DIR"

for f in \
  "$SRC_DIR/deploy/linux/elrucio.service" \
  "$SRC_DIR/deploy/linux/elrucio.env.example" \
  "$SRC_DIR/deploy/linux/install.sh" \
  "$SRC_DIR/deploy/linux/update.sh" \
  "$SRC_DIR/deploy/linux/backup.sh" \
  "$SRC_DIR/deploy/linux/restore.sh"; do
  [[ -f "$f" ]] || fail "required file missing: $f"
done
ok "deploy assets present"

if [[ ! -f "$ENV_FILE" ]]; then
  warn "env file missing: $ENV_FILE"
  warn "create it from template: cp $SRC_DIR/deploy/linux/elrucio.env.example $ENV_FILE"
  exit 0
fi

ok "env file found: $ENV_FILE"

telegram_token="$(grep -E '^Telegram__BotToken=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
openai_key="$(grep -E '^Voice__OpenAiApiKey=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
allowed_chat="$(grep -E '^ElRucio__AllowedChatIds__0=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
video_enabled="$(grep -E '^Video__Enabled=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
video_provider="$(grep -E '^Video__Provider=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"
video_api_key="$(grep -E '^Video__ApiKey=' "$ENV_FILE" | head -n1 | cut -d= -f2- || true)"

[[ -n "$telegram_token" ]] || warn "Telegram__BotToken is empty"
[[ -n "$openai_key" ]] || warn "Voice__OpenAiApiKey is empty (STT disabled unless you set it)"
[[ -n "$allowed_chat" ]] || warn "ElRucio__AllowedChatIds__0 is empty (bot access not locked)"

if [[ "${video_enabled,,}" == "true" ]]; then
  [[ -n "$video_provider" ]] || warn "Video__Provider is empty"
  [[ -n "$video_api_key" ]] || warn "Video__ApiKey is empty (video/image analysis will fail)"
  if ! command -v ffmpeg >/dev/null 2>&1; then
    warn "ffmpeg not installed; video file analysis requires ffmpeg"
  else
    ok "ffmpeg present for video frame extraction"
  fi
fi

if [[ -n "$telegram_token" ]]; then
  if [[ "$telegram_token" =~ ^[0-9]+:[A-Za-z0-9_-]{30,}$ ]]; then
    ok "Telegram token format looks valid"
  else
    warn "Telegram token format looks unusual"
  fi

  if command -v curl >/dev/null 2>&1; then
    telegram_ok="$(curl -sS "https://api.telegram.org/bot${telegram_token}/getMe" | grep -c '"ok":true' || true)"
    if [[ "$telegram_ok" == "1" ]]; then
      ok "Telegram API token check passed"
    else
      warn "Telegram API token check failed"
    fi
  else
    warn "curl not installed; skipped Telegram API token check"
  fi
fi

echo "Preflight completed."
