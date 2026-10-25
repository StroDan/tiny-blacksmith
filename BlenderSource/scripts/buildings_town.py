# Tiny Blacksmith - the eight town houses of 01_town_residential_variants.png (left to right, top row then bottom).
# Front (the street side, with the main door) faces -Y. In every reference the right-hand visible face is the street
# front and the left-hand one a side wall; both are modelled, the back is kept simpler but never blank.
import math
from tb_lib import V
import tb_arch as A
import tb_props as P

WL, WM, WD = A.WL, A.WM, A.WD
CREAM, BEIGE = A.CREAM, A.BEIGE
FL_W, FL_R, FL_Y, FL_P = "TB_FlowerWhite", "TB_FlowerRed", "TB_FlowerYellow", "TB_FlowerPurple"
SH_BROWN, SH_BLUE, SH_GREEN, SH_RED = "TB_WoodMedium", "TB_RoofBlue", "TB_RoofGreenDark", "TB_RoofRedDark"


def _prop(k, fn, pos, rz=0.0, *a, **kw):
    if not k.detail:
        return
    with k.at(pos, rz=rz):
        fn(k, *a, **kw)


def _lantern(k, f, s, z):
    with k.frame(f.M(s, z)):
        A.wall_lantern(k)


def town_1(k):
    """01 #1 - red roof, front cross gable, red + blue tiled porches, stone plinth, ivy, stairs with a rail."""
    W, D = 5.8, 5.4
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 3.95, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.35, dict(zc=1.75, shutters=SH_BROWN, box=(FL_W,))),
        ("left", "win", 2.7, dict(zc=1.75, shutters=SH_BROWN, box=(FL_W,))),
        ("right", "win", 3.6, dict(zc=1.75, w=0.7)),
        ("back", "win", 2.9, dict(zc=1.75)),
    ], plinth=0.9)
    A.floor_band(k, x0, x1, y0 - 0.25, y1, 3.0)
    A.joists(k, F["front"], 2.86)
    G = A.storey(k, (x0, x1, y0 - 0.25, y1), 3.08, 5.5, [
        ("front", "win", 1.3, dict(zc=1.25, shutters=SH_BROWN, box=(FL_W, FL_R))),
        ("front", "win", 4.25, dict(zc=1.25, box=(FL_W,))),
        ("left", "win", 2.9, dict(zc=1.25, shutters=SH_BROWN)),
        ("right", "win", 2.9, dict(zc=1.25)),
        ("back", "win", 1.8, dict(zc=1.25)),
        ("back", "win", 4.2, dict(zc=1.25)),
    ], flip=True)
    roof = A.gable_roof(k, x0, x1, y0 - 0.25, y1, 5.5, 2.7, "x", "red", gable_windows=((0.6, 0.7), None))
    A.gable_roof(k, 0.25, 2.55, y0 - 0.25, 0.2, 5.5, 1.85, "y", "red", gable_windows=((0.55, 0.7), None), over=0.3)
    with k.at((-1.3, -1.75, roof.z_at(-1.3, -1.75) - 0.15)):
        A.dormer(k, 0.95, 0.95, 1.4, "red")
    A.chimney(k, -2.0, 0.85, 6.4, 9.1)
    with k.frame(F["front"].M(3.95, 3.0)):
        A.tile_awning(k, 1.9, 1.25, 0.0, "red", posts=True, drop=0.5)
    with k.frame(F["right"].M(1.4, 2.95)):
        A.tile_awning(k, 2.4, 1.2, 0.0, "blue", posts=True, drop=0.55)
    _lantern(k, F["front"], 3.1, 2.4)
    with k.frame(F["front"].M(0.25, 0)):
        A.ivy(k, 0, 0.9, 5.0, 0.3)
    with k.frame(F["left"].M(5.2, 0)):
        A.ivy(k, 0, 0.9, 4.2, 0.25)
    if k.detail:
        with k.frame(F["front"].M(3.95, 0)):
            A.steps(k, 1.4, 2, rise=0.18, run=0.3, rail=True, side=1)
        _prop(k, P.crate, (-2.3, -3.2, 0), 10, 0.62)
        _prop(k, P.crate, (-1.6, -3.25, 0), -8, 0.5)
        _prop(k, P.topiary, (-3.3, -2.2, 0), 0, 1.3)
        _prop(k, P.potted_plant, (2.9, -3.1, 0), 0, 0.9)
        _prop(k, P.crate, (-2.9, -2.4, 0), 25, 0.45)
        _prop(k, P.fence, (1.9, -3.2, 0), 0, 1.0, 0.8, 2)
    k.meta.update(floors=2, style="town")


