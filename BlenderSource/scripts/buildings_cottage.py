# Tiny Blacksmith - the eight cottages of 02_cottage_residential_variants.png (top row left to right, then bottom).
# Smaller one-and-a-half / two storey homes for the edges of town: fenced front gardens, porches and lean-tos,
# firewood, laundry lines, vines and flower boxes. Front (street / door) faces -Y.
import math
import tb_arch as A
import tb_props as P

WL, WM, WD = A.WL, A.WM, A.WD
CREAM = A.CREAM
FL_W, FL_R, FL_Y, FL_P, FL_PK = "TB_FlowerWhite", "TB_FlowerRed", "TB_FlowerYellow", "TB_FlowerPurple", "TB_FlowerPink"
SH_BLUE, SH_GREEN, SH_BROWN = "TB_RoofBlue", "TB_RoofGreenDark", "TB_WoodMedium"


def _prop(k, fn, pos, rz=0.0, *a, **kw):
    if not k.detail:
        return
    with k.at(pos, rz=rz):
        fn(k, *a, **kw)


def _fence_run(k, a, b, h=0.9):
    """Fence posts every ~1.1 m with two rails between a and b (ground points)."""
    if not k.detail:
        return
    ax, ay = a
    bx, by = b
    L = math.hypot(bx - ax, by - ay)
    n = max(1, int(round(L / 1.1)))
    for i in range(n + 1):
        t = i / n
        x, y = ax + (bx - ax) * t, ay + (by - ay) * t
        k.box((x, y, h / 2), (0.12, 0.12, h), WD)
        k.frustum((x, y, h + 0.06), 0.12, 0.12, 0.01, 0.01, 0.12, WD)
    for z in (h * 0.4, h * 0.75):
        k.span((ax, ay, z), (bx, by, z), 0.05, 0.12, WM)


def cypress(k, h=2.6):
    k.prism((0, 0, 0.15), 6, 0.07, 0.06, 0.3, "TB_Bark")
    k.blob((0, 0, h * 0.35), h * 0.2, "TB_LeafDark", sub=1, squash=1.7)
    k.blob((0, 0, h * 0.62), h * 0.15, "TB_Leaf", sub=1, squash=1.8)
    k.blob((0, 0, h * 0.85), h * 0.08, "TB_Leaf", sub=1, squash=1.8)


def _lantern(k, f, s, z):
    with k.frame(f.M(s, z)):
        A.wall_lantern(k)


def cottage_1(k):
    """02 #1 - blue slate roof with a front cross gable, blue porch roof over the arched door, stone plinth, fenced
    flower garden, a cypress."""
    W, D = 5.6, 5.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 4.0, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.5, dict(zc=1.75, box=(FL_W, FL_R))),
        ("left", "win", 2.5, dict(zc=1.75, box=(FL_W,))),
        ("right", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("back", "win", 2.8, dict(zc=1.75)),
    ], plinth=0.95)
    A.floor_band(k, x0, x1, y0, y1, 3.0, h=0.16, over=0.04)
    G = A.storey(k, (x0, x1, y0, y1), 3.06, 4.2, [
        ("left", "win", 2.5, dict(zc=0.6, w=0.6, h=0.7)),
        ("right", "win", 2.5, dict(zc=0.6, w=0.6, h=0.7)),
    ], braces=False, sill_rail=False)
    roof = A.gable_roof(k, x0, x1, y0, y1, 4.2, 2.6, "x", "blue", style="slate", tile=0.6)
    A.gable_roof(k, x0 + 0.5, 0.1, y0, 0.5, 3.0, 2.8, "y", "blue", style="slate", tile=0.55, over=0.3,
                 gable_windows=((0.62, 0.8), None))
    with k.frame(A.Face((x0 + 0.5, y0, 0), (1, 0, 0), 2.4 - 0.5 + 0.5).M(1.3, 4.1)):
        P.flower_box(k, 0.8, (FL_W, FL_R)) if k.detail else None
    A.chimney(k, -1.6, 0.8, 5.4, 7.9, pot="TB_Clay")
    with k.frame(F["front"].M(4.0, 3.0)):
        A.tile_awning(k, 2.0, 1.3, 0.0, "blue", posts=True, drop=0.6, style="slate")
    _lantern(k, F["front"], 3.2, 2.4)
    if k.detail:
        _fence_run(k, (x0 - 0.4, y0 - 1.3), (2.9, y0 - 1.3))
        _fence_run(k, (x0 - 0.4, y0 - 1.3), (x0 - 0.4, 0.4))
        _prop(k, cypress, (x0 - 0.9, 1.3, 0), 0, 2.8)
        _prop(k, P.bush_small, (-1.9, y0 - 0.7, 0), 0, 0.7, "TB_Leaf", "TB_LeafLight")
        for x in (-0.9, 0.2, 2.9):
            _prop(k, P.potted_plant, (x, y0 - 0.55, 0), 0, 0.8)
        _prop(k, P.stool, (x1 + 0.4, y0 - 0.6, 0), 0, 0.45, False)
    k.meta.update(floors=1, style="cottage")


