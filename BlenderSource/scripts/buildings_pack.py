# Tiny Blacksmith - 03_medieval_building_pack.png: townhouse, cottage, workshop, tavern, warehouse, watermill.
# Front faces -Y. The watermill's wheel is a separate object (pivot on its axle) so Unity can turn it.
import math
import bpy
from tb_lib import Kit, V
import tb_arch as A
import tb_props as P

WL, WM, WD = A.WL, A.WM, A.WD
FL_W, FL_R = "TB_FlowerWhite", "TB_FlowerRed"
SH_ORANGE = "TB_WoodLight"


def _prop(k, fn, pos, rz=0.0, *a, **kw):
    if not k.detail:
        return
    with k.at(pos, rz=rz):
        fn(k, *a, **kw)


def _lantern(k, f, s, z):
    with k.frame(f.M(s, z)):
        A.wall_lantern(k)


def pack_townhouse(k):
    """03 TOWNHOUSE - four storeys, stone ground floor, jettied timber floors with orange shutters, two blue fleur
    banners, blue roof with a side gable and a dormer, blue porch roof over the outside stair."""
    W, D = 5.2, 6.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 2.0, dict(w=1.0, h=2.2, z=0.72, steps=4, main=True)),
        ("front", "win", 4.2, dict(zc=1.7, w=0.65)),
        ("left", "win", 3.0, dict(zc=1.7, w=0.7)),
        ("right", "win", 3.0, dict(zc=1.7, w=0.7)),
        ("back", "win", 2.6, dict(zc=1.7, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 3.0)
    z = 3.0
    jet = 0.0
    for i, (h, wins) in enumerate(((2.6, ((1.2, SH_ORANGE), (4.0, SH_ORANGE))), (2.5, ((1.2, SH_ORANGE), (4.0, None))), (2.3, ((2.6, SH_ORANGE),)))):
        A.floor_band(k, x0, x1, y0 - jet - 0.25, y1, z)
        if i == 0:
            A.joists(k, F["front"], z - 0.14)
        jet += 0.25
        feats = [("front", "win", s, dict(zc=h * 0.5, shutters=sh, box=(FL_W,) if (i == 0 and s > 3) else None)) for s, sh in wins]
        feats += [("left", "win", 3.0, dict(zc=h * 0.5, shutters=SH_ORANGE)), ("right", "win", 3.0, dict(zc=h * 0.5)),
                  ("back", "win", 2.6, dict(zc=h * 0.5))]
        G = A.storey(k, (x0, x1, y0 - jet, y1), z + 0.08, z + h, feats, flip=bool(i % 2))
        if i == 0:
            first = G
        z += h
    roof = A.gable_roof(k, x0, x1, y0 - jet, y1, z, 3.2, "y", "blue", gable_windows=((0.55, 0.7), None))
    A.gable_roof(k, x0 - 0.3, 0.0, -0.8, 1.6, z - 0.4, 2.2, "x", "blue", over=0.3, gable_windows=((0.5, 0.6), None))
    with k.at((1.3, 0.8, 0), rz=90):
        with k.at((0, 0, roof.z_at(1.3, 0.8) - 0.2)):
            A.dormer(k, 0.9, 0.9, 1.2, "blue")
    A.chimney(k, -1.0, 2.2, z + 1.0, z + 3.6)
    with k.frame(first["front"].M(1.9, 0.8)):
        A.wall_banner(k, 0.55, 1.6, "TB_ClothBlue", "fleur")
    with k.frame(first["right"].M(0.5, 2.0)):
        A.wall_banner(k, 0.5, 1.3, "TB_ClothBlue", "fleur")
    with k.frame(F["front"].M(2.0, 3.1)):
        A.tile_awning(k, 2.0, 1.5, 0.0, "blue", posts=True, drop=0.6)
    _lantern(k, F["front"], 0.9, 2.3)
    if k.detail:
        with k.frame(F["front"].M(2.0, 0)):
            A.steps(k, 1.5, 4, rise=0.18, run=0.3, rail=True, side=1)
        _prop(k, P.crate, (1.8, y0 - 0.6, 0), 8, 0.6)
        _prop(k, P.crate, (2.4, y0 - 0.5, 0), -12, 0.5)
        _prop(k, P.potted_plant, (-2.3, y0 - 0.45, 0), 0, 1.0)
        _prop(k, P.barrel, (x1 + 0.4, -1.0, 0), 0, 0.8, 0.28)
    k.meta.update(floors=4, style="town")


def pack_cottage(k):
    """03 COTTAGE - thick thatch sweeping low with a tall front cross gable and a small dormer, stone plinth,
    canopy over the door, fence, barrel."""
    W, D = 6.0, 5.4
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 2.7, [
        ("front", "door", 3.9, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.0, dict(zc=1.6, w=0.7)),
        ("left", "win", 2.7, dict(zc=1.6, w=0.7)),
        ("right", "win", 2.7, dict(zc=1.6, w=0.7)),
        ("back", "win", 3.0, dict(zc=1.6)),
    ], plinth=0.75)
    roof = A.gable_roof(k, x0, x1, y0, y1, 2.7, 3.6, "x", "thatch", style="thatch", over=0.6, gable_over=0.5,
                        gable_windows=((0.55, 0.7), None))
    A.gable_roof(k, 1.3 - 1.4, 1.3 + 1.4, y0 - 0.25, 0.8, 2.7, 3.9, "y", "thatch", style="thatch", over=0.55, gable_over=0.45,
                 gable_windows=((0.65, 0.85, None), None))
    # the cross gable's front wall (it stands proud of the main front)
    A.storey(k, (1.3 - 1.4, 1.3 + 1.4, y0 - 0.25, y0 + 0.2), 0.0, 2.7, [("front", "win", 0.9, dict(zc=1.6, w=0.7, box=(FL_W,)))],
             plinth=0.75, skip_faces=("back",))
    with k.at((-1.5, -1.2, roof.z_at(-1.5, -1.2) - 0.25)):
        A.dormer(k, 0.9, 0.8, 1.3, "thatch", style="thatch", rise=0.55)
    A.chimney(k, -0.9, 0.9, 4.2, 7.3)
    with k.frame(A.Face((-0.1, y0 - 0.25, 0), (1, 0, 0), 2.8).M(2.2, 2.6)):
        A.tile_awning(k, 1.3, 0.9, 0.0, (WL, WM), brackets=True, drop=0.4)
    _lantern(k, F["front"], 3.0, 2.2)
    with k.frame(F["front"].M(0.3, 0)):
        A.ivy(k, 0, 0.3, 2.4, 0.2)
    if k.detail:
        _prop(k, P.barrel, (-2.3, y0 - 0.7, 0), 0, 0.8, 0.28)
        _prop(k, P.fence, (x1 + 0.2, y0 - 1.0, 0), 0, 1.8, 0.85, 3)
        _prop(k, P.bush_small, (x0 - 0.3, y0 - 0.2, 0), 0, 0.9)
        _prop(k, P.topiary, (x0 - 0.5, 0.9, 0), 0, 1.4)
    k.meta.update(floors=1, style="cottage")


