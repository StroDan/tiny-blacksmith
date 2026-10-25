# Tiny Blacksmith - core low-poly construction kit for Blender (5.x, headless).
#
# Every asset is built by code into ONE bmesh with one material slot per shared palette colour, so a building is a
# single mesh / single renderer in Unity with a handful of submeshes that all point at the shared material library.
#
# Conventions (Blender space, metres):
#   - Z up, the FRONT of a building faces -Y (Blender's front view), +X is the viewer's right.
#   - Origin = centre of the footprint on the ground.
#   - A "wall frame" (see tb_arch.Face) is right-handed with x along the wall (left -> right seen from outside),
#     z up and y pointing INTO the wall - so a feature authored facing -Y can be put on any wall.
# The Unity side turns the front to +X (VillageBuilder's convention) in the prefab.

import bpy, bmesh, math, random, json, os, zlib
from contextlib import contextmanager
from mathutils import Vector, Matrix, Euler

# ---- the shared palette (sRGB colour, smoothness, emission multiplier) -------------------------------------------
# Unity builds one URP/Lit material per entry from palette.json (colour = sRGB like every other LowPoly paint;
# emission colour = colour * emission). Window glass keeps DayCycle's window signature: r = 1, g in 0.72..0.8,
# b = g - 0.35 - that is what makes each house's windows light up on their own evening schedule.
PALETTE = {
    "TB_PlasterCream":   ((0.94, 0.88, 0.74), 0.05, 0),
    "TB_PlasterBeige":   ((0.87, 0.78, 0.62), 0.05, 0),
    "TB_WoodLight":      ((0.68, 0.47, 0.27), 0.08, 0),
    "TB_WoodMedium":     ((0.52, 0.33, 0.18), 0.08, 0),
    "TB_WoodDark":       ((0.35, 0.22, 0.12), 0.08, 0),
    "TB_WoodDarker":     ((0.22, 0.14, 0.08), 0.05, 0),
    "TB_StoneLight":     ((0.72, 0.71, 0.68), 0.06, 0),
    "TB_StoneMedium":    ((0.57, 0.56, 0.54), 0.06, 0),
    "TB_StoneDark":      ((0.42, 0.41, 0.40), 0.06, 0),
    "TB_CastleStoneLight": ((0.90, 0.84, 0.70), 0.06, 0),
    "TB_CastleStone":    ((0.81, 0.73, 0.59), 0.06, 0),
    "TB_CastleStoneDark": ((0.63, 0.56, 0.46), 0.06, 0),
    "TB_RoofRed":        ((0.82, 0.38, 0.21), 0.10, 0),
    "TB_RoofRedDark":    ((0.68, 0.28, 0.16), 0.10, 0),
    "TB_RoofBlue":       ((0.31, 0.42, 0.64), 0.12, 0),
    "TB_RoofBlueDark":   ((0.23, 0.32, 0.51), 0.12, 0),
    "TB_RoofThatch":     ((0.94, 0.74, 0.30), 0.04, 0),
    "TB_RoofThatchDark": ((0.80, 0.58, 0.21), 0.04, 0),
    "TB_RoofGreen":      ((0.47, 0.56, 0.23), 0.08, 0),
    "TB_RoofGreenDark":  ((0.36, 0.45, 0.17), 0.08, 0),
    "TB_RoofSlate":      ((0.34, 0.36, 0.44), 0.12, 0),
    "TB_RoofSlateDark":  ((0.25, 0.27, 0.34), 0.12, 0),
    "TB_MetalDark":      ((0.19, 0.19, 0.21), 0.40, 0),
    "TB_MetalLight":     ((0.62, 0.64, 0.68), 0.55, 0),
    "TB_WindowA":        ((1.00, 0.76, 0.41), 0.20, 0.55),
    "TB_WindowB":        ((1.00, 0.75, 0.40), 0.20, 0.55),
    "TB_LanternGlow":    ((1.00, 0.72, 0.30), 0.00, 1.5),
    "TB_Fire":           ((1.00, 0.52, 0.14), 0.00, 2.2),
    "TB_Coal":           ((0.10, 0.10, 0.11), 0.15, 0),
    "TB_Glass":          ((0.55, 0.68, 0.72), 0.80, 0),
    "TB_Leaf":           ((0.37, 0.56, 0.21), 0.08, 0),
    "TB_LeafLight":      ((0.50, 0.68, 0.25), 0.08, 0),
    "TB_LeafDark":       ((0.25, 0.42, 0.16), 0.08, 0),
    "TB_LeafYellow":     ((0.78, 0.76, 0.22), 0.08, 0),
    "TB_LeafOrange":     ((0.90, 0.50, 0.15), 0.08, 0),
    "TB_LeafRed":        ((0.76, 0.32, 0.11), 0.08, 0),
    "TB_Pine":           ((0.19, 0.37, 0.22), 0.08, 0),
    "TB_PineLight":      ((0.26, 0.46, 0.27), 0.08, 0),
    "TB_Bark":           ((0.42, 0.27, 0.15), 0.06, 0),
    "TB_BirchBark":      ((0.90, 0.88, 0.82), 0.06, 0),
    "TB_Grass":          ((0.43, 0.63, 0.22), 0.05, 0),
    "TB_GrassDry":       ((0.80, 0.66, 0.30), 0.05, 0),
    "TB_Wheat":          ((0.92, 0.74, 0.31), 0.05, 0),
    "TB_Moss":           ((0.46, 0.60, 0.21), 0.05, 0),
    "TB_FlowerWhite":    ((0.97, 0.95, 0.90), 0.10, 0),
    "TB_FlowerRed":      ((0.88, 0.22, 0.17), 0.10, 0),
    "TB_FlowerYellow":   ((0.99, 0.80, 0.18), 0.10, 0),
    "TB_FlowerPurple":   ((0.63, 0.43, 0.82), 0.10, 0),
    "TB_FlowerBlue":     ((0.36, 0.47, 0.86), 0.10, 0),
    "TB_FlowerPink":     ((0.94, 0.56, 0.66), 0.10, 0),
    "TB_ClothCream":     ((0.93, 0.89, 0.77), 0.05, 0),
    "TB_ClothRed":       ((0.73, 0.22, 0.18), 0.05, 0),
    "TB_ClothBlue":      ((0.29, 0.42, 0.68), 0.05, 0),
    "TB_Sack":           ((0.80, 0.68, 0.48), 0.04, 0),
    "TB_Rope":           ((0.64, 0.53, 0.36), 0.04, 0),
    "TB_Water":          ((0.31, 0.57, 0.77), 0.85, 0),
    "TB_WaterFoam":      ((0.86, 0.93, 0.97), 0.60, 0),
    "TB_Gold":           ((0.86, 0.69, 0.26), 0.55, 0),
    "TB_Clay":           ((0.74, 0.37, 0.22), 0.15, 0),
    "TB_ClayLight":      ((0.86, 0.68, 0.46), 0.15, 0),
    "TB_GlassGreen":     ((0.24, 0.44, 0.25), 0.70, 0),
    "TB_GlassBrown":     ((0.52, 0.31, 0.15), 0.70, 0),
    "TB_MushroomRed":    ((0.84, 0.17, 0.12), 0.15, 0),
    "TB_MushroomCap":    ((0.64, 0.42, 0.23), 0.10, 0),
    "TB_Chanterelle":    ((0.96, 0.67, 0.17), 0.10, 0),
    "TB_Dirt":           ((0.55, 0.42, 0.28), 0.03, 0),
}