def cottage_2(k):
    """02 #2 - thick thatch with a front cross gable and an eyebrow dormer, shingled door canopy, laundry line."""
    W, D = 5.8, 5.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 2.2, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 4.4, dict(zc=1.75, box=(FL_W,))),
        ("left", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("right", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("back", "win", 2.9, dict(zc=1.75)),
    ], plinth=0.8)
    roof = A.gable_roof(k, x0, x1, y0, y1, 3.0, 3.3, "x", "thatch", style="thatch", over=0.55, gable_over=0.45,
                        gable_windows=((0.55, 0.65), None))
    A.gable_roof(k, x0 + 0.4, 0.2, y0 - 0.2, 0.6, 3.0, 3.2, "y", "thatch", style="thatch", over=0.5, gable_over=0.4,
                 gable_windows=((0.6, 0.75), None))
    with k.at((1.6, -1.2, roof.z_at(1.6, -1.2) - 0.25)):
        A.dormer(k, 1.1, 0.85, 1.4, "thatch", style="thatch", rise=0.6)
    A.chimney(k, 0.9, 0.6, 4.6, 7.4)
    with k.frame(F["front"].M(2.2, 2.9)):
        A.tile_awning(k, 1.6, 1.0, 0.0, (WL, WM), brackets=True, drop=0.5)
    _lantern(k, F["front"], 1.35, 2.4)
    if k.detail:
        with k.at((x1 + 0.9, -0.2, 0), rz=90):
            P.laundry_line(k, 1.8, ("TB_ClothCream", "TB_ClothBlue", "TB_ClothCream"))
        _fence_run(k, (x0 - 0.3, y0 - 1.3), (1.2, y0 - 1.3))
        _fence_run(k, (3.2, y0 - 1.3), (x1 + 0.5, y0 - 1.3))
        _prop(k, cypress, (x0 - 1.0, 0.0, 0), 0, 2.4)
        _prop(k, P.bush_small, (3.9, y0 - 0.6, 0), 0, 0.7)
        _prop(k, P.barrel, (-2.4, y0 - 0.6, 0), 0, 0.75, 0.27)
    k.meta.update(floors=1, style="cottage")