def pack_workshop(k):
    """03 WORKSHOP - red roof, a massive stone chimney on the left, open forge bay on the right under a red lean-to
    (glowing hearth, anvil, bench, sign with the anvil), shingled porch on the left, spoked wheel, crates."""
    W, D = 5.4, 5.6
    x0, x1, y0, y1 = -W / 2 - 0.6, W / 2 - 0.6, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 1.4, dict(w=1.0, main=True)),
        ("front", "win", 3.8, dict(zc=1.6, w=0.8)),
        ("left", "win", 2.8, dict(zc=1.7, w=0.7)),
        ("back", "win", 2.7, dict(zc=1.7)),
    ], kind="stone")
    A.floor_band(k, x0, x1, y0, y1, 3.0)
    G = A.storey(k, (x0, x1, y0, y1), 3.08, 5.2, [
        ("front", "win", 2.7, dict(zc=1.1, w=0.7)),
        ("left", "win", 2.8, dict(zc=1.1, w=0.7)),
        ("right", "win", 2.8, dict(zc=1.1, w=0.7)),
    ])
    roof = A.gable_roof(k, x0, x1, y0, y1, 5.2, 2.9, "y", "red", gable_windows=((0.6, 0.8), None))
    # the great chimney standing against the left gable wall
    cx = x0 - 0.45
    if k.detail:
        P.stone_stack(k, (cx, 0.0, 0), 1.3, 3.0, courses=5)
        P.stone_stack(k, (cx, 0.0, 3.0), 1.0, 3.0, courses=5)
        P.stone_stack(k, (cx, 0.0, 6.0), 0.8, 3.2, courses=5)
    else:
        k.box((cx, 0, 4.6), (1.0, 1.0, 9.2), A.SM)
    k.box((cx, 0, 9.26), (0.95, 0.95, 0.14), A.SD)
    k.meta.setdefault("chimneys", []).append([cx, 0.0, 9.5])
    # open forge bay to the right under a lean-to
    bx0, bx1 = x1, x1 + 2.6
    for (px, py) in ((bx1 - 0.1, y0 + 0.1), (bx1 - 0.1, y1 - 0.3)):
        k.box((px, py, 1.3), (0.16, 0.16, 2.6), WM)
        if k.detail:
            k.span((px, py, 1.9), (px - 0.5, py, 2.5), 0.1, 0.1, WM)
    k.box((bx1 - 0.1, 0, 2.55), (0.16, D, 0.16), WM)
    with k.at((bx0 - 0.02, 0, 0), rz=90):
        A.shed_roof(k, -D / 2 - 0.3, D / 2 + 0.3, 0.0, 2.9, 3.6, 2.6, "red", over=0.3, tile=0.6)
    k.box(((bx0 + bx1) / 2, 0, 0.05), (2.6, D, 0.1), A.SM)
    hx = (bx0 + bx1) / 2 + 0.2
    k.box((hx, 1.6, 0.5), (1.6, 1.3, 1.0), A.SM)
    k.box((hx, 1.1, 0.75), (0.9, 0.3, 0.5), "TB_Fire")
    k.box((hx, 1.6, 1.05), (1.4, 1.0, 0.12), "TB_Coal")
    k.frustum((hx, 1.7, 1.9), 1.5, 1.2, 0.5, 0.5, 1.3, A.IRON)
    k.box((hx, 1.7, 3.2), (0.45, 0.45, 1.4), A.IRON)
    k.meta.setdefault("glow", []).append([hx, 1.0, 1.0])
    _prop(k, P.anvil, (hx - 0.2, -0.8, 0), 90)
    _prop(k, P.barrel, (bx1 - 0.5, -2.1, 0), 0, 0.85, 0.32, True)
    _prop(k, P.crate, (bx0 + 0.4, -2.2, 0), 10, 0.55)
    with k.at((bx1 + 0.1, y0 + 0.6, 0), rz=-90):
        if k.detail:
            with k.at((0, 0, 0)):
                P.hanging_sign(k, "anvil", iron=False, board=0.6)
    # shingled porch on the left of the door
    with k.frame(F["front"].M(1.4, 2.8)):
        A.tile_awning(k, 2.0, 1.4, 0.0, (WL, WM), posts=True, drop=0.5)
    _lantern(k, F["front"], 2.35, 2.3)
    if k.detail:
        with k.at((x0 + 1.6, y0 - 1.0, 0.45), rz=10):
            P.wheel(k, 0.45)
        _prop(k, P.crate, (0.3, y0 - 0.6, 0), 5, 0.6)
        _prop(k, P.crate, (-0.25, y0 - 0.55, 0), -10, 0.45)
        _prop(k, P.bench, (x1 + 1.2, y1 - 0.9, 0), 0, 1.3)
        _prop(k, P.barrel, (x1 + 2.8, y1 - 0.5, 0), 0, 0.85, 0.3)
    k.meta.update(floors=2, style="workshop")


