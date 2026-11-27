#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:0.0}"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/1000}"
export DOTNET_ROOT="${DOTNET_ROOT:-/home/max/.dotnet}"
export DOTNET_ROOT_X64="${DOTNET_ROOT_X64:-/home/max/.dotnet}"
export PATH="/home/max/.dotnet:/home/max/.local/bin:$PATH"

SKUA_BIN="/home/max/Skua-Linux/Skua.Linux/bin/Debug/net10.0/Skua"
HDBON_SCRIPT="/home/max/Skua-Linux/CustomScripts/HDBoN/HollowbornDragonBladeOfNulgath.cs"
BUTLER_SCRIPT="/home/max/Skua-Linux/CustomScripts/ButlerV4/Butlerv4.cs"
LOG_DIR="/home/max/.config/Skua/logs"
SYNC_FILE="/home/max/.config/Skua/leader_sync_ports.json"
mkdir -p "$LOG_DIR"

for candidate in "$SKUA_BIN"; do
    if [[ ! -x "$candidate" ]]; then
        echo "Skua executable not found: $candidate" >&2
        exit 1
    fi
done

if [[ ! -f "$HDBON_SCRIPT" || ! -f "$BUTLER_SCRIPT" ]]; then
    echo "The HDBoN or Butler V4 script is missing." >&2
    exit 1
fi

if pgrep -x Skua >/dev/null; then
    echo "A Skua process is already running; refusing to create duplicate accounts." >&2
    exit 1
fi

get_password() {
    local account="$1"
    # Bengt uses the existing shared farm login saved under MrPresident.
    [[ "$account" != "bengt of worlds" ]] || account="MrPresident BR"
    python3 - "$HOME/.config/Skua/Skua.settings.json" "$account" <<'PYTHON'
import json
import sys

with open(sys.argv[1]) as settings:
    accounts = json.load(settings)["manager"]["ManagedAccounts"]
password = accounts.get(sys.argv[2], {}).get("Password")
if not password:
    sys.exit(f"No saved password for {sys.argv[2]}.")
print(password)
PYTHON
}

# Remove only the stale runtime port map; all Skua processes were checked above.
rm -f "$SYNC_FILE"
rm -f "$LOG_DIR"/hdbon_party_*.status "$LOG_DIR/hdbon_potion_prep.lock" "$LOG_DIR/hdbon_leader_room.status"

PIDS=()
cleanup() {
    echo "Stopping HDBoN farm clients..."
    for pid in "${PIDS[@]:-}"; do
        kill -TERM "$pid" 2>/dev/null || true
    done
    wait 2>/dev/null || true
}
trap cleanup SIGINT SIGTERM EXIT

PASS="$(get_password "bengt of worlds")"
echo "Starting Bengt with the HDBoN progression runner."
"$SKUA_BIN" -u "bengt of worlds" -p "$PASS" -s "Twilly" --run-script "$HDBON_SCRIPT" \
    > "$LOG_DIR/hdbon_bengt.log" 2>&1 &
BENGT_PID=$!
PIDS+=("$BENGT_PID")

VALIDATED=0
for _ in $(seq 1 180); do
    if ! kill -0 "$BENGT_PID" 2>/dev/null; then
        echo "Bengt's Skua process exited during startup; inspect $LOG_DIR/hdbon_bengt.log." >&2
        exit 1
    fi
    if grep -aFq "HDBON_RUNNER_V1: Bengt validated" "$LOG_DIR/hdbon_bengt.log"; then
        VALIDATED=1
        break
    fi
    if grep -aFq "HDBON is paused:" "$LOG_DIR/hdbon_bengt.log"; then
        echo "Bengt failed the runner's level or membership check; inspect $LOG_DIR/hdbon_bengt.log." >&2
        exit 1
    fi
    sleep 1
done

if [[ "$VALIDATED" -ne 1 ]]; then
    echo "HDBoN runner did not reach its validated start marker; inspect $LOG_DIR/hdbon_bengt.log." >&2
    exit 1
fi

echo "Bengt validated. Waiting for Butler V4 sync registration."
SYNC_READY=0
for _ in $(seq 1 120); do
    if [[ -f "$SYNC_FILE" ]] && grep -aiFq 'bengt of worlds' "$SYNC_FILE"; then
        SYNC_READY=1
        break
    fi
    sleep 1
done

if [[ "$SYNC_READY" -ne 1 ]]; then
    echo "Bengt's Butler V4 sync port was not registered; inspect $LOG_DIR/hdbon_bengt.log." >&2
    exit 1
fi

launch_butler() {
    local account="$1"
    local logfile="$2"
    local password
    password="$(get_password "$account")"
    echo "Starting Butler V4: $account"
    "$SKUA_BIN" -u "$account" -p "$password" -s "Twilly" --run-script "$BUTLER_SCRIPT" \
        > "$LOG_DIR/$logfile" 2>&1 &
    PIDS+=("$!")
    sleep 2
}

launch_butler "MrPresident BR" "hdbon_mrpresident.log"
launch_butler "LeesDragon Prime" "hdbon_leesdragon.log"
launch_butler "The Borges Warrion" "hdbon_borges.log"

launch_butler "Little_mett" "hdbon_little_mett.log"
launch_butler "Bella of Worlds" "hdbon_bella.log"

echo "HDBoN farm active: Bengt and five Butler V4 helpers."
while kill -0 "$BENGT_PID" 2>/dev/null; do
    sleep 10
done

echo "Bengt's Skua process exited."