def cottage_3(k):
    """02 #3 - vine-covered two-storey cottage: red roof with two dormers and a front gable, red door canopy, small
    side balcony, barrels."""
    W, D = 5.4, 5.2
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 2.9, [
        ("front", "door", 3.7, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.3, dict(zc=1.7, box=(FL_W,))),
        ("left", "win", 2.6, dict(zc=1.7, w=0.7)),
        ("right", "win", 2.6, dict(zc=1.7, w=0.7)),
        ("back", "win", 2.7, dict(zc=1.7)),
    ], kind="stone")
    A.floor_band(k, x0, x1, y0, y1, 2.9)
    G = A.storey(k, (x0, x1, y0, y1), 2.98, 5.2, [
        ("front", "win", 1.3, dict(zc=1.2, box=(FL_W, FL_R))),
        ("front", "win", 3.9, dict(zc=1.2, box=(FL_W,))),
        ("left", "win", 2.6, dict(zc=1.2)),
        ("right", "win", 2.6, dict(zc=1.2, h=1.2)),
        ("back", "win", 2.7, dict(zc=1.2)),
    ])
    roof = A.gable_roof(k, x0, x1, y0, y1, 5.2, 2.7, "x", "red")
    A.gable_roof(k, 0.4, 2.5, y0, 0.4, 5.2, 1.8, "y", "red", gable_windows=((0.5, 0.6), None), over=0.3)
    with k.at((-1.2, -1.45, roof.z_at(-1.2, -1.45) - 0.15)):
        A.dormer(k, 0.9, 0.9, 1.3, "red")
    A.chimney(k, 1.0, 1.1, 6.6, 8.8)
    with k.frame(F["front"].M(3.7, 2.95)):
        A.tile_awning(k, 1.8, 1.1, 0.0, "red", brackets=True, drop=0.45)
    with k.frame(G["right"].M(3.2, 2.98)):
        A.balcony(k, 1.6, 0.7, flowers=(FL_W,), pots=False)
    _lantern(k, F["front"], 2.85, 2.3)
    for f, s in ((F["left"], 4.8), (F["left"], 1.0), (F["front"], 0.2), (F["front"], 2.4)):
        with k.frame(f.M(s, 0)):
            A.ivy(k, 0, 0.2, 5.6, 0.35, dense=1.3)
    if k.detail:
        _prop(k, P.barrel, (0.1, y0 - 0.55, 0), 0, 0.8, 0.28)
        _prop(k, P.barrel, (-0.55, y0 - 0.6, 0), 0, 0.7, 0.25)
        _prop(k, P.crate, (-2.2, y0 - 0.6, 0), 10, 0.55)
        _prop(k, P.bush_small, (x1 + 0.4, y0 - 0.3, 0), 0, 0.8)
        _fence_run(k, (x1 + 0.9, y0 - 1.2), (x1 + 0.9, 1.5))
    k.meta.update(floors=2, style="cottage")


def cottage_4(k):
    """02 #4 - wide blue-roofed cottage with a front dormer, a red-tiled woodshed lean-to on the left (log stack
    under it) and a blue canopy over the door; planters and a cypress."""
    W, D = 5.6, 5.0
    x0, x1, y0, y1 = -W / 2 + 0.6, W / 2 + 0.6, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 3.9, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.5, dict(zc=1.75)),
        ("right", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("back", "win", 2.8, dict(zc=1.75)),
    ], plinth=0.8)
    A.floor_band(k, x0, x1, y0, y1, 3.0, h=0.16, over=0.04)
    G = A.storey(k, (x0, x1, y0, y1), 3.06, 4.1, [
        ("left", "win", 2.5, dict(zc=0.55, w=0.6, h=0.65)),
        ("right", "win", 2.5, dict(zc=0.55, w=0.6, h=0.65)),
    ], braces=False, sill_rail=False)
    roof = A.gable_roof(k, x0, x1, y0, y1, 4.1, 2.7, "x", "blue", style="slate", tile=0.6)
    cx = (x0 + x1) / 2
    with k.at((cx - 0.3, -1.3, roof.z_at(cx, -1.3) - 0.15)):
        A.dormer(k, 1.2, 1.0, 1.4, "blue", style="slate", box=(FL_W, FL_R))
    A.chimney(k, x1 - 1.0, 0.9, 5.2, 7.9)
    with k.frame(F["front"].M(3.9, 2.95)):
        A.tile_awning(k, 1.8, 1.1, 0.0, "blue", brackets=True, drop=0.45, style="slate")
    # the woodshed: open lean-to on posts against the left wall, red tiles, a log stack inside
    lx0 = x0 - 2.1
    for (px, py) in ((lx0 + 0.1, y0 + 0.1), (lx0 + 0.1, y1 - 0.1)):
        k.box((px, py, 1.25), (0.16, 0.16, 2.5), WM)
    k.box((lx0 + 0.1, (y0 + y1) / 2, 2.45), (0.16, D, 0.16), WM)
    with k.at((lx0 - 0.1, (y0 + y1) / 2, 0), rz=-90):
        A.shed_roof(k, -D / 2 - 0.2, D / 2 + 0.2, 2.2, 2.3, 3.2, 2.35, "red", over=0.2, tile=0.55)
    k.box((lx0 + 1.0, 0, 0.08), (2.0, D - 0.2, 0.16), A.SM)
    if k.detail:
        for yy in (-1.2, 0.4):
            with k.at((lx0 + 1.05, yy, 0.16), rz=90):
                P.firewood(k, 4, 1.5)
        _fence_run(k, (lx0 - 0.2, y0 - 1.2), (x0 + 1.9, y0 - 1.2))
        _prop(k, cypress, (x1 + 0.8, 0.6, 0), 0, 3.0)
        with k.at((x0 + 1.2, y0 - 0.55, 0)):
            P.flower_box(k, 1.1, (FL_W, FL_R, FL_Y))
        with k.at((x1 - 0.3, y0 - 0.55, 0)):
            P.flower_box(k, 0.9, (FL_W, FL_PK))
        _prop(k, P.potted_plant, (x0 + 3.0, y0 - 0.5, 0), 0, 0.8)
    k.meta.update(floors=1, style="cottage")