def pack_tavern(k):
    """03 TAVERN - broad two-and-a-half storeys, blue roof with three front gables, a long blue veranda on posts over
    tables and benches, hanging mug sign, bunting on a pole, lanterns, barrels."""
    W, D = 9.2, 6.4
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 4.6, dict(w=1.2, h=2.3, z=0.18, steps=1, main=True)),
        ("front", "win", 1.8, dict(zc=1.6, w=1.1)),
        ("front", "win", 7.4, dict(zc=1.6, w=1.1)),
        ("left", "win", 3.2, dict(zc=1.6)),
        ("right", "win", 3.2, dict(zc=1.6)),
        ("back", "win", 2.5, dict(zc=1.6)), ("back", "win", 6.5, dict(zc=1.6)),
    ], plinth=0.6)
    A.floor_band(k, x0, x1, y0 - 0.25, y1, 3.0)
    G = A.storey(k, (x0, x1, y0 - 0.25, y1), 3.08, 5.5, [
        ("front", "win", 1.3, dict(zc=1.25, shutters=SH_ORANGE)),
        ("front", "win", 3.6, dict(zc=1.25, shutters=SH_ORANGE)),
        ("front", "win", 5.6, dict(zc=1.25, shutters=SH_ORANGE)),
        ("front", "win", 7.9, dict(zc=1.25, shutters=SH_ORANGE)),
        ("left", "win", 3.2, dict(zc=1.25)),
        ("right", "win", 3.2, dict(zc=1.25)),
        ("back", "win", 2.5, dict(zc=1.25)), ("back", "win", 6.5, dict(zc=1.25)),
    ])
    roof = A.gable_roof(k, x0, x1, y0 - 0.25, y1, 5.5, 3.0, "x", "blue", gable_windows=((0.55, 0.7), (0.55, 0.7)))
    for gx, gw in ((-3.0, 2.4), (1.3, 2.2)):
        A.gable_roof(k, gx - gw / 2, gx + gw / 2, y0 - 0.25, 0.3, 5.5, 2.3, "y", "blue", over=0.3,
                     gable_windows=((0.5, 0.65), None))
        A.storey(k, (gx - gw / 2 + 0.1, gx + gw / 2 - 0.1, y0 - 0.25, y0), 5.5, 5.55, [], kind="plaster")
    A.chimney(k, -1.0, 1.3, 7.2, 9.6)
    A.chimney(k, 3.2, 1.0, 6.8, 9.2)
    # the veranda: blue tiles on posts all along the front
    vy = y0 - 2.0
    for x in (x0 + 0.2, -1.5, 1.5, x1 - 0.2):
        k.box((x, vy + 0.1, 1.3), (0.18, 0.18, 2.6), WM)
        if k.detail:
            for s in (-1, 1):
                k.span((x, vy + 0.1, 2.0), (x + s * 0.45, vy + 0.1, 2.5), 0.08, 0.08, WM)
    k.box((0, vy + 0.1, 2.55), (W, 0.18, 0.18), WM)
    with k.frame(F["front"].M(W / 2, 3.2)):
        A.shed_roof(k, -W / 2, W / 2, 0.0, 2.1, 0.0, -0.7, "blue", over=0.25, tile=0.6)
    k.box((0, (vy + y0) / 2, 0.06), (W - 0.2, y0 - vy, 0.12), WL)
    if k.detail:
        for tx in (-2.8, 2.8):
            with k.at((tx, vy + 0.9, 0.12)):
                k.box((0, 0, 0.72), (1.4, 0.8, 0.07), WL)
                for sx in (-0.55, 0.55):
                    k.box((sx, 0, 0.36), (0.08, 0.6, 0.72), WD)
                for sy in (-0.65, 0.65):
                    k.box((0, sy, 0.42), (1.3, 0.28, 0.06), WM)
                    k.box((-0.5, sy, 0.2), (0.07, 0.24, 0.4), WD)
                    k.box((0.5, sy, 0.2), (0.07, 0.24, 0.4), WD)
                with k.at((0.2, 0.1, 0.76)):
                    P.mug(k)
                with k.at((-0.3, -0.1, 0.76)):
                    P.jug(k, "TB_Clay", 0.7)
    # hanging sign with a mug on the left corner
    with k.at((x0 - 0.1, y0 - 0.2, 0.9), rz=-90):
        k.box((0, -0.5, 2.7), (0.1, 1.1, 0.1), WD)
        k.span((0, -0.05, 2.2), (0, -0.55, 2.68), 0.08, 0.08, WD)
        for s in (-1, 1):
            k.box((0, -0.75 + s * 0.28, 2.5), (0.02, 0.02, 0.3), A.IRON)
        k.box((0, -0.75, 2.0), (0.06, 0.85, 0.7), WM)
        k.box((0, -0.75, 2.0), (0.05, 0.93, 0.78), WD)
        with k.at((0, -0.75, 0), rz=90):
            P.emblem_mark(k, "mug", (0, -0.04, 2.0), 0.55)
            P.emblem_mark(k, "mug", (0, 0.04, 2.0), 0.55)
    # bunting on a pole at the right corner
    if k.detail:
        k.box((x1 + 0.7, y0 - 1.6, 1.6), (0.1, 0.1, 3.2), WD)
        n = 7
        for i in range(n):
            t = (i + 0.5) / n
            px = x1 + 0.7 - t * 3.6
            pz = 3.1 - math.sin(t * math.pi) * 0.35
            k.wedge((px, y0 - 1.6, pz - 0.28), 0.03, 0.34, 0.3, ("TB_ClothBlue", "TB_ClothRed", "TB_ClothCream")[i % 3], r=(180, 0, 90))
        k.span((x1 + 0.7, y0 - 1.6, 3.1), (x1 + 0.7 - 3.6, y0 - 1.6, 3.1), 0.015, 0.015, "TB_Rope")
    for x in (x0 + 0.2, x1 - 0.2):
        with k.frame(A.Face((x0, vy + 0.19, 0), (1, 0, 0), W).M(x - x0, 2.35)):
            A.wall_lantern(k, 0.3)
    _lantern(k, F["front"], 3.6, 2.4)
    if k.detail:
        _prop(k, P.barrel, (x0 - 0.3, y0 - 0.9, 0), 0, 0.9, 0.33)
        _prop(k, P.barrel, (x0 - 0.1, y0 - 1.6, 0), 0, 0.8, 0.3)
        _prop(k, P.barrel, (x1 + 0.3, y0 - 0.5, 0), 0, 0.9, 0.33)
        _prop(k, P.crate, (x0 + 0.9, vy - 0.4, 0), 10, 0.55)
        _prop(k, P.bucket, (0.9, vy + 0.5, 0.12), 0)
    k.meta.update(floors=2, style="tavern")


