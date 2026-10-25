# Turnaround renders of one asset for side-by-side QA with a reference sheet:
#   blender -b <file.blend> --python preview_views.py -- <AssetName> <out_prefix>
import sys, os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy
import tb_lib as L

name, prefix = sys.argv[sys.argv.index("--") + 1:][:2]
coll = bpy.data.collections[name]
objs = [o for o in coll.objects if not o.name.endswith("_LOD1")]
for label, yaw, pitch in (("hero", -35, 24), ("front", 0, 8), ("left", -90, 8), ("right", 90, 8), ("back", 180, 8)):
    L.preview(objs, "%s_%s.png" % (prefix, label), yaw=yaw, pitch=pitch, res=(1200, 800), layout=False)