def cottage_5(k):
    """02 #5 - long red roof sweeping down over a veranda on posts, two dormers, stone walls at the corners,
    flower beds and barrels."""
    W, D = 6.2, 5.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2 + 0.6, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 4.4, dict(z=0.18, steps=1, main=True)),
        ("front", "win", 1.8, dict(zc=1.7, w=1.2, h=1.0, box=(FL_W, FL_R))),
        ("left", "win", 2.2, dict(zc=1.7, w=0.7)),
        ("right", "win", 2.2, dict(zc=1.7, w=0.7)),
        ("back", "win", 3.1, dict(zc=1.7)),
    ], plinth=0.7)
    A.quoins(k, F["front"], 0.7, 3.0)
    ye = y0 - 1.6   # the veranda edge: the front slope carries on down over it
    roof = A.gable_roof(k, x0, x1, ye, y1, 2.4, 3.9, "x", "red", over=0.25, gable_windows=((0.55, 0.7), (0.55, 0.7)))
    for x in (x0 + 0.15, x0 + 2.1, x1 - 2.2, x1 - 0.15):
        k.box((x, ye + 0.15, 1.2), (0.16, 0.16, 2.4), WM)
        if k.detail:
            k.span((x, ye + 0.15, 1.9), (x + (0.35 if x < 0 else -0.35), ye + 0.15, 2.3), 0.08, 0.08, WM)
    k.box((0, ye + 0.15, 2.36), (W, 0.16, 0.16), WM)
    k.box((0, (ye + y0) / 2, 0.06), (W, y0 - ye, 0.12), WL)
    for x in (-1.4, 1.1):
        with k.at((x, -2.1, roof.z_at(x, -2.1) - 0.2)):
            A.dormer(k, 0.95, 0.9, 1.3, "red", box=(FL_W,) if x < 0 else None)
    A.chimney(k, -2.2, 1.0, 4.4, 7.2)
    _lantern(k, F["front"], 3.6, 2.2)
    if k.detail:
        _fence_run(k, (x1 - 1.0, ye - 0.5), (x1 + 0.5, ye - 0.5))
        _fence_run(k, (x0 - 0.3, ye - 0.5), (x0 + 1.5, ye - 0.5))
        _prop(k, P.barrel, (x0 + 0.6, ye + 0.6, 0), 0, 0.85, 0.3)
        _prop(k, P.barrel, (x0 + 1.25, ye + 0.5, 0), 0, 0.7, 0.26)
        _prop(k, P.crate, (x0 + 0.5, ye - 0.2, 0), 10, 0.5)
        _prop(k, P.topiary, (x1 - 1.4, ye + 0.5, 0), 0, 1.1)
        _prop(k, P.potted_plant, (0.3, ye + 0.5, 0), 0, 0.9)
        _prop(k, cypress, (x0 - 0.8, 1.2, 0), 0, 2.6)
        with k.at((x0 + 2.8, ye + 0.45, 0.12)):
            P.flower_box(k, 1.0, (FL_R, FL_W))
    k.meta.update(floors=1, style="cottage")