def town_2(k):
    """01 #2 - tall narrow blue-roofed house: front gable, jettied upper floors, blue banner, side balcony."""
    W, D = 4.8, 5.2
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 1.25, dict(w=0.95, main=True)),
        ("front", "door", 3.55, dict(w=0.9, z=0.36, steps=2)),
        ("left", "win", 2.6, dict(zc=1.7, w=0.7)),
        ("right", "win", 1.6, dict(zc=1.7, w=0.7)),
        ("back", "win", 2.4, dict(zc=1.7)),
    ], plinth=1.2)
    A.floor_band(k, x0, x1, y0 - 0.3, y1, 3.0)
    A.joists(k, F["front"], 2.86)
    G = A.storey(k, (x0, x1, y0 - 0.3, y1), 3.08, 5.6, [
        ("front", "win", 1.15, dict(zc=1.25, shutters=SH_BLUE, box=(FL_W,))),
        ("front", "win", 3.65, dict(zc=1.25, box=(FL_W, FL_R))),
        ("left", "win", 2.8, dict(zc=1.25, shutters=SH_BLUE)),
        ("right", "win", 3.8, dict(zc=1.25)),
        ("back", "win", 2.4, dict(zc=1.25)),
    ])
    A.floor_band(k, x0, x1, y0 - 0.5, y1, 5.6)
    H = A.storey(k, (x0, x1, y0 - 0.5, y1), 5.68, 7.9, [
        ("front", "win", 2.4, dict(zc=1.15, w=0.75, box=(FL_W,))),
        ("left", "win", 2.9, dict(zc=1.15, w=0.7)),
        ("right", "win", 2.9, dict(zc=1.15, w=0.7)),
        ("back", "win", 2.4, dict(zc=1.15, w=0.7)),
    ], flip=True)
    roof = A.gable_roof(k, x0, x1, y0 - 0.5, y1, 7.9, 3.4, "y", "blue", gable_windows=((0.55, 0.75), (0.5, 0.6)))
    A.gable_roof(k, 0.0, x1 + 0.2, -0.6, 1.4, 7.4, 1.9, "x", "blue", gable_windows=(None, (0.5, 0.65)), over=0.3)
    A.chimney(k, -0.9, 1.7, 8.8, 11.4)
    with k.frame(F["front"].M(2.4, 4.3)):
        A.wall_banner(k, 0.55, 1.25, "TB_ClothBlue", "fleur")
    with k.frame(G["right"].M(1.5, 3.08)):
        A.balcony(k, 2.2, 0.8, flowers=(FL_W, FL_R))
    with k.frame(G["right"].M(0.3, 0)):
        A.ivy(k, 0, 2.2, 6.5, 0.25)
    _lantern(k, F["front"], 2.4, 2.5)
    if k.detail:
        _prop(k, P.crate, (-2.0, -3.1, 0), 12, 0.6)
        _prop(k, P.crate, (-1.45, -3.2, 0), -6, 0.45)
        _prop(k, P.topiary, (-2.9, -1.8, 0), 0, 1.25)
        _prop(k, P.barrel, (2.8, -2.9, 0), 0, 0.8, 0.28)
        _prop(k, P.potted_plant, (2.45, -3.3, 0), 0, 0.9)
        _prop(k, P.potted_plant, (0.2, -3.0, 0), 0, 0.8)
    k.meta.update(floors=3, style="town")


