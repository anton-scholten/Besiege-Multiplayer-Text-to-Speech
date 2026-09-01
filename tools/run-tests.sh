#!/usr/bin/env bash
#
# Runs the text-to-speech pipeline tests, offline, with Besiege's own compiler
# and Mono. Nothing to install and no game launch.
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

# Always rebuilt, so a checker that stops compiling fails the run rather than
# silently re-running the last binary.
rm -f "$BUILD/pipelinetest.exe"
"$BUILD/besiegecc" -target:exe -out:"$BUILD/pipelinetest.exe" -lib:"$MANAGED" \
    -r:System.dll -r:System.Core.dll \
    "$ROOT"/src/Klatt/*.cs "$HERE/PipelineTest.cs"

TARGET_ASM="$BUILD/pipelinetest.exe" exec "$BUILD/monohost"
