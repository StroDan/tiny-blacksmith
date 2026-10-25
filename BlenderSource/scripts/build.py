# Tiny Blacksmith environment assets - headless build.
#   blender -b --factory-startup --python BlenderSource/scripts/build.py -- <category> [<category> ...] [--preview DIR]
# categories: town cottage pack blacksmith nature props landmarks (or "all")
# Per category: BlenderSource/<Folder>/<category>.blend (one collection per asset, LOD0 + LOD1), one FBX per asset in
# Assets/TinyBlacksmith/Environment/Models/<sub>/, metadata assets_<category>.json, and (with --preview) a workbench
# render of the whole set from the reference sheets' three-quarter angle.
import sys, os, importlib
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import tb_lib as L

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
preview_dir = None
if "--preview" in argv:
    i = argv.index("--preview")
    preview_dir = argv[i + 1]
    argv = argv[:i] + argv[i + 2:]
only = None
if "--only" in argv:
    i = argv.index("--only")
    only = set(argv[i + 1].split(","))
    argv = argv[:i] + argv[i + 2:]
cats = argv or ["all"]
if "all" in cats:
    cats = ["town", "cottage", "pack", "blacksmith", "nature", "props", "landmarks"]

bpy.context.preferences.filepaths.save_version = 0   # no .blend1 backups next to the sources
L.write_palette(os.path.join(L.ENV_ASSETS, "Materials", "palette.json"))


def run_category(cat):
    L.reset_scene()
    reg = L.Registry(cat)
    root = L.collection(cat)
    shown = []
    if cat in ("town", "cottage", "pack", "blacksmith"):
        mod, attr, sub, blend_folder = {
            "town": ("buildings_town", "TOWN", "Buildings/Residential/Town", "Buildings/Residential"),
            "cottage": ("buildings_cottage", "COTTAGE", "Buildings/Residential/Cottage", "Buildings/Residential"),
            "pack": ("buildings_pack", "PACK", "Buildings/Commercial", "Buildings/Commercial"),
            "blacksmith": ("buildings_blacksmith", "SMITH", "Buildings/Blacksmith", "Buildings/Blacksmith")}[cat]
        items = getattr(importlib.import_module(mod), attr)
        for entry in items:
            name, fn = entry[0], entry[1]
            if only and name not in only:
                continue
            objs = L.make_asset(reg, root, name, "building", sub, fn, lod1=True)
            shown.append(objs[0])
    elif cat == "nature":
        import nature
        importlib.reload(nature)
        blend_folder = "Nature"
        for name, fn, kind in nature.NATURE:
            if only and name not in only:
                continue
            objs = L.make_asset(reg, root, name, kind, "Nature", fn, lod1=False)
            shown.append(objs[0])
    elif cat == "landmarks":
        import buildings_landmarks
        importlib.reload(buildings_landmarks)
        blend_folder = "Buildings/Landmarks"
        for name, fn, kind in buildings_landmarks.LANDMARKS:
            if only and name not in only:
                continue
            objs = L.make_asset(reg, root, name, kind, "Buildings/Landmarks", fn, lod1=True)
            shown.append(objs[0])
    elif cat == "props":
        import props_assets
        importlib.reload(props_assets)
        blend_folder = "Props"
        for name, fn in props_assets.PROPS:
            if only and name not in only:
                continue
            objs = L.make_asset(reg, root, name, "prop", "Props", fn, lod1=False)
            shown.append(objs[0])
    if not only:
        reg.save()
    out = os.path.join(L.BLEND_OUT, blend_folder)
    os.makedirs(out, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out, "%s.blend" % cat))
    if preview_dir and shown:
        os.makedirs(preview_dir, exist_ok=True)
        per = 4 if cat not in ("nature", "props") else 10
        for i in range(0, len(shown), per):
            L.preview(shown[i:i + per], os.path.join(preview_dir, "%s_%d.png" % (cat, i // per)))


for c in cats:
    run_category(c)
print("[TB] done", cats)
