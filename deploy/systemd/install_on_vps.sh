#!/usr/bin/env bash
set -euo pipefail

APP_DIR="/root/project-multiplayer-server"
UNIT_SRC="${APP_DIR}/projectmultiplayer.service"
UNIT_DST="/etc/systemd/system/projectmultiplayer.service"

if [[ ! -x "${APP_DIR}/ServerProjectMultiplayer.x86_64" ]]; then
  echo "ERROR: ${APP_DIR}/ServerProjectMultiplayer.x86_64 tidak ditemukan / belum executable."
  exit 1
fi

if [[ ! -f "${UNIT_SRC}" ]]; then
  echo "ERROR: unit file ${UNIT_SRC} tidak ditemukan."
  exit 1
fi

cp -f "${UNIT_SRC}" "${UNIT_DST}"
systemctl daemon-reload
systemctl enable projectmultiplayer.service
systemctl restart projectmultiplayer.service

echo "=== STATUS ==="
systemctl --no-pager --full status projectmultiplayer.service | sed -n '1,30p'

echo "=== LAST LOGS ==="
journalctl -u projectmultiplayer.service -n 60 --no-pager
