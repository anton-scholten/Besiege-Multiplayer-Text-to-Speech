#!/usr/bin/env python3
"""Validates Mod.xml before the game gets a chance to reject it silently.

A malformed manifest produces no error in game: the mod simply never appears
in the list, which is indistinguishable from not having installed it. So the
parse and the existence of every file it names are checked here instead.
"""
import os
import sys
import xml.etree.ElementTree as ET

# Elements the loader refuses a manifest without. It reports the first one it
# finds as
#
#   [Mods] ModInfo (at line 1, column 2 in Mod.xml) must contain X element!
#   [Mods] There was an error loading the mod manifest: .../Mod.xml
#   [Mods] Not loading <ModName>
#
# and the mod then simply does not appear in the list.
#
# This list is not guessed from what other mods happen to ship -- doing that
# wrongly makes Debug look mandatory, because every shipped mod has one.
# InternalModding.Common.Serialization.Validate builds the required set as every
# [XmlElement] member of InternalModding.Mods.ModInfo *without* a [DefaultValue]
# attribute, and reading those attributes out of Assembly-CSharp with Cecil
# gives exactly these five. Re-derive it the same way against a newer Besiege.
REQUIRED = ("Name", "Author", "Version", "Description", "MultiplayerCompatible")

# [XmlElement] members that do carry a [DefaultValue], so the loader is happy
# without them. Listed to record that their absence was checked, not assumed.
OPTIONAL = ("Debug", "Icon", "WorkshopThumbnail", "LoadOrder",
            "LoadInTitleScreen", "Resources", "ID")


def main(path):
    folder = os.path.dirname(os.path.abspath(path))
    problems = []

    if not os.path.isfile(path):
        print("   manifest: %s does not exist" % path, file=sys.stderr)
        return 1

    # An XML comment may not contain two hyphens in a row, which prose written
    # with a dash produces easily -- and which the game reports as nothing.
    try:
        tree = ET.parse(path)
    except ET.ParseError as e:
        print("   manifest: will not parse: %s" % e, file=sys.stderr)
        return 1

    root = tree.getroot()

    for field in REQUIRED:
        node = root.find(field)
        if node is None:
            problems.append("<%s> is missing -- the loader refuses the manifest "
                            "with \"must contain %s element!\" and the mod never "
                            "appears" % (field, field))
        elif not (node.text or "").strip():
            problems.append("<%s> is empty" % field)

    # <ID> is written by the game on first load. It must not be hand-authored
    # before that, and must never change afterwards.
    ident = root.find("ID")
    if ident is None or not (ident.text or "").strip():
        print("   manifest: no <ID> yet -- the game writes one on first load.")
        print("             Commit it once it appears; saved machines use it.")

    # Not a loader requirement -- a blocks-only mod needs no assembly -- but
    # this mod is nothing without its code, so an empty <Assemblies> would
    # install a mod that loads and does nothing at all.
    if not root.findall(".//Assembly"):
        problems.append("no <Assembly> is declared, so none of the mod's code "
                        "would run")

    # The two bases are different, and getting them the wrong way round is
    # silent: <Assembly> and <Block> are relative to the mod root, while
    # everything inside <Resources> is relative to the mod's Resources/
    # folder, because ModPaths.GetFilePath appends "Resources" for those.
    # A <Texture path="Resources/icon.png"> therefore resolves to
    # Resources/Resources/icon.png, never loads, and takes
    # ModResource.AllResourcesLoaded down with it for the whole mod.
    ROOT_RELATIVE = ("Assembly", "Block", "Entity")
    RESOURCE_RELATIVE = ("Texture", "Mesh", "AudioClip", "AssetBundle")

    for tag in ROOT_RELATIVE + RESOURCE_RELATIVE:
        base = folder
        if tag in RESOURCE_RELATIVE:
            base = os.path.join(folder, "Resources")

        for node in root.iter(tag):
            rel = node.get("path")
            if not rel:
                problems.append("<%s> has no path attribute" % tag)
                continue

            if tag in RESOURCE_RELATIVE and rel.replace("\\", "/").startswith("Resources/"):
                problems.append(
                    "<%s path=\"%s\"> is relative to the mod's Resources/ folder "
                    "already, so this resolves to Resources/%s and will never load"
                    % (tag, rel, rel))
                continue

            full = os.path.join(base, rel)
            if not os.path.isfile(full):
                problems.append("<%s path=\"%s\"> does not exist (looked in %s)"
                                % (tag, rel, os.path.relpath(base, folder)))
            else:
                # Resource loading is case-sensitive on Linux, and a manifest
                # authored on Windows regularly gets this wrong.
                real = os.path.basename(os.path.realpath(full))
                if real != os.path.basename(rel):
                    problems.append(
                        "<%s path=\"%s\"> differs in case from %s on disk"
                        % (tag, rel, real))

    if problems:
        print("   manifest: %d problem(s)" % len(problems), file=sys.stderr)
        for p in problems:
            print("     " + p, file=sys.stderr)
        return 1

    print("   manifest: ok")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else "Mod.xml"))
