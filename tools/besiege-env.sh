# Locates the Besiege install and exports the paths the other scripts need.
# Sourced, not run. Set BESIEGE_DIR to override the search.

find_besiege() {
    if [[ -n "${BESIEGE_DIR:-}" ]]; then echo "$BESIEGE_DIR"; return; fi

    local candidates=("$HOME/.steam/steam/steamapps/common/Besiege"
                      "$HOME/.local/share/Steam/steamapps/common/Besiege"
                      "$HOME/Steam/steamapps/common/Besiege")

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
        [[ -f "$dir/Besiege_Data/Managed/mcs.dll" ]] && { echo "$dir"; return; }
    done
    return 1
}

BESIEGE="$(find_besiege)" || {
    echo "Besiege not found. Set BESIEGE_DIR to the install directory." >&2
    exit 1
}

DATA="$BESIEGE/Besiege_Data"
export BESIEGE DATA
export LIBMONO="$DATA/Mono/x86_64/libmono.so"
export MANAGED="$DATA/Managed"
export MONOETC="$DATA/Mono/etc"

# UI Factory 3 (Workshop item 2913469777) ships the prefabs the options panel
# is built from. It is a *soft* dependency at runtime -- the mod loads and
# reads chat aloud without it -- but its assemblies have to be present to
# compile against.
find_uifactory() {
    # An explicit override is checked and then obeyed or refused -- never
    # quietly replaced by a search hit. Falling back would build against a
    # different UI Factory than the one that was asked for, and say nothing.
    if [[ -n "${UIFACTORY_DIR:-}" ]]; then
        if [[ -f "$UIFACTORY_DIR/Besiege.UI.dll" ]]; then
            echo "$UIFACTORY_DIR"
            return
        fi
        echo "UIFACTORY_DIR is set to '$UIFACTORY_DIR', which has no" \
             "Besiege.UI.dll in it." >&2
        return 1
    fi

    local dir
    for dir in "$BESIEGE/../../workshop/content/346010/2913469777/UIFactory" \
               "$DATA/Mods/UIFactory"; do
        [[ -f "$dir/Besiege.UI.dll" ]] && { echo "$dir"; return; }
    done
    return 1
}

UIFACTORY="$(find_uifactory || true)"
export UIFACTORY
