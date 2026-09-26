#!/bin/bash
# Run ON the Steam Frame: start frameeyeosc sending to the PC. Usage: headset-run.sh <PC-IP> [port]
# The PC address must be reachable from the headset (the Frame's own Wi-Fi AP subnet is typically 10.35.78.x).
set -euo pipefail
PC="${1:?usage: headset-run.sh <PC-IP> [port]}"
PORT="${2:-9020}"
pkill -x frameeyeosc 2>/dev/null || true
nohup ~/frameeyeosc/target/release/frameeyeosc --target "$PC:$PORT" > ~/frameeyeosc.log 2>&1 < /dev/null &
sleep 1; pgrep -a frameeyeosc; cat ~/frameeyeosc.log
