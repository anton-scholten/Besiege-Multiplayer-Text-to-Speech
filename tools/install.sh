#!/usr/bin/env bash
#
# Installs the mod into Besiege's Mods folder.
#
#   ./tools/install.sh            symlink the mod (best for development -- a
#                                 rebuilt assembly is picked up on restart)
#   ./tools/install.sh --copy     copy the mod instead (for handing it to someone)
#   ./tools/install.sh --uninstall
#
# Set BESIEGE_DIR to point at your install if it is not auto-detected, e.g.
#   BESIEGE_DIR="$HOME/.steam/steam/steamapps/common/Besiege" ./tools/install.sh

set -euo pipefail

REPO_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
MOD_NAME="MultiplayerTTS"
SRC="$REPO_DIR/$MOD_NAME"

find_besiege() {
    if [[ -n "${BESIEGE_DIR:-}" ]]; then
        echo "$BESIEGE_DIR"
        return
    fi

    local candidates=(
        "$HOME/.steam/steam/steamapps/common/Besiege"
        "$HOME/.local/share/Steam/steamapps/common/Besiege"
        "$HOME/Steam/steamapps/common/Besiege"
        "$HOME/Library/Application Support/Steam/steamapps/common/Besiege"
    )
    # Any additional Steam library folders configured on this machine.
    local vdf
    for vdf in "$HOME/.steam/steam/steamapps/libraryfolders.vdf" \
               "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf" \
               "$HOME/.steam/debian-installation/steamapps/libraryfolders.vdf"; do
        [[ -f "$vdf" ]] || continue
        while read -r lib; do
            candidates+=("$lib/steamapps/common/Besiege")
        done < <(grep -oE '"path"[[:space:]]+"[^"]+"' "$vdf" | sed -E 's/.*"([^"]+)"$/\1/')
    done

    local dir
    for dir in "${candidates[@]}"; do
        if [[ -d "$dir/Besiege_Data/Mods" ]]; then
            echo "$dir"
            return
        fi
    done
    return 1
}

if ! BESIEGE="$(find_besiege)"; then
    echo "Could not find Besiege. Set BESIEGE_DIR to your install directory." >&2
    exit 1
fi

MODS="$BESIEGE/Besiege_Data/Mods"
DEST="$MODS/$MOD_NAME"
echo "Besiege:  $BESIEGE"
echo "Mods dir: $MODS"

# The mod ships a pre-built assembly (see Mod.xml), so building is part of
# installing, and has to happen before --copy takes a snapshot of the folder.
# build.sh uses Besiege's own compiler -- no C# toolchain needed -- and runs the
# loader's own blacklist and entry-point checks, which is what stops a refused
# assembly being installed as a mod that silently never appears.
if [[ "${1:-}" != "--uninstall" ]]; then
    if ! BESIEGE_DIR="$BESIEGE" "$REPO_DIR/tools/build.sh"; then
        cat >&2 <<'EOF'

The mod's assembly could not be built, so nothing was installed.
Fix the error above and re-run this script.
EOF
        exit 1
    fi
    echo
fi

mkdir -p "$MODS"

case "${1:-}" in
    --uninstall)
        if [[ -L "$DEST" ]]; then
            rm "$DEST"
            echo "Removed symlink $DEST"
        elif [[ -d "$DEST" ]]; then
            rm -rf "$DEST"
            echo "Removed $DEST"
        else
            echo "Nothing installed at $DEST"
        fi
        echo
        echo "Settings are kept, in Besiege_Data/Mods/Data/. Delete that folder"
        echo "too if you want the per-player volumes gone."
        exit 0
        ;;
    --copy)
        # Replace whatever is there, whichever kind it is.
        [[ -L "$DEST" ]] && rm "$DEST"
        [[ -d "$DEST" ]] && rm -rf "$DEST"
        cp -r "$SRC" "$DEST"
        echo "Copied mod to $DEST"
        echo
        echo "Note: the game writes the generated <ID> into the copy's Mod.xml,"
        echo "not into your working copy. Install with a symlink if you want that"
        echo "ID where you can commit it."
        ;;
    "")
        [[ -L "$DEST" ]] && rm "$DEST"
        [[ -d "$DEST" ]] && rm -rf "$DEST"
        ln -s "$SRC" "$DEST"
        echo "Linked $DEST -> $SRC"
        ;;
    *)
        echo "Unknown option: $1" >&2
        echo "Usage: $0 [--copy | --uninstall]" >&2
        exit 1
        ;;
esac

if pgrep -x Besiege >/dev/null 2>&1 || pgrep -f 'Besiege\.x86' >/dev/null 2>&1; then
    echo
    echo "Besiege is running; restart it to pick this up."
fi

cat <<'EOF'

Done. Next:
  1. Start Besiege and enable "Multiplayer Text to Speech" in the mods menu.
  2. Enter a level. The mod loads then, not at startup.
  3. `tts say hello world` in the console tests the voice without needing
     anyone else online. Open the console with ` and use `show_logs true`
     to see the mod's own logging.
  4. Join or host a multiplayer game and open the chat. A gear appears just
     to the left of the chat window; it opens the volume options, including
     a slider per player.
  5. `tts status` says whether the chat hook has actually seen a message,
     which is the thing to check first if speech is silent.

Note: the game writes the generated mod ID into Mod.xml the first time it loads
the mod. With a symlink that write lands in your working copy, which is what you
want -- <ID> is meant to stay stable for the life of the mod, so commit it.
EOF
