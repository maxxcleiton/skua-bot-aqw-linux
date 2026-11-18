#!/usr/bin/env bash
set -euo pipefail

DIR="$(cd "$(dirname "$0")" && pwd)"
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.local/bin:$HOME/.dotnet:$DIR/.tools/jdk-17.0.21+9-jre/bin:$PATH"
export JAVA_HOME="$DIR/.tools/jdk-17.0.21+9-jre"

OUT_DIR="$DIR/Skua.Linux/bin/Debug/net10.0"
if [ ! -f "$OUT_DIR/Skua" ]; then
    "$DIR/build-linux.sh"
fi

ln -sfn "$DIR/FFDec" "$OUT_DIR/FFDec"
cd "$OUT_DIR"
exec ./Skua "$@"