def cottage_6(k):
    """02 #6 - tall narrow cottage: stone ground floor, timber upper floor with a laundry balcony, blue roof with a
    side dormer, arched door with a lantern, barrels."""
    W, D = 5.0, 5.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 2.5, dict(z=0.36, steps=2, main=True, w=1.05)),
        ("front", "win", 4.2, dict(zc=1.75, w=0.65)),
        ("left", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("right", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("back", "win", 2.5, dict(zc=1.75, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 3.0)
    A.floor_band(k, x0, x1, y0 - 0.2, y1, 3.0)
    G = A.storey(k, (x0, x1, y0 - 0.2, y1), 3.08, 5.5, [
        ("front", "win", 1.2, dict(zc=1.25, box=(FL_W,))),
        ("front", "win", 3.6, dict(zc=1.25)),
        ("left", "win", 2.5, dict(zc=1.25)),
        ("right", "win", 2.5, dict(zc=1.1, h=1.3)),
        ("back", "win", 2.5, dict(zc=1.25)),
    ])
    roof = A.gable_roof(k, x0, x1, y0 - 0.2, y1, 5.5, 3.1, "y", "blue", style="slate", tile=0.6,
                        gable_windows=((0.6, 0.75), None))
    with k.at((x0 + 1.0, 0.3, 0), rz=-90):
        with k.at((0, 0, roof.z_at(x0 + 1.0, 0.3) - 0.2)):
            A.dormer(k, 1.0, 0.9, 1.3, "blue", style="slate")
    A.chimney(k, 1.0, 1.4, 6.6, 9.2)
    with k.frame(G["right"].M(2.5, 3.08)):
        A.balcony(k, 2.4, 0.8, flowers=(FL_W,), pots=False)
    if k.detail:
        with k.frame(G["right"].M(2.5, 3.08)):
            with k.at((0, -0.75, 0)):
                k.box((0, 0, 1.55), (0.06, 0.06, 0.7), WM)
                k.box((0, -0.25, 1.9), (0.06, 0.6, 0.06), WM)
                for i, c in enumerate(("TB_ClothCream", "TB_ClothBlue", "TB_ClothCream")):
                    k.box((-0.8 + i * 0.55, -0.1, 1.6), (0.4, 0.03, 0.5), c)
                k.box((0, -0.1, 1.88), (2.2, 0.01, 0.01), "TB_Rope")
    _lantern(k, F["front"], 3.4, 2.4)
    with k.frame(G["front"].M(4.6, 0)):
        A.ivy(k, 0, 1.0, 5.3, 0.25)
    if k.detail:
        _fence_run(k, (x0 - 0.3, y0 - 1.3), (1.5, y0 - 1.3))
        _prop(k, P.barrel, (3.0, y0 - 0.9, 0), 0, 0.85, 0.3)
        _prop(k, P.barrel, (3.1, y0 - 0.2, 0), 0, 0.8, 0.28)
        _prop(k, P.crate, (-1.4, y0 - 0.65, 0), 5, 0.5)
        with k.at((0.0, y0 - 0.6, 0)):
            P.flower_box(k, 1.0, (FL_W, FL_R))
    k.meta.update(floors=2, style="cottage")


def cottage_7(k):
    """02 #7 - broad thatched cottage with two eyebrow dormers, shingled door canopy, sunflowers and vines, fence
    and barrels."""
    W, D = 6.2, 5.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 3.9, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.6, dict(zc=1.75, box=(FL_W,))),
        ("left", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("right", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("back", "win", 3.1, dict(zc=1.75)),
    ], plinth=0.8)
    roof = A.gable_roof(k, x0, x1, y0, y1, 3.0, 3.2, "x", "thatch", style="thatch", over=0.55, gable_over=0.45,
                        gable_windows=((0.55, 0.65), (0.55, 0.65)))
    for x in (-1.5, 1.4):
        with k.at((x, -1.2, roof.z_at(x, -1.2) - 0.25)):
            A.dormer(k, 1.0, 0.85, 1.3, "thatch", style="thatch", rise=0.6)
    A.chimney(k, -0.2, 0.7, 4.6, 7.3)
    with k.frame(F["front"].M(3.9, 2.9)):
        A.tile_awning(k, 1.7, 1.05, 0.0, (WL, WM), brackets=True, drop=0.5)
    _lantern(k, F["front"], 3.05, 2.35)
    for s in (2.6, 5.0):
        with k.frame(F["front"].M(s, 0)):
            A.ivy(k, 0, 0.3, 2.8, 0.2, dense=1.3)
    if k.detail:
        for i, x in enumerate((1.9, 2.3, 2.1)):
            with k.at((x, y0 - 0.7 - i * 0.12, 0)):
                k.box((0, 0, 0.8), (0.05, 0.05, 1.6 - i * 0.2), "TB_Leaf")
                k.prism((0, -0.04, 1.6 - i * 0.2), 10, 0.22, 0.22, 0.04, "TB_FlowerYellow", r=(90, 0, 0))
                k.prism((0, -0.07, 1.6 - i * 0.2), 8, 0.1, 0.1, 0.04, WD, r=(90, 0, 0))
                k.blob((0.1, 0, 0.8), 0.12, "TB_Leaf", sub=0)
        _fence_run(k, (x0 - 0.3, y0 - 1.3), (1.4, y0 - 1.3))
        _fence_run(k, (x1 - 0.7, y0 - 1.3), (x1 + 0.4, y0 - 1.3))
        _prop(k, P.barrel, (x1 - 1.0, y0 - 0.65, 0), 0, 0.8, 0.28)
        _prop(k, P.barrel, (x1 - 0.4, y0 - 0.5, 0), 0, 0.7, 0.25)
        _prop(k, P.bush_small, (x0 - 0.2, y0 - 0.5, 0), 0, 0.8)
    k.meta.update(floors=1, style="cottage")


def cottage_8(k):
    """02 #8 - red roof with a flowered front gable, blue tiled porch roof over the door on the left, laundry pole,
    barrels, stone base and a small fenced garden."""
    W, D = 5.4, 5.0
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 1.5, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 4.0, dict(zc=1.75, box=(FL_R, FL_W))),
        ("left", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("right", "win", 2.5, dict(zc=1.75, w=0.7)),
        ("back", "win", 2.7, dict(zc=1.75)),
    ], plinth=0.95)
    A.floor_band(k, x0, x1, y0, y1, 3.0, h=0.16, over=0.04)
    G = A.storey(k, (x0, x1, y0, y1), 3.06, 4.0, [], braces=False, sill_rail=False)
    roof = A.gable_roof(k, x0, x1, y0, y1, 4.0, 2.8, "x", "red", gable_windows=((0.55, 0.65), None))
    A.gable_roof(k, 1.0, x1 - 0.2, y0, 0.5, 4.0, 2.4, "y", "red", over=0.3, gable_windows=((0.7, 0.8), None))
    with k.frame(A.Face((1.0, y0, 0), (1, 0, 0), x1 - 0.2 - 1.0).M((x1 - 1.2) / 2, 4.28)):
        P.flower_box(k, 0.9, (FL_R, FL_W)) if k.detail else None
    A.chimney(k, 1.8, 0.8, 5.6, 8.1)
    with k.frame(F["front"].M(1.5, 2.95)):
        A.tile_awning(k, 2.1, 1.3, 0.0, "blue", posts=True, drop=0.55)
    _lantern(k, F["front"], 2.55, 2.35)
    if k.detail:
        with k.at((x1 + 0.7, y0 - 0.3, 0), rz=90):
            k.box((0, 0, 1.1), (0.08, 0.08, 2.2), WM)
            k.box((0, -0.35, 2.1), (0.06, 0.8, 0.06), WM)
            k.box((0, -0.55, 1.75), (0.03, 0.35, 0.6), "TB_ClothBlue")
            k.box((0, -0.2, 1.8), (0.03, 0.3, 0.5), "TB_ClothCream")
        _fence_run(k, (2.2, y0 - 1.2), (x1 + 0.3, y0 - 1.2))
        _prop(k, P.barrel, (-2.6, y0 - 0.7, 0), 0, 0.8, 0.28)
        _prop(k, P.barrel, (x1 - 0.5, y0 - 0.6, 0), 0, 0.8, 0.28)
        _prop(k, cypress, (x0 - 0.8, 0.8, 0), 0, 2.7)
        _prop(k, P.potted_plant, (0.4, y0 - 0.5, 0), 0, 0.9)
        with k.at((2.6, y0 - 0.55, 0)):
            P.flower_box(k, 0.9, (FL_R, FL_W))
    k.meta.update(floors=1, style="cottage")


COTTAGE = [("Cottage_01_BlueSlate", cottage_1), ("Cottage_02_Thatch", cottage_2), ("Cottage_03_Vines", cottage_3),
           ("Cottage_04_Woodshed", cottage_4), ("Cottage_05_Veranda", cottage_5), ("Cottage_06_Laundry", cottage_6),
           ("Cottage_07_Sunflower", cottage_7), ("Cottage_08_BluePorch", cottage_8)]