def town_3(k):
    """01 #3 - thatched townhouse: deep yellow thatch, centre dormer, thatch porch with posts, laundry line."""
    W, D = 6.4, 5.6
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "win", 1.45, dict(zc=1.7, shutters=SH_BLUE, box=(FL_W,))),
        ("front", "door", 4.3, dict(z=0.36, steps=2, main=True)),
        ("left", "win", 2.8, dict(zc=1.7, shutters=SH_BROWN, box=(FL_W,))),
        ("right", "win", 2.8, dict(zc=1.7)),
        ("back", "win", 3.2, dict(zc=1.7)),
    ], plinth=0.85)
    A.floor_band(k, x0, x1, y0 - 0.25, y1, 3.0)
    G = A.storey(k, (x0, x1, y0 - 0.25, y1), 3.08, 5.3, [
        ("front", "win", 1.4, dict(zc=1.15, box=(FL_W,))),
        ("front", "win", 4.9, dict(zc=1.15, box=(FL_W, FL_Y))),
        ("left", "win", 2.8, dict(zc=1.15, w=0.65, shutters=SH_GREEN)),
        ("right", "win", 2.8, dict(zc=1.15, w=0.65)),
        ("back", "win", 3.2, dict(zc=1.15)),
    ])
    roof = A.gable_roof(k, x0, x1, y0 - 0.25, y1, 5.3, 3.0, "x", "thatch", style="thatch", over=0.55, gable_over=0.45,
                        gable_windows=((0.55, 0.65, SH_GREEN), None))
    with k.at((0.0, -1.55, roof.z_at(0, -1.55) - 0.2)):
        A.dormer(k, 1.2, 1.0, 1.5, "thatch", style="thatch", rise=0.75)
    A.chimney(k, -1.0, 0.9, 6.8, 9.4)
    with k.frame(F["front"].M(4.3, 3.0)):
        A.tile_awning(k, 2.3, 1.4, 0.0, "thatch", posts=True, drop=0.55, style="thatch")
    _lantern(k, F["front"], 3.35, 2.35)
    with k.frame(F["front"].M(2.7, 0)):
        A.ivy(k, 0, 0.8, 3.0, 0.2)
    if k.detail:
        with k.at((3.2, -3.8, 0), rz=90):
            P.laundry_line(k, 1.3, ("TB_ClothCream", "TB_ClothCream"))
        _prop(k, P.fence, (-2.2, -3.4, 0), 0, 2.0, 0.85, 3)
        _prop(k, P.crate, (1.9, -3.25, 0), 5, 0.55)
        _prop(k, P.crate, (-3.7, -1.6, 0), 30, 0.5)
        _prop(k, P.potted_plant, (2.6, -3.3, 0), 0, 0.85)
        _prop(k, P.bush_small, (-3.6, 0.8, 0), 0, 0.9)
    k.meta.update(floors=2, style="town")


