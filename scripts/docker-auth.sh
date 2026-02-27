#!/usr/bin/env bash
set -euo pipefail

IMAGE="${IMAGE:-elrucio:local}"
VOLUME="${VOLUME:-elrucio-data}"
ACTION="${1:-login}"

case "$ACTION" in
  login)
    CMD="copilot auth login"
    ;;
  status)
    CMD="copilot auth status"
    ;;
  *)
    echo "Usage: bash scripts/docker-auth.sh [login|status]"
    exit 1
    ;;
esac

docker run --rm -it \
  -v "$VOLUME:/var/lib/elrucio" \
  --entrypoint /bin/sh \
  "$IMAGE" -c "$CMD"
