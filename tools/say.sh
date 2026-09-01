#!/usr/bin/env bash
#
# Renders a phrase with the mod's own synthesiser, offline, and writes a WAV.
# Uses Besiege's embedded Mono and its own C# compiler, so the voice can be
# tuned and the code kept honest without launching the game.
#
#   ./tools/say.sh "hello world"
#   ./tools/say.sh "gg wp" out.wav 22050 122 1.0
#                   text    file  rate pitch speed
#
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
BUILD="${TMPDIR:-/tmp}/besiege-mp-tts-build"

source "$HERE/besiege-env.sh"

mkdir -p "$BUILD"
for tool in besiegecc monohost; do
    if [[ ! -x "$BUILD/$tool" || "$HERE/$tool.c" -nt "$BUILD/$tool" ]]; then
        gcc -O1 -o "$BUILD/$tool" "$HERE/$tool.c" -ldl
    fi
done

# Always rebuild: a stale checker that reports OK is worse than no checker
# (note 06 of the modding notes).
rm -f "$BUILD/synthtest.exe"
"$BUILD/besiegecc" -target:exe -out:"$BUILD/synthtest.exe" -lib:"$MANAGED" \
    -r:System.dll -r:System.Core.dll \
    "$ROOT"/src/Klatt/*.cs "$HERE/SynthTest.cs"

TARGET_ASM="$BUILD/synthtest.exe" exec "$BUILD/monohost" "$@"
