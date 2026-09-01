#!/usr/bin/env bash
#
# Builds the mod assembly with Besiege's own C# compiler, then checks the
# result against the loader's blacklist before the game gets a chance to
# refuse it silently.
#
#   ./tools/build.sh            build and check
#   ./tools/build.sh --check    compile to a temp file only (see verify-build.sh)
#
# To install, use tools/install.sh -- it builds first.
#
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
BUILD="${TMPDIR:-/tmp}/besiege-mp-tts-build"      # named for this mod, not shared
MOD="$ROOT/MultiplayerTTS"

CHECK_ONLY=0
if [[ "${1:-}" == "--check" ]]; then
    CHECK_ONLY=1
elif [[ -n "${1:-}" ]]; then
    echo "Unknown option: $1" >&2
    echo "Usage: $0 [--check]   (to install, use tools/install.sh)" >&2
    exit 1
fi

source "$HERE/besiege-env.sh"

mkdir -p "$BUILD"
for tool in besiegecc monohost; do
    if [[ ! -x "$BUILD/$tool" || "$HERE/$tool.c" -nt "$BUILD/$tool" ]]; then
        gcc -O1 -o "$BUILD/$tool" "$HERE/$tool.c" -ldl
    fi
done

# ---------------------------------------------------------------------------
# Compile
# ---------------------------------------------------------------------------
echo "== compiling"

# When only checking, compile somewhere else: a --check run must never leave
# the shipped assembly missing or half-written.
if [[ $CHECK_ONLY -eq 1 ]]; then
    OUT="$BUILD/MultiplayerTTS.check.dll"
else
    OUT="$MOD/MultiplayerTTS.dll"
fi
rm -f "$OUT"

# Every .cs under src/, found rather than globbed per directory: a fixed list
# of globs silently drops a whole new subdirectory, and the symptom is a
# missing-namespace error that reads like a bad reference.
SOURCES=()
while IFS= read -r f; do SOURCES+=("$f"); done \
    < <(find "$ROOT/src" -name '*.cs' -type f | sort)
[[ ${#SOURCES[@]} -gt 0 ]] || { echo "no sources under $ROOT/src" >&2; exit 1; }
echo "   ${#SOURCES[@]} source files"

if [[ -z "$UIFACTORY" ]]; then
    cat >&2 <<'EOF'
UI Factory 3 was not found, and the options panel is compiled against it.

  Subscribe to Workshop item 2913469777 ("UI Factory 3"), or set UIFACTORY_DIR
  to the folder holding Besiege.UI.dll.

The dependency is soft at *runtime* -- without UI Factory the mod still loads
and still reads chat aloud, it just has no panel -- but the assemblies have to
be here to build against.
EOF
    exit 1
fi
echo "   UI Factory: $UIFACTORY"

"$BUILD/besiegecc" -target:library -out:"$OUT" -lib:"$MANAGED" -lib:"$UIFACTORY" \
    -r:UnityEngine.dll -r:UnityEngine.UI.dll \
    -r:Assembly-CSharp.dll -r:Assembly-CSharp-firstpass.dll \
    -r:System.dll -r:System.Core.dll \
    -r:Besiege.UI.dll -r:Besiege.UI.Bridge.dll \
    "${SOURCES[@]}"

[[ -f "$OUT" ]] || { echo "no assembly produced" >&2; exit 1; }

# ---------------------------------------------------------------------------
# Blacklist check
#
# The loader refuses an assembly that references any blacklisted namespace and
# says so only in Player.log, as a mod that never appears. Catching it here
# means a file and a line number instead of a mystery.
#
# The checker is always rebuilt: note 06 of the modding notes records a build
# script that silenced its checker's compile and then ran a stale binary that
# reported OK forever.
# ---------------------------------------------------------------------------
echo "== checking against the loader blacklist"
rm -f "$BUILD/blacklist.exe"
"$BUILD/besiegecc" -target:exe -out:"$BUILD/blacklist.exe" -lib:"$MANAGED" \
    -r:Mono.Cecil.dll -r:System.dll -r:System.Core.dll "$HERE/BlacklistCheck.cs"

TARGET_ASM="$BUILD/blacklist.exe" "$BUILD/monohost" "$OUT"

# ---------------------------------------------------------------------------
# Manifest check
# ---------------------------------------------------------------------------
echo "== checking the manifest"
python3 "$HERE/check-manifest.py" "$MOD/Mod.xml"

if [[ $CHECK_ONLY -eq 1 ]]; then
    echo "== build OK (check only; $(stat -c%s "$OUT") bytes, not installed)"
    exit 0
fi

echo "== built $OUT"
echo "   Run tools/install.sh to put it in Besiege_Data/Mods."
