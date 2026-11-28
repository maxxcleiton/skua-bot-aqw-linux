#!/usr/bin/env bash
set -euo pipefail

export DISPLAY="${DISPLAY:-:0.0}"
export XDG_RUNTIME_DIR="${XDG_RUNTIME_DIR:-/run/user/1000}"
export DOTNET_ROOT="${DOTNET_ROOT:-/home/max/.dotnet}"
export DOTNET_ROOT_X64="${DOTNET_ROOT_X64:-/home/max/.dotnet}"
export PATH="/home/max/.dotnet:/home/max/.local/bin:$PATH"

SKUA_BIN="/home/max/Skua-Linux/Skua.Linux/bin/Debug/net10.0/Skua"
SCRIPT="/home/max/Skua-Linux/CustomScripts/Ultras/UltraSpeaker.cs"
LOG_DIR="/home/max/.config/Skua/logs"
SKUA_DIR="/home/max/.config/Skua"

mkdir -p "$LOG_DIR"

if [[ ! -x "$SKUA_BIN" ]]; then
    echo "Skua executable not found: $SKUA_BIN" >&2
    exit 1
fi

if [[ ! -f "$SCRIPT" ]]; then
    echo "UltraSpeaker script not found: $SCRIPT" >&2
    exit 1
fi

if pgrep -x Skua >/dev/null; then
    echo "A Skua process is already running; stop existing Skua clients first." >&2
    exit 1
fi

get_password() {
    local account="$1"
    # Bengt uses the shared farm login saved under MrPresident BR.
    [[ "$account" != "bengt of worlds" ]] || account="MrPresident BR"
    python3 - "$SKUA_DIR/Skua.settings.json" "$account" <<'PYTHON'
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

# Clean stale UltraSpeaker sync files so reruns never stall on old state.
rm -f "$SKUA_DIR"/ultra_speaker*.sync \
      "$SKUA_DIR"/UltraSpeaker*.sync \
      "$SKUA_DIR/Options"/ultra_speaker*.sync \
      "$SKUA_DIR/Options"/UltraSpeaker*.sync

PIDS=()
cleanup() {
    echo "Stopping UltraSpeaker clients..."
    for pid in "${PIDS[@]:-}"; do
        kill -TERM "$pid" 2>/dev/null || true
    done
    wait 2>/dev/null || true
}
trap cleanup SIGINT SIGTERM EXIT

launch_account() {
    local account="$1"
    local logfile="$2"
    local password
    password="$(get_password "$account")"
    echo "Starting UltraSpeaker client: $account"
    "$SKUA_BIN" -u "$account" -p "$password" -s "Twilly" --run-script "$SCRIPT" \
        > "$LOG_DIR/$logfile" 2>&1 &
    PIDS+=("$!")
    sleep 6
}

launch_account "bengt of worlds" "speaker_bengt.log"
launch_account "Little_mett" "speaker_little_mett.log"
launch_account "MrPresident BR" "speaker_mrpresident.log"
launch_account "LeesDragon Prime" "speaker_leesdragon.log"

echo "UltraSpeaker 4-account run active (Bengt, Little_mett, MrPresident BR, LeesDragon Prime)."
wait "${PIDS[0]}" || true
for pid in "${PIDS[@]:1}"; do
    wait "$pid" 2>/dev/null || true
done
echo "UltraSpeaker run finished."
