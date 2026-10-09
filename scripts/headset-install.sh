#!/bin/bash
# Run ON the Steam Frame (setup.ps1 -Headset copies it to ~/steamframe and runs it).
#   headset-install.sh --info            print the PC address this SSH session comes from and the current service target
#   headset-install.sh <PC-IP> [port]    build frameeyeosc if needed and install/refresh the auto-start service
#   headset-install.sh --off             stop frameeyeosc and disable its auto-start (the PC then uses SteamVR's eye data)
# Everything stays in $HOME. Undo: systemctl --user disable --now frameeyeosc; loginctl disable-linger "$USER"
set -euo pipefail
DIR="$(cd "$(dirname "$0")" && pwd)"
UNIT="$HOME/.config/systemd/user/frameeyeosc.service"

if [ "${1:-}" = "--info" ]; then
  echo "CLIENT=${SSH_CLIENT%% *}"
  if [ -f "$UNIT" ]; then grep -o -- '--target [^ ]*' "$UNIT" | cut -d' ' -f2 | sed 's/^/UNIT=/'; fi
  exit 0
fi

if [ "${1:-}" = "--off" ]; then
  # for people who enabled frameeyeosc with an older version: stop it and keep it off (unit and checkout stay, lingering is not touched)
  systemctl --user disable --now frameeyeosc 2>/dev/null || true
  pkill -x frameeyeosc 2>/dev/null || true
  echo "service: $(systemctl --user is-active frameeyeosc || true), enabled: $(systemctl --user is-enabled frameeyeosc 2>/dev/null || true)"
  exit 0
fi

PC="${1:?usage: headset-install.sh <PC-IP> [port]  |  --info  |  --off}"
PORT="${2:-9020}"

# frameeyeosc (as built by headset-setup.sh) understands shared-memory layout versions 4 and 5
if [ -r /dev/shm/eye-server.mmap ]; then
  ver=$(od -A n -t u4 -N 4 /dev/shm/eye-server.mmap | tr -d ' ')
  if [ "$ver" != "4" ] && [ "$ver" != "5" ]; then echo "WARNING: eye shared memory is version $ver, frameeyeosc expects 4 or 5 (a Frame update changed it); it may refuse to start"; fi
else
  echo "note: /dev/shm/eye-server.mmap not present yet (eye tracking service not running?); the service will retry"
fi

bash "$DIR/headset-setup.sh"

mkdir -p "$(dirname "$UNIT")"
sed "s/PC_IP:9020/$PC:$PORT/" "$DIR/frameeyeosc.service" > "$UNIT.new"
if [ -f "$UNIT" ] && cmp -s "$UNIT" "$UNIT.new"; then
  rm -f "$UNIT.new"; echo "service unchanged (target $PC:$PORT)"
else
  mv "$UNIT.new" "$UNIT"; echo "service written (target $PC:$PORT)"
fi

# a copy started by hand (headset-run.sh) would send twice
pkill -x frameeyeosc 2>/dev/null || true
systemctl --user daemon-reload
systemctl --user enable frameeyeosc >/dev/null
systemctl --user restart frameeyeosc
loginctl enable-linger "$USER" 2>/dev/null || echo "note: could not enable lingering; the service starts when you log in on the headset"
sleep 2
state=$(systemctl --user is-active frameeyeosc || true)
echo "service: $state"
[ "$state" = "active" ]