def town_4(k):
    """01 #4 - three storeys, stone ground floor, long flowered balcony, red lion banner, red door awning."""
    W, D = 5.6, 5.6
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 4.15, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.5, dict(zc=1.7, w=0.7, shutters=SH_RED)),
        ("left", "win", 2.8, dict(zc=1.7, w=0.7)),
        ("right", "win", 2.8, dict(zc=1.7, w=0.7)),
        ("back", "win", 2.8, dict(zc=1.7, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 3.0)
    A.floor_band(k, x0, x1, y0 - 0.3, y1, 3.0)
    A.joists(k, F["front"], 2.86)
    G = A.storey(k, (x0, x1, y0 - 0.3, y1), 3.08, 5.6, [
        ("front", "win", 1.2, dict(zc=1.25, shutters=SH_RED, box=(FL_W,))),
        ("front", "win", 3.8, dict(zc=1.1, w=0.8, h=1.3, shutters=SH_RED)),
        ("left", "win", 2.8, dict(zc=1.25, shutters=SH_RED)),
        ("right", "win", 2.8, dict(zc=1.25)),
        ("back", "win", 2.8, dict(zc=1.25)),
    ])
    A.floor_band(k, x0, x1, y0 - 0.5, y1, 5.6)
    H = A.storey(k, (x0, x1, y0 - 0.5, y1), 5.68, 8.0, [
        ("front", "win", 1.25, dict(zc=1.15, box=(FL_W,))),
        ("front", "win", 4.2, dict(zc=1.15, box=(FL_W, FL_R))),
        ("left", "win", 2.9, dict(zc=1.15)),
        ("right", "win", 2.9, dict(zc=1.15)),
        ("back", "win", 2.9, dict(zc=1.15)),
    ], flip=True)
    roof = A.gable_roof(k, x0, x1, y0 - 0.5, y1, 8.0, 2.9, "x", "red")
    A.gable_roof(k, x0 + 0.1, -0.1, y0 - 0.5, 0.5, 8.0, 2.4, "y", "red", gable_windows=((0.6, 0.75), None), over=0.3)
    A.chimney(k, 0.9, 1.0, 9.6, 12.0)
    with k.frame(G["front"].M(3.75, 3.08)):
        A.balcony(k, 3.1, 0.95, flowers=(FL_W, FL_R))
    with k.frame(F["front"].M(4.15, 3.1)):
        A.tile_awning(k, 1.7, 1.0, 0.0, "red", brackets=True, drop=0.45)
    with k.frame(G["left"].M(4.2, 2.3)):
        A.wall_banner(k, 0.62, 1.5, "TB_ClothRed", "lion")
    _lantern(k, F["front"], 3.25, 2.45)
    with k.frame(F["front"].M(2.6, 0)):
        A.ivy(k, 0, 0.3, 5.5, 0.25)
    if k.detail:
        _prop(k, P.barrel, (-2.1, -3.1, 0), 0, 0.85, 0.3)
        _prop(k, P.barrel, (-1.5, -3.25, 0), 0, 0.75, 0.26)
        _prop(k, P.crate, (-2.9, -2.1, 0), 15, 0.6)
        _prop(k, P.topiary, (-3.3, -0.5, 0), 0, 1.2)
        _prop(k, P.potted_plant, (3.0, -3.2, 0), 0, 0.9)
        _prop(k, P.bush_small, (3.3, -1.4, 0), 0, 0.8)
    k.meta.update(floors=3, style="town")


def town_5(k):
    """01 #5 - blue roof, stone ground floor with an outside stone stair up to the first-floor door (blue awning),
    blue fleur banner on an arm."""
    W, D = 5.8, 5.6
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 2.8, [
        ("front", "door", 0.75, dict(w=0.9, h=1.9, main=True)),
        ("left", "win", 2.8, dict(zc=1.55, w=0.7, box=(FL_W,))),
        ("right", "win", 2.8, dict(zc=1.55, w=0.7)),
        ("back", "win", 2.9, dict(zc=1.55, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 2.8)
    # outside stair along the front: from the ground at x -1.35 up to a landing before the first-floor door
    n = 10
    for i in range(n):
        xs = -1.35 + (i + 0.5) * 2.5 / n
        k.box((xs, y0 - 0.55, (i + 1) * 0.28 / 2), (2.5 / n + 0.01, 1.05, (i + 1) * 0.28), A.SM if i % 2 else A.SL)
    k.box((1.85, y0 - 0.55, 1.4), (1.55, 1.05, 2.8), A.SM)
    A.stones(k, A.Face((-1.35, y0 - 1.08, 0), (1, 0, 0), 4.0), 0, 4.0, 0, 0.9, mats=(A.SL, A.SM)) if k.detail else None
    if k.detail:
        for i in range(0, n + 1, 3):
            xs = -1.35 + i * 2.5 / n
            k.box((xs, y0 - 1.02, i * 0.28 + 0.5), (0.09, 0.09, 1.0), WM)
        k.span((-1.35, y0 - 1.02, 1.0), (1.15, y0 - 1.02, 3.8), 0.08, 0.08, WM)
        k.span((1.15, y0 - 1.02, 3.8), (2.6, y0 - 1.02, 3.8), 0.08, 0.08, WM)
        k.box((2.6, y0 - 1.02, 3.3), (0.09, 0.09, 1.0), WM)
    A.floor_band(k, x0, x1, y0 - 0.2, y1, 2.8)
    G = A.storey(k, (x0, x1, y0 - 0.2, y1), 2.88, 5.4, [
        ("front", "door", 4.75, dict(w=0.95, h=2.0)),
        ("front", "win", 1.7, dict(zc=1.25, box=(FL_W, FL_R))),
        ("left", "win", 2.8, dict(zc=1.25, shutters=SH_BLUE)),
        ("right", "win", 2.8, dict(zc=1.25)),
        ("back", "win", 2.9, dict(zc=1.25)),
    ])
    A.floor_band(k, x0, x1, y0 - 0.4, y1, 5.4)
    H = A.storey(k, (x0, x1, y0 - 0.4, y1), 5.48, 7.8, [
        ("front", "win", 1.5, dict(zc=1.15, box=(FL_W,))),
        ("front", "win", 4.4, dict(zc=1.15, box=(FL_W,))),
        ("left", "win", 2.9, dict(zc=1.15)),
        ("right", "win", 2.9, dict(zc=1.15)),
        ("back", "win", 2.9, dict(zc=1.15)),
    ], flip=True)
    roof = A.gable_roof(k, x0, x1, y0 - 0.4, y1, 7.8, 2.7, "x", "blue")
    A.gable_roof(k, x0 + 0.1, -0.3, y0 - 0.4, 0.4, 7.8, 2.2, "y", "blue", gable_windows=((0.6, 0.75), None), over=0.3)
    A.chimney(k, 1.2, 1.0, 9.3, 11.6)
    with k.frame(G["front"].M(4.75, 2.5)):
        A.tile_awning(k, 1.6, 1.0, 0.0, "blue", brackets=True, drop=0.45)
    with k.frame(G["right"].M(1.0, 1.7)):
        A.arm_banner(k, 0.85, 0.45, 0.95, "TB_ClothBlue", "fleur")
    _lantern(k, G["front"], 3.9, 2.0)
    with k.frame(F["front"].M(0.2, 0)):
        A.ivy(k, 0, 0.3, 4.6, 0.25)
    if k.detail:
        _prop(k, P.crate, (-2.3, -3.6, 0), 8, 0.6)
        _prop(k, P.crate, (2.9, -3.4, 0), -10, 0.55)
        _prop(k, P.potted_plant, (-1.7, -3.55, 0), 0, 0.9)
        _prop(k, P.topiary, (3.35, -2.1, 0), 0, 1.3)
        _prop(k, P.potted_plant, (-3.2, -1.6, 0), 0, 0.8)
    k.meta.update(floors=3, style="town")


def town_6(k):
    """01 #6 - green roof with a front gable, green-tiled porch on posts, ivy-hung balcony, cloth on an arm."""
    W, D = 5.4, 5.8
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 3.95, dict(z=0.36, steps=2, main=True)),
        ("front", "win", 1.25, dict(zc=1.7, shutters=SH_GREEN, box=(FL_W,))),
        ("left", "win", 2.9, dict(zc=1.7, shutters=SH_GREEN)),
        ("right", "win", 2.9, dict(zc=1.7)),
        ("back", "win", 2.7, dict(zc=1.7)),
    ], plinth=0.75)
    A.floor_band(k, x0, x1, y0 - 0.25, y1, 3.0)
    A.joists(k, F["front"], 2.86)
    G = A.storey(k, (x0, x1, y0 - 0.25, y1), 3.08, 5.6, [
        ("front", "win", 1.25, dict(zc=1.25, shutters=SH_GREEN, box=(FL_W,))),
        ("front", "win", 3.95, dict(zc=1.1, h=1.3)),
        ("left", "win", 2.9, dict(zc=1.25)),
        ("right", "win", 2.9, dict(zc=1.25)),
        ("back", "win", 2.7, dict(zc=1.25)),
    ])
    A.floor_band(k, x0, x1, y0 - 0.45, y1, 5.6)
    H = A.storey(k, (x0, x1, y0 - 0.45, y1), 5.68, 7.9, [
        ("front", "win", 2.7, dict(zc=1.15, box=(FL_W,))),
        ("left", "win", 2.9, dict(zc=1.15, shutters=SH_GREEN)),
        ("right", "win", 2.9, dict(zc=1.15)),
    ], flip=True)
    roof = A.gable_roof(k, x0, x1, y0 - 0.45, y1, 7.9, 3.2, "y", "green", gable_windows=((0.5, 0.6), None))
    A.gable_roof(k, x0 - 0.2, 0, -0.4, 1.8, 7.3, 2.1, "x", "green", gable_windows=((0.5, 0.65), None), over=0.3)
    A.chimney(k, 1.0, 1.8, 8.9, 11.6)
    with k.frame(F["front"].M(1.3, 2.95)):
        A.tile_awning(k, 2.6, 1.4, 0.0, "green", posts=True, drop=0.55)
    with k.frame(G["front"].M(3.95, 3.08)):
        A.balcony(k, 2.2, 0.85, flowers=(FL_W,))
    with k.frame(G["front"].M(5.2, 0)):
        A.ivy(k, 0, 2.5, 6.0, 0.3, dense=1.4)
    with k.frame(G["front"].M(3.0, 0)):
        A.ivy(k, 0, 2.2, 4.0, 0.2)
    with k.frame(H["right"].M(0.6, 1.6)):
        A.arm_banner(k, 0.8, 0.4, 0.8, "TB_ClothCream", "none")
    _lantern(k, F["front"], 3.15, 2.4)
    if k.detail:
        with k.frame(F["front"].M(3.95, 0)):
            A.steps(k, 1.4, 2, rise=0.18, run=0.3, rail=True, side=1)
        for xs in (-2.4, -0.4):
            _prop(k, P.fence, (xs + 0.9, -3.5, 0), 0, 1.4, 0.8, 2)
        _prop(k, P.crate, (2.9, -3.2, 0), 10, 0.55)
        _prop(k, P.potted_plant, (-0.9, -3.3, 0), 0, 0.85)
        _prop(k, P.topiary, (-3.1, -2.0, 0), 0, 1.2)
    k.meta.update(floors=3, style="town")