def pack_warehouse(k):
    """03 WAREHOUSE - stone ground floor with a big arched cart door, timber loft with a loading door and a hoist beam
    (pulley, rope, crate), dark slate roof, a canvas-roofed lean-to with a cart, a loading platform and stairs."""
    W, D = 7.0, 6.4
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.3, [
        ("front", "door", 3.5, dict(w=2.0, h=2.9, main=True)),
        ("front", "win", 1.1, dict(zc=1.9, w=0.7)),
        ("front", "win", 5.9, dict(zc=1.9, w=0.7)),
        ("left", "win", 3.2, dict(zc=1.9, w=0.7)),
        ("right", "win", 3.2, dict(zc=1.9, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 3.3)
    A.floor_band(k, x0, x1, y0, y1, 3.3)
    G = A.storey(k, (x0, x1, y0, y1), 3.38, 5.8, [
        ("front", "win", 1.4, dict(zc=1.2, w=0.7)),
        ("front", "door", 3.5, dict(w=1.3, h=1.9, arch=False, z=0.2)),
        ("front", "win", 5.6, dict(zc=1.2, w=0.7)),
        ("left", "win", 3.2, dict(zc=1.2)),
        ("right", "win", 3.2, dict(zc=1.2)),
    ])
    roof = A.gable_roof(k, x0, x1, y0, y1, 5.8, 3.4, "y", "slate", style="slate", tile=0.7,
                        gable_windows=((0.6, 0.7), None))
    # hoist beam out of the gable with a pulley, rope and a crate hanging
    k.box((0, y0 - 0.8, 7.2), (0.2, 2.2, 0.2), WD)
    if k.detail:
        k.span((0, y0 + 0.05, 6.3), (0, y0 - 0.9, 7.1), 0.12, 0.12, WD)
        k.prism((0, y0 - 1.6, 6.95), 10, 0.16, 0.16, 0.08, A.IRON, r=(0, 90, 0))
        k.box((0, y0 - 1.75, 5.5), (0.02, 0.02, 2.8), "TB_Rope")
        with k.at((0, y0 - 1.75, 3.55), rz=15):
            P.crate(k, 0.6)
    # lean-to on the left with a canvas roof over the cart
    lx = x0 - 2.4
    for py in (y0 + 0.4, y1 - 0.4):
        k.box((lx + 0.1, py, 1.2), (0.16, 0.16, 2.4), WM)
    with k.at((x0, 0, 0), rz=-90):
        A.cloth_awning(k, D - 0.4, 2.4, 3.0, ("TB_ClothCream",), poles=False, drop=0.65)
    _prop(k, P.cart, (lx + 1.3, -0.4, 0), 0)
    # loading platform and stairs on the right front
    px0 = x1 - 2.2
    k.box((px0 + 1.4, y0 - 0.8, 0.45), (2.4, 1.5, 0.12), WL)
    for (a, b) in ((px0 + 0.3, y0 - 1.45), (px0 + 2.5, y0 - 1.45), (px0 + 0.3, y0 - 0.15), (px0 + 2.5, y0 - 0.15)):
        k.box((a, b, 0.22), (0.14, 0.14, 0.45), WD)
    if k.detail:
        for i in range(3):
            k.box((px0 + 3.0 + i * 0.3, y0 - 0.8, 0.13 + i * 0.13), (0.3, 1.2, 0.07), WL)
        k.box((px0 + 2.55, y0 - 1.5, 0.95), (0.1, 0.1, 1.0), WD)
        k.box((px0 + 1.4, y0 - 1.5, 1.35), (2.3, 0.08, 0.08), WD)
    _lantern(k, F["front"], 2.1, 2.5)
    if k.detail:
        for (x, y, s) in ((px0 + 0.8, y0 - 0.75, 0.55), (px0 + 1.5, y0 - 0.9, 0.5)):
            _prop(k, P.crate, (x, y, 0.51), 12, s)
        _prop(k, P.barrel, (px0 + 2.1, y0 - 0.7, 0.51), 0, 0.8, 0.28)
        _prop(k, P.barrel, (1.3, y0 - 0.6, 0), 0, 0.9, 0.33)
        _prop(k, P.barrel, (-1.6, y0 - 0.55, 0), 0, 0.85, 0.3)
        _prop(k, P.sack, (-2.3, y0 - 0.6, 0), 0, 0.9)
    k.meta.update(floors=2, style="warehouse")


def _wheel_object(R=2.3, width=1.0):
    """The mill's water wheel as its own object, built round its axle (Blender X)."""
    w = Kit("Watermill_Wheel", seed=3)
    for s in (-width / 2, width / 2):
        with w.at((s, 0, 0), ry=90):
            n = 16
            for i in range(n):
                a0, a1 = 2 * math.pi * i / n, 2 * math.pi * (i + 1) / n
                w.span((math.cos(a0) * R, math.sin(a0) * R, 0), (math.cos(a1) * R, math.sin(a1) * R, 0), 0.14, 0.2, WM, up=(0, 0, 1))
                w.span((math.cos(a0) * R * 0.62, math.sin(a0) * R * 0.62, 0), (math.cos(a1) * R * 0.62, math.sin(a1) * R * 0.62, 0), 0.1, 0.14, WD, up=(0, 0, 1))
            for i in range(8):
                a = 2 * math.pi * i / 8
                w.span((0, 0, 0), (math.cos(a) * R, math.sin(a) * R, 0), 0.12, 0.12, WD, up=(0, 0, 1))
    for i in range(16):
        a = 2 * math.pi * (i + 0.5) / 16
        w.box((0, math.cos(a) * (R - 0.15), math.sin(a) * (R - 0.15)), (width + 0.1, 0.08, 0.5), WL, r=(math.degrees(a), 0, 0))
    w.prism((0, 0, 0), 10, 0.32, 0.32, width + 0.5, WD, r=(0, 90, 0))
    w.prism((0, 0, 0), 8, 0.12, 0.12, width + 1.3, A.IRON, r=(0, 90, 0))
    ob = w.build("Watermill_Wheel", bpy.context.scene.collection)
    return ob


def pack_watermill(k):
    """03 WATERMILL - stone ground floor, timber upper storey, red roof with a dormer and a stone chimney, the big
    water wheel on the right (river) side on a timber frame, a plank deck round the base, rocks at the water line.
    The river itself comes from the game - the model stops at the water's edge (its right side)."""
    W, D = 6.2, 5.6
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.2, [
        ("front", "door", 2.0, dict(w=1.0, h=2.1, z=0.36, steps=2, main=True)),
        ("front", "win", 4.5, dict(zc=1.8, w=0.7)),
        ("left", "win", 2.8, dict(zc=1.8, w=0.7)),
        ("right", "win", 1.3, dict(zc=1.8, w=0.6)),
        ("back", "win", 3.1, dict(zc=1.8, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 3.2)
    A.floor_band(k, x0, x1, y0 - 0.2, y1, 3.2)
    G = A.storey(k, (x0, x1, y0 - 0.2, y1), 3.28, 5.5, [
        ("front", "win", 1.5, dict(zc=1.15)),
        ("front", "win", 4.6, dict(zc=1.15)),
        ("left", "win", 2.8, dict(zc=1.15)),
        ("right", "win", 2.8, dict(zc=1.15)),
        ("back", "win", 3.1, dict(zc=1.15)),
    ])
    roof = A.gable_roof(k, x0, x1, y0 - 0.2, y1, 5.5, 2.8, "x", "red", gable_windows=((0.55, 0.7), (0.55, 0.7)))
    with k.at((0.8, -1.5, roof.z_at(0.8, -1.5) - 0.15)):
        A.dormer(k, 1.0, 0.9, 1.3, "red")
    A.chimney(k, 1.8, 0.8, 6.6, 9.3, pot=None)
    if k.detail:
        k.prism((1.8, 0.8, 9.55), 8, 0.2, 0.2, 0.3, A.IRON)
        k.frustum((1.8, 0.8, 9.8), 0.5, 0.5, 0.1, 0.1, 0.25, A.IRON)
    # wheel frame: axle bearing posts beside the right wall, the wheel hub at hx
    R = 2.3
    hx, hy, hz = x1 + 1.05, 0.2, R - 0.55
    for s in (-1, 1):
        k.box((hx + 0.85, hy + s * 0.75, hz / 2), (0.24, 0.24, hz + 0.4), WD)
        k.box((x1 + 0.12, hy + s * 1.1, hz), (0.24, 0.24, 1.2), WD)
    k.box((hx + 0.85, hy, hz + 0.25), (0.28, 1.8, 0.24), WD)
    k.box(((x1 + hx) / 2 + 0.1, hy, hz), (hx - x1 + 1.0, 0.3, 0.3), WD)
    # a chute bringing water over the top of the wheel
    k.box((hx - 0.2, hy - 1.6, R + hz - 0.05), (0.9, 3.0, 0.12), WM, r=(-4, 0, 0))
    for s in (-1, 1):
        k.box((hx - 0.2 + s * 0.45, hy - 1.6, R + hz + 0.1), (0.08, 3.0, 0.3), WM, r=(-4, 0, 0))
    k.box((hx - 0.2, hy - 1.6, R + hz + 0.02), (0.8, 2.9, 0.04), "TB_Water", r=(-4, 0, 0))
    k.box((hx - 0.2, hy - 3.0, (R + hz) / 2), (0.16, 0.16, R + hz), WD)
    # plank deck along the front and the river side
    k.box((x1 - 0.8, y0 - 1.0, 0.25), (3.2, 1.6, 0.1), WL)
    if k.detail:
        for x in (x1 - 2.2, x1 + 0.6):
            for y in (y0 - 0.3, y0 - 1.7):
                k.box((x, y, 0.1), (0.14, 0.14, 0.3), WD)
        for i in range(8):
            a = i * 0.8
            k.blob((x1 + 0.3 + math.cos(a) * 0.3, y0 - 0.3 + i * 0.85, 0.15), 0.35 + (i % 3) * 0.12, A.SM if i % 2 else A.SL, sub=1, squash=0.6)
        _prop(k, P.sack, (x1 - 1.6, y0 - 0.9, 0.3), 0, 0.8)
        _prop(k, P.sack, (x1 - 1.1, y0 - 1.1, 0.3), 30, 0.75)
        _prop(k, P.crate, (x1 - 0.2, y0 - 1.0, 0.3), 5, 0.55)
        _prop(k, P.barrel, (x0 + 0.5, y0 - 0.6, 0), 0, 0.85, 0.3)
        _prop(k, P.bush_small, (x0 - 0.3, y0 + 0.4, 0), 0, 0.9)
    _lantern(k, F["front"], 1.1, 2.4)
    k.meta.update(floors=2, style="mill", wheel=[hx, hy, hz], wheel_axis="x", wheel_radius=R)
    if k.detail:
        ob = _wheel_object(R)
        ob.location = (hx, hy, hz)
        return [ob]
    return []


PACK = [("Pack_Townhouse", pack_townhouse), ("Pack_Cottage", pack_cottage), ("Pack_Workshop", pack_workshop),
        ("Pack_Tavern", pack_tavern), ("Pack_Warehouse", pack_warehouse), ("Pack_Watermill", pack_watermill)]