def srgb_to_linear(c):
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def material(name):
    """Get or create the Blender material for a palette entry (viewport + Principled preview only - Unity uses the
    shared library built from palette.json, remapped by name)."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    col, smooth, emit = PALETTE[name]
    m = bpy.data.materials.new(name)
    lin = [srgb_to_linear(c) for c in col]
    m.diffuse_color = (*lin, 1.0)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf is not None:
        bsdf.inputs["Base Color"].default_value = (*lin, 1.0)
        bsdf.inputs["Roughness"].default_value = 1.0 - smooth
        if emit > 0 and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (*lin, 1.0)
            bsdf.inputs["Emission Strength"].default_value = emit
    return m


def write_palette(path):
    data = [{"name": n, "color": list(c), "smoothness": s, "emission": e} for n, (c, s, e) in PALETTE.items()]
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w") as f:
        json.dump({"materials": data}, f, indent=1)


# ---- the mesh kit --------------------------------------------------------------------------------------------------

_CUBE_FACES = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (2, 3, 7, 6), (3, 0, 4, 7), (1, 2, 6, 5)]
# face order: bottom, top, front(-y), back(+y), left(-x), right(+x)


def V(x, y=None, z=None):
    if y is None:
        return Vector(x)
    return Vector((x, y, z))


def rot(rx=0.0, ry=0.0, rz=0.0):
    """Rotation matrix from degrees (XYZ order)."""
    return Euler((math.radians(rx), math.radians(ry), math.radians(rz)), 'XYZ').to_matrix().to_4x4()


class Kit:
    """Accumulates flat-shaded geometry with material indices under a transform stack."""

    def __init__(self, name, seed=1, detail=True):
        self.name = name
        self.bm = bmesh.new()
        self.mats = []
        self.stack = [Matrix.Identity(4)]
        self.rng = random.Random(seed)
        self.detail = detail          # False = the cheap LOD1 version: builders skip small stuff
        self.meta = {}                # anything the Unity side needs (doors, chimney tops, lantern spots ...)

    # -- transforms --
    @contextmanager
    def at(self, pos=(0, 0, 0), rz=0.0, rx=0.0, ry=0.0, scale=None):
        M = Matrix.Translation(V(pos)) @ rot(rx, ry, rz)
        if scale is not None:
            s = scale if isinstance(scale, (tuple, list)) else (scale, scale, scale)
            M = M @ Matrix.Diagonal((s[0], s[1], s[2], 1.0))
        self.stack.append(self.stack[-1] @ M)
        try:
            yield
        finally:
            self.stack.pop()

    @contextmanager
    def frame(self, M):
        self.stack.append(self.stack[-1] @ M)
        try:
            yield
        finally:
            self.stack.pop()

    def world(self, p):
        return self.stack[-1] @ V(p)

    def mi(self, mat):
        if mat not in self.mats:
            self.mats.append(mat)
        return self.mats.index(mat)

    def r(self, a, b):
        return a + (b - a) * self.rng.random()

    def pick(self, seq):
        return seq[self.rng.randrange(len(seq))]

    # -- raw geometry --
    def poly(self, pts, faces, mat, M=None):
        T = self.stack[-1] if M is None else self.stack[-1] @ M
        flip = T.to_3x3().determinant() < 0
        vs = [self.bm.verts.new(T @ V(p)) for p in pts]
        idx = self.mi(mat)
        for f in faces:
            ids = list(reversed(f)) if flip else list(f)
            try:
                face = self.bm.faces.new([vs[i] for i in ids])
                face.material_index = idx
            except ValueError:
                pass  # degenerate / duplicate face
        return vs

    def hexa(self, p8, mat, skip=()):
        """8 corners in cube order (see _CUBE_FACES). skip = face indices to leave out (hidden faces)."""
        faces = [f for i, f in enumerate(_CUBE_FACES) if i not in skip]
        self.poly(p8, faces, mat)

    def box(self, c, s, mat, r=None, skip=()):
        """Axis box centred on c with size s, optional rotation (rx, ry, rz) degrees about its centre."""
        hx, hy, hz = s[0] * 0.5, s[1] * 0.5, s[2] * 0.5
        pts = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
               (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
        M = Matrix.Translation(V(c))
        if r is not None:
            M = M @ rot(*r)
        faces = [f for i, f in enumerate(_CUBE_FACES) if i not in skip]
        self.poly(pts, faces, mat, M)

    def span(self, a, b, w, h, mat, up=(0, 0, 1)):
        """A beam of cross-section w (horizontal) x h from point a to point b."""
        a, b = V(a), V(b)
        d = b - a
        L = d.length
        if L < 1e-5:
            return
        f = d / L
        u = V(up)
        side = f.cross(u)
        if side.length < 1e-4:
            side = f.cross(V(1, 0, 0))
        side.normalize()
        upv = side.cross(f).normalized()
        M = Matrix((
            (side.x, f.x, upv.x, 0), (side.y, f.y, upv.y, 0), (side.z, f.z, upv.z, 0), (0, 0, 0, 1)))
        M = Matrix.Translation((a + b) * 0.5) @ M
        hx, hy, hz = w * 0.5, L * 0.5, h * 0.5
        pts = [(-hx, -hy, -hz), (hx, -hy, -hz), (hx, hy, -hz), (-hx, hy, -hz),
               (-hx, -hy, hz), (hx, -hy, hz), (hx, hy, hz), (-hx, hy, hz)]
        self.poly(pts, _CUBE_FACES, mat, M)

    def frustum(self, c, w0, d0, w1, d1, h, mat, r=None, dx=0.0, dy=0.0):
        """Rectangular frustum: bottom w0 x d0, top w1 x d1 (top offset by dx, dy), height h, centred on c."""
        pts = [(-w0 / 2, -d0 / 2, -h / 2), (w0 / 2, -d0 / 2, -h / 2), (w0 / 2, d0 / 2, -h / 2), (-w0 / 2, d0 / 2, -h / 2),
               (-w1 / 2 + dx, -d1 / 2 + dy, h / 2), (w1 / 2 + dx, -d1 / 2 + dy, h / 2),
               (w1 / 2 + dx, d1 / 2 + dy, h / 2), (-w1 / 2 + dx, d1 / 2 + dy, h / 2)]
        M = Matrix.Translation(V(c))
        if r is not None:
            M = M @ rot(*r)
        self.poly(pts, _CUBE_FACES, mat, M)

    def prism(self, c, n, r0, r1, h, mat, r=None, phase=0.0, caps=True, sx=1.0, sy=1.0):
        """n-sided frustum along local Z, centred on c. r1 = 0 makes a cone."""
        M = Matrix.Translation(V(c))
        if r is not None:
            M = M @ rot(*r)
        pts = []
        for i in range(n):
            a = 2 * math.pi * i / n + math.radians(phase)
            pts.append((math.cos(a) * r0 * sx, math.sin(a) * r0 * sy, -h / 2))
        cone = r1 <= 1e-5
        if cone:
            pts.append((0, 0, h / 2))
        else:
            for i in range(n):
                a = 2 * math.pi * i / n + math.radians(phase)
                pts.append((math.cos(a) * r1 * sx, math.sin(a) * r1 * sy, h / 2))
        faces = []
        for i in range(n):
            j = (i + 1) % n
            if cone:
                faces.append((i, j, n))
            else:
                faces.append((i, j, n + j, n + i))
        if caps:
            faces.append(tuple(reversed(range(n))))
            if not cone:
                faces.append(tuple(range(n, 2 * n)))
        self.poly(pts, faces, mat, M)

    def wedge(self, c, w, d, h, mat, r=None, top_w=0.0):
        """Triangular prism: base w (x) by d (y) on the bottom, ridge along y at height h (top_w > 0 = flat top)."""
        tw = top_w / 2
        if tw <= 0:
            pts = [(-w / 2, -d / 2, 0), (w / 2, -d / 2, 0), (0, -d / 2, h), (-w / 2, d / 2, 0), (w / 2, d / 2, 0), (0, d / 2, h)]
            faces = [(0, 1, 2), (4, 3, 5), (0, 3, 4, 1), (3, 0, 2, 5), (1, 4, 5, 2)]
        else:
            pts = [(-w / 2, -d / 2, 0), (w / 2, -d / 2, 0), (w / 2, d / 2, 0), (-w / 2, d / 2, 0),
                   (-tw, -d / 2, h), (tw, -d / 2, h), (tw, d / 2, h), (-tw, d / 2, h)]
            faces = _CUBE_FACES
        M = Matrix.Translation(V(c))
        if r is not None:
            M = M @ rot(*r)
        self.poly(pts, faces, mat, M)

    def blob(self, c, radius, mat, sub=1, squash=1.0, jitter=0.12, sx=1.0, sy=1.0):
        """A faceted low-poly ball (icosphere) with a little random wobble - foliage, rocks, sacks."""
        tmp = bmesh.new()
        bmesh.ops.create_icosphere(tmp, subdivisions=sub, radius=1.0)
        pts, faces = [], []
        index = {}
        for i, v in enumerate(tmp.verts):
            index[v] = i
            j = 1.0 + (self.rng.random() - 0.5) * 2 * jitter
            pts.append((v.co.x * radius * j * sx, v.co.y * radius * j * sy, v.co.z * radius * j * squash))
        for f in tmp.faces:
            faces.append(tuple(index[v] for v in f.verts))
        tmp.free()
        self.poly(pts, faces, mat, Matrix.Translation(V(c)))

    def extrude(self, outline, depth, mat, c=(0, 0, 0), r=None):
        """A flat polygon (list of (x, z) points, counter-clockwise seen from -Y) extruded along +Y by depth."""
        n = len(outline)
        pts = [(x, 0, z) for x, z in outline] + [(x, depth, z) for x, z in outline]
        faces = [tuple(range(n)), tuple(reversed(range(n, 2 * n)))]
        # front cap: seen from -Y, CCW order in (x, z) is already outward (-Y)
        for i in range(n):
            j = (i + 1) % n
            faces.append((j, i, n + i, n + j))
        M = Matrix.Translation(V(c))
        if r is not None:
            M = M @ rot(*r)
        self.poly(pts, faces, mat, M)

    # -- output --
    def build(self, obj_name, collection):
        me = bpy.data.meshes.new(obj_name)
        bmesh.ops.remove_doubles(self.bm, verts=self.bm.verts, dist=0.0001)
        self.bm.to_mesh(me)
        self.bm.free()
        for m in self.mats:
            me.materials.append(material(m))
        for p in me.polygons:
            p.use_smooth = False
        ob = bpy.data.objects.new(obj_name, me)
        collection.objects.link(ob)
        return ob


# ---- scene / export helpers ----------------------------------------------------------------------------------------

def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def collection(name, parent=None):
    c = bpy.data.collections.new(name)
    (parent or bpy.context.scene.collection).children.link(c)
    return c


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons)


def export_fbx(objects, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={'MESH', 'EMPTY'},
        axis_forward='-Z', axis_up='Y', apply_unit_scale=True, apply_scale_options='FBX_SCALE_UNITS',
        bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
        add_leaf_bones=False, bake_anim=False, use_custom_props=False, path_mode='STRIP')


PROJECT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ENV_ASSETS = os.path.join(PROJECT, "Assets", "TinyBlacksmith", "Environment")
BLEND_OUT = os.path.join(PROJECT, "BlenderSource")


class Registry:
    """Collects the per-asset metadata the Unity prefab builder reads (Models/<category>/assets_<category>.json)."""

    def __init__(self, category):
        self.category = category
        self.items = []

    def add(self, name, kind, sub, ob, meta):
        bb = [ob.matrix_world @ V(c) for c in ob.bound_box]
        mn = V(min(p.x for p in bb), min(p.y for p in bb), min(p.z for p in bb))
        mx = V(max(p.x for p in bb), max(p.y for p in bb), max(p.z for p in bb))
        item = {"name": name, "kind": kind, "folder": sub, "min": list(mn), "max": list(mx), "tris": tri_count(ob)}
        for key, val in meta.items():
            if isinstance(val, list) and val and isinstance(val[0], (list, tuple)):
                val = [c for p in val for c in p]      # Unity JsonUtility reads flat float arrays only
            item[key] = val
        self.items.append(item)

    def save(self):
        path = os.path.join(ENV_ASSETS, "Models", "assets_%s.json" % self.category)
        os.makedirs(os.path.dirname(path), exist_ok=True)
        with open(path, "w") as f:
            json.dump({"assets": self.items}, f, indent=1)
        print("[TB] wrote", path, len(self.items), "assets")


def make_asset(reg, coll, name, kind, sub, build, lod1=True, extra_objects=None, blend_coll=None):
    """Builds LOD0 (+ LOD1 when lod1) with build(kit), exports <sub>/<name>.fbx and records metadata.
    build(kit) may return a list of extra objects (e.g. a water wheel) to export with the model."""
    c = collection(name, coll)
    k0 = Kit(name, seed=zlib.crc32(name.encode()) & 0xffff, detail=True)
    extras = build(k0) or []
    ob0 = k0.build(name + ("_LOD0" if lod1 else ""), c)
    objs = [ob0]
    if lod1:
        k1 = Kit(name, seed=zlib.crc32(name.encode()) & 0xffff, detail=False)
        build(k1)
        ob1 = k1.build(name + "_LOD1", c)
        objs.append(ob1)
        k0.meta["lod1_tris"] = tri_count(ob1)
    for e in extras:
        if e.name not in c.objects:
            for uc in e.users_collection:
                uc.objects.unlink(e)
            c.objects.link(e)
        objs.append(e)
    export_fbx(objs, os.path.join(ENV_ASSETS, "Models", sub, name + ".fbx"))
    reg.add(name, kind, sub, ob0, k0.meta)
    print("[TB] %-28s %6d tris%s" % (name, tri_count(ob0), ("  (LOD1 %d)" % k0.meta["lod1_tris"]) if lod1 else ""))
    return objs


# ---- previews (visual QA against the reference sheets) -------------------------------------------------------------

def preview(objects, path, yaw=-35.0, pitch=24.0, res=(1600, 900), spacing=None, label=None, layout=True):
    """Workbench render of the given objects side by side (moved along X for the shot, then put back), from a
    three-quarter view like the reference sheets. Front-left view = camera yaw -35 (front faces -Y)."""
    scene = bpy.context.scene
    hidden = [o for o in scene.objects if o not in objects and not o.hide_render]
    for o in hidden:
        o.hide_render = True
    saved = [o.location.copy() for o in objects]
    x = 0.0
    boxes = []
    for o in (objects if layout else []):
        bb = [o.matrix_world @ V(c) for c in o.bound_box]
        w = max(p.x for p in bb) - min(p.x for p in bb)
        d = max(p.y for p in bb) - min(p.y for p in bb)
        size = max(w, d)
        o.location.x += x + size / 2 - (min(p.x for p in bb) + max(p.x for p in bb)) / 2 + o.location.x * 0
        x += size + (spacing if spacing is not None else size * 0.25)
    bpy.context.view_layer.update()
    pts = []
    for o in objects:
        pts += [o.matrix_world @ V(c) for c in o.bound_box]
    mn = V(min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts))
    mx = V(max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts))
    centre = (mn + mx) / 2
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam_data.type = 'ORTHO'
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    scene.collection.objects.link(cam)
    direction = V(math.sin(math.radians(yaw)) * math.cos(math.radians(pitch)) * 1,
                  -math.cos(math.radians(yaw)) * math.cos(math.radians(pitch)),
                  math.sin(math.radians(pitch)))
    cam.location = centre + direction * 200
    cam.rotation_euler = (direction * -1).to_track_quat('-Z', 'Y').to_euler()
    bpy.context.view_layer.update()
    inv = cam.matrix_world.inverted()
    loc = [inv @ p for p in pts]
    w = max(p.x for p in loc) - min(p.x for p in loc)
    h = max(p.y for p in loc) - min(p.y for p in loc)
    cam_data.ortho_scale = max(w, h * res[0] / res[1]) * 1.06
    scene.camera = cam
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = False
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.film_transparent = False
    try:
        scene.world = scene.world or bpy.data.worlds.new("W")
        scene.world.color = (0.9, 0.89, 0.86)
    except Exception:
        pass
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam)
    for o, l in zip(objects, saved):
        o.location = l
    for o in hidden:
        o.hide_render = False