def _flare(k, x0, x1, y_eave, zb, side, color, width_extra=0.3):
    """A bell-cast skirt: a shallower strip of tiles kicking out beyond a steep roof's eave (01 #7)."""
    mats = A.ROOF[color]
    th = math.radians(18)
    d = V(0, side * math.cos(th), -math.sin(th))
    n = V(0, side * math.sin(th), math.cos(th))
    O = V((x0 + x1) / 2, y_eave, zb + 0.28)
    A._slope(k, O, d, n, x1 - x0 + width_extra * 2, 0.95, "tile", mats, tile=0.6)


def town_7(k):
    """01 #7 - steep red roof with flared eaves and a centre dormer, stone ground floor with two arched doors,
    flowered balcony, lanterns on wooden arms."""
    W, D = 6.0, 5.6
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 1.35, dict(w=1.1, h=2.2)),
        ("front", "door", 4.6, dict(z=0.36, steps=2, main=True)),
        ("left", "win", 2.8, dict(zc=1.7, w=0.7)),
        ("right", "win", 2.8, dict(zc=1.7, w=0.7)),
        ("back", "win", 3.0, dict(zc=1.7, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 3.0)
    A.floor_band(k, x0, x1, y0 - 0.3, y1, 3.0)
    A.joists(k, F["front"], 2.86)
    G = A.storey(k, (x0, x1, y0 - 0.3, y1), 3.08, 5.5, [
        ("front", "win", 1.2, dict(zc=1.25, box=(FL_W, FL_R))),
        ("front", "win", 3.9, dict(zc=1.1, h=1.3)),
        ("left", "win", 2.8, dict(zc=1.25)),
        ("right", "win", 2.8, dict(zc=1.25)),
        ("back", "win", 3.0, dict(zc=1.25)),
    ])
    roof = A.gable_roof(k, x0, x1, y0 - 0.3, y1, 5.5, 3.6, "x", "red", over=0.1, gable_windows=((0.55, 0.7), (0.55, 0.7)))
    for side, ye in ((-1, y0 - 0.3 - 0.05), (1, y1 + 0.05)):
        _flare(k, x0, x1, ye, 5.5, side, "red")
    with k.at((0.0, -1.6, roof.z_at(0, -1.6) - 0.2)):
        A.dormer(k, 1.1, 1.0, 1.5, "red", rise=0.7)
    A.chimney(k, -1.2, 0.8, 7.2, 10.1)
    with k.frame(G["front"].M(3.6, 3.08)):
        A.balcony(k, 3.2, 0.9, flowers=(FL_W, FL_R))
    with k.frame(F["front"].M(4.6, 3.1)):
        A.tile_awning(k, 2.0, 1.1, 0.0, "red", brackets=True, drop=0.45)
    with k.frame(G["front"].M(0.25, 2.3)):
        A.hanging_bracket_lantern(k, 0.75)
    with k.frame(G["right"].M(0.5, 2.1)):
        A.hanging_bracket_lantern(k, 0.8)
    with k.frame(G["front"].M(5.6, 0)):
        A.ivy(k, 0, 0.5, 4.3, 0.25, dense=1.3)
    if k.detail:
        _prop(k, P.barrel, (0.2, -3.35, 0), 0, 0.9, 0.34)
        _prop(k, P.topiary, (-3.4, -2.6, 0), 0, 1.5)
        _prop(k, P.topiary, (3.45, -2.4, 0), 0, 1.3)
        _prop(k, P.crate, (-2.1, -3.35, 0), 12, 0.55)
        _prop(k, P.potted_plant, (2.6, -3.35, 0), 0, 0.8)
    k.meta.update(floors=2, style="town")


def town_8(k):
    """01 #8 - tall stone-built lower half, timber top storey with two flowered balconies, blue roof with a front
    gable, a cream canvas awning over a shop window."""
    W, D = 5.6, 5.6
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    F = A.storey(k, (x0, x1, y0, y1), 0.0, 3.0, [
        ("front", "door", 4.3, dict(w=1.05, h=2.25, z=0.18, steps=1, main=True)),
        ("front", "win", 1.55, dict(zc=1.45, w=1.3, h=1.1, panes=2)),
        ("left", "win", 2.8, dict(zc=1.7, w=0.7)),
        ("right", "win", 2.8, dict(zc=1.7, w=0.7)),
        ("back", "win", 2.8, dict(zc=1.7, w=0.7)),
    ], kind="stone")
    G = A.storey(k, (x0, x1, y0, y1), 3.0, 5.6, [
        ("front", "win", 1.5, dict(zc=1.3, w=0.7, box=(FL_W,))),
        ("front", "win", 4.3, dict(zc=1.3, w=0.7)),
        ("left", "win", 2.8, dict(zc=1.3, w=0.7)),
        ("right", "win", 2.8, dict(zc=1.3, w=0.7)),
        ("back", "win", 2.8, dict(zc=1.3, w=0.7)),
    ], kind="stone")
    A.quoins(k, F["front"], 0, 5.6)
    A.quoins(k, F["left"], 0, 5.6)
    A.floor_band(k, x0, x1, y0 - 0.3, y1, 5.6)
    A.joists(k, G["front"], 5.46)
    H = A.storey(k, (x0, x1, y0 - 0.3, y1), 5.68, 8.1, [
        ("front", "win", 1.4, dict(zc=1.2)),
        ("front", "win", 4.3, dict(zc=1.2)),
        ("left", "win", 2.9, dict(zc=1.2)),
        ("right", "win", 2.9, dict(zc=1.2)),
        ("back", "win", 2.9, dict(zc=1.2)),
    ])
    roof = A.gable_roof(k, x0, x1, y0 - 0.3, y1, 8.1, 2.8, "x", "blue")
    A.gable_roof(k, 0.3, x1 - 0.1, y0 - 0.3, 0.6, 8.1, 2.3, "y", "blue", gable_windows=((0.55, 0.7), None), over=0.3)
    A.chimney(k, -1.7, 0.9, 9.6, 12.0)
    with k.frame(H["front"].M(1.4, 5.68)):
        A.balcony(k, 2.2, 0.85, flowers=(FL_W, FL_R))
    with k.frame(H["front"].M(4.3, 5.68)):
        A.balcony(k, 2.0, 0.85, flowers=(FL_W,))
    with k.frame(F["front"].M(1.55, 2.6)):
        A.cloth_awning(k, 1.9, 1.3, 0.0, ("TB_ClothCream",), poles=True, drop=0.5)
    with k.frame(G["right"].M(0.6, 1.9)):
        A.hanging_bracket_lantern(k, 0.8)
    with k.frame(G["front"].M(3.0, 0)):
        A.ivy(k, 0, 1.0, 5.0, 0.3, dense=1.2)
    if k.detail:
        _prop(k, P.crate, (-2.3, -3.25, 0), 6, 0.55)
        _prop(k, P.crate, (-0.9, -3.3, 0), -8, 0.5)
        _prop(k, P.potted_plant, (-1.6, -3.4, 0), 0, 0.8, WM)
        _prop(k, P.barrel, (2.9, -3.05, 0), 0, 0.8, 0.28)
        _prop(k, P.topiary, (-3.3, -1.5, 0), 0, 1.1)
    k.meta.update(floors=3, style="town")


TOWN = [("Town_01_RedPorch", town_1), ("Town_02_BlueBanner", town_2), ("Town_03_Thatch", town_3),
        ("Town_04_RedBalcony", town_4), ("Town_05_BlueStair", town_5), ("Town_06_GreenPorch", town_6),
        ("Town_07_RedFlared", town_7), ("Town_08_StoneTower", town_8)]
