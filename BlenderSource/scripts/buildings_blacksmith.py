# Tiny Blacksmith - the smith's own house, from 04_blacksmith_house_turnaround.png (hero, front, left, right and back
# views). Front faces -Y. Main house 7 x 5 m; the open forge lean-to on the right (+X) gable end, under the big stone
# chimney that climbs that gable wall.
#
# Game fit: the door leaf is its own object ("BlacksmithHouse_Door", origin at the bottom of the doorway) so the
# End-the-Day Interactable can sit on it. Door centre at x = DOOR_X on the front wall (y = -D/2).
import math
import bpy
from tb_lib import Kit, V
import tb_arch as A
import tb_props as P

WL, WM, WD, WX = A.WL, A.WM, A.WD, A.WX
SL, SM, SD = A.SL, A.SM, A.SD
CREAM = A.CREAM
W, D = 7.0, 5.0
DOOR_X, DOOR_W, DOOR_H, DOOR_Z = -0.8, 1.1, 2.25, 0.54   # DOOR_Z: sill height (three steps)


def _door_leaf():
    """The arched plank door, planks + iron straps + ring (its own object for the Interactable)."""
    k = Kit("BlacksmithHouse_Door", seed=7)
    r = DOOR_W / 2
    body = DOOR_H - r * 0.85
    outline = [(-r, 0), (r, 0), (r, body)] + [(math.cos(math.pi * i / 8) * r, body + math.sin(math.pi * i / 8) * r * 0.85)
                                             for i in range(1, 8)] + [(-r, body)]
    k.extrude(outline, 0.07, WM, c=(0, -0.06, 0))
    for i in range(1, 4):
        x = -r + i * DOOR_W / 4
        k.box((x, -0.065, body / 2 + 0.1), (0.035, 0.03, body + 0.2), WD)
    for z in (0.35, body - 0.1):
        k.box((-r * 0.15, -0.08, z), (DOOR_W * 0.8, 0.02, 0.09), A.IRON)
        k.box((-r + 0.08, -0.085, z), (0.12, 0.02, 0.14), A.IRON)
    k.prism((r * 0.5, -0.1, body * 0.55), 10, 0.09, 0.09, 0.025, A.IRON, r=(90, 0, 0))
    k.prism((r * 0.5, -0.09, body * 0.62), 6, 0.035, 0.035, 0.04, A.IRON, r=(90, 0, 0))
    ob = k.build("BlacksmithHouse_Door", bpy.context.scene.collection)
    ob.location = (DOOR_X, -D / 2, DOOR_Z)
    return ob


def blacksmith_house(k):
    x0, x1, y0, y1 = -W / 2, W / 2, -D / 2, D / 2
    zb = 3.0          # eaves
    rise = 3.1
    # -- ground floor: a knee-high course of big grey blocks, plaster and dark timber above
    F = A.storey(k, (x0, x1, y0, y1), 0.0, zb, [
        ("front", "hole", DOOR_X - x0, dict(w=DOOR_W + 0.3, h=0.1, zc=0.1)),
        ("front", "win", 4.9, dict(zc=1.95, w=0.95, h=0.9, lead=True, box=("TB_FlowerWhite",), panes=0)),
        ("left", "win", 2.5, dict(zc=1.95, w=0.8, h=0.85, lead=True, box=("TB_FlowerWhite",), panes=0)),
        ("back", "win", 2.0, dict(zc=1.95, w=0.8, h=0.85, lead=True, panes=0)),
        ("back", "win", 5.0, dict(zc=1.95, w=0.8, h=0.85, lead=True, panes=0)),
    ], plinth=1.05, sill_rail=False, braces=True)
    # the doorway: dark recess, a heavy timber portal with a pointed arch, three stone steps
    with k.frame(F["front"].M(DOOR_X - x0, 0)):
        k.box((0, -0.18, 0.09), (1.9, 0.9, 0.18), SL)
        k.box((0, -0.12, 0.27), (1.75, 0.6, 0.18), SM)
        k.box((0, -0.06, 0.45), (1.6, 0.3, 0.18), SL)
        with k.at((0, 0, DOOR_Z)):
            r = DOOR_W / 2
            body = DOOR_H - r * 0.85
            outline = [(-r - 0.04, 0), (r + 0.04, 0), (r + 0.04, body)] + \
                      [(math.cos(math.pi * i / 8) * (r + 0.04), body + math.sin(math.pi * i / 8) * (r * 0.85 + 0.04)) for i in range(1, 8)] + \
                      [(-r - 0.04, body)]
            k.extrude(outline, 0.03, WX, c=(0, -0.02, 0))
            for s in (-1, 1):
                k.box((s * (r + 0.1), -0.07, body / 2), (0.18, 0.16, body), WD)
            pts = [(math.cos(math.pi * i / 8) * (r + 0.1), body + math.sin(math.pi * i / 8) * (r * 0.85 + 0.12)) for i in range(9)]
            for i in range(8):
                a, b = pts[i], pts[i + 1]
                k.span((a[0], -0.07, a[1]), (b[0], -0.07, b[1]), 0.16, 0.18, WD, up=(0, -1, 0))
            k.box((0, -0.07, DOOR_H + 0.25), (0.14, 0.14, 0.35), WD)
    p = k.world(F["front"].point(DOOR_X - x0, 0, 1.2))
    k.meta["door"] = [p.x, p.y, 0.0]
    k.meta["door_leaf"] = [DOOR_X, y0, DOOR_Z]
    for s in (DOOR_X - x0 - 1.0, DOOR_X - x0 + 1.0):
        with k.frame(F["front"].M(s, 2.45)):
            A.wall_lantern(k, 0.3)
    # -- roof: main ridge along the front, a big front gable over the door, a small gable on the back
    roof = A.gable_roof(k, x0, x1, y0, y1, zb, rise, "x", "red", over=0.45, gable_over=0.4, tile=0.8,
                        gable_windows=((0.7, 0.75), (0.55, 0.6)))
    gx0, gx1 = DOOR_X - 1.55, DOOR_X + 1.55
    A.storey(k, (gx0, gx1, y0 - 0.05, y0 + 0.3), zb - 0.2, zb + 0.1, [], kind="plaster")
    A.gable_roof(k, gx0, gx1, y0 - 0.1, 0.4, zb, 2.5, "y", "red", over=0.4, gable_over=0.35, tile=0.72)
    with k.frame(A.Face((gx0, y0 - 0.1, 0), (1, 0, 0), gx1 - gx0).M((gx1 - gx0) / 2, zb + 0.95)):
        A.window(k, 0.72, 0.8, lead=True, panes=0, box=("TB_FlowerWhite",))
    A.gable_roof(k, -0.8, 0.8, 0.4, y1 + 0.05, zb + 0.9, 1.4, "y", "red", over=0.25, gable_over=0.2, tile=0.55,
                 gable_windows=(None, (0.45, 0.5)))
    # -- the chimney up the right gable wall, fed by the forge hearth in the lean-to
    cx = x1 + 0.55
    if k.detail:
        P.stone_stack(k, (cx, 0.15, 0), 1.1, 3.4, courses=6, mats=(SL, SM))
        P.stone_stack(k, (cx - 0.05, 0.15, 3.4), 0.9, 3.0, courses=5, mats=(SL, SM))
        P.stone_stack(k, (cx - 0.1, 0.15, 6.4), 0.8, 1.9, courses=3, mats=(SL, SM))
    else:
        k.box((cx - 0.05, 0.15, 4.15), (0.95, 0.95, 8.3), SM)
    k.box((cx - 0.1, 0.15, 8.37), (1.05, 1.05, 0.14), SD)
    k.box((cx - 0.1, 0.15, 8.62), (0.55, 0.55, 0.36), SM)
    k.box((cx - 0.1, 0.15, 8.86), (0.75, 0.75, 0.1), SD)
    k.meta.setdefault("chimneys", []).append([cx - 0.1, 0.15, 9.0])
    # -- the forge lean-to: posts, a red shed roof falling away to the right, the stone hearth under the chimney
    lx0, lx1 = x1, x1 + 3.4
    for py in (y0 + 0.15, y1 - 0.15):
        k.box((lx1 - 0.12, py, 1.25), (0.2, 0.2, 2.5), WM)
        if k.detail:
            k.span((lx1 - 0.12, py, 1.75), (lx1 - 0.7, py, 2.45), 0.12, 0.12, WM)
    k.box((lx1 - 0.12, 0, 2.5), (0.2, D, 0.2), WM)
    k.box(((lx0 + lx1) / 2, y0 + 0.12, 2.5), (3.4, 0.16, 0.16), WM)
    with k.at((lx0, 0, 0), rz=90):
        A.shed_roof(k, -D / 2 - 0.35, D / 2 + 0.35, 0.0, 3.4, 3.55, 2.55, "red", over=0.4, tile=0.72)
    k.box(((lx0 + lx1) / 2, 0, 0.05), (3.4, D - 0.1, 0.1), SM)
    hx = x1 + 0.95
    if k.detail:
        for i in range(3):
            for j in range(2):
                k.box((hx + (j - 0.5) * 0.85, -0.3 + 0.95 * ((i + j) % 2) * 0.0, 0.2 + i * 0.35),
                      (0.84, 1.95, 0.33), SL if (i + j) % 2 else SM)
    else:
        k.box((hx, -0.3, 0.55), (1.7, 1.95, 1.1), SM)
    k.box((hx, -0.3, 1.14), (1.8, 2.05, 0.12), SD)
    # arched fire mouth facing the street (-Y) with the glowing coals
    k.box((hx, -1.3, 0.62), (0.9, 0.05, 0.62), WX)
    k.box((hx, -1.2, 0.55), (0.75, 0.25, 0.42), "TB_Fire")
    for i in range(5):
        a = math.pi * i / 4
        k.box((hx + math.cos(a) * 0.5, -1.33, 0.93 + math.sin(a) * 0.2), (0.24, 0.1, 0.16), SL, r=(0, -math.degrees(a) + 90, 0))
    k.box((hx, -0.3, 1.25), (1.2, 1.2, 0.1), "TB_Coal")
    k.meta.setdefault("glow", []).append([hx, -1.4, 0.6])
    # hood of stone rising into the chimney
    k.frustum((hx, -0.1, 1.9), 1.7, 1.8, 0.95, 0.95, 1.3, SM)
    # a lantern hung from the lean-to beam
    with k.frame(A.Face((lx0, y0 + 0.12, 0), (1, 0, 0), 3.4).M(2.3, 2.42)):
        k.box((0, 0, -0.12), (0.02, 0.02, 0.24), A.IRON)
        with k.at((0, 0, -0.62)):
            P.lantern(k, s=0.95)
        pl = k.world((0, 0, -0.45))
        k.meta.setdefault("lanterns", []).append([pl.x, pl.y, pl.z])
    # -- the smith's sign on an arm out from the front-left corner, a lantern under it
    with k.at((x0 - 0.05, y0 - 0.05, 0), rz=180):
        k.box((0.55, 0, 2.95), (1.25, 0.14, 0.14), WD)
        if k.detail:
            k.span((0.02, 0, 2.35), (0.6, 0, 2.9), 0.1, 0.1, WD)
        for s in (-1, 1):
            for i in range(3):
                k.box((0.8 + s * 0.3, 0, 2.82 - i * 0.1), (0.025, 0.035, 0.09), A.IRON, r=(0, 0, 90 * (i % 2)))
        k.box((0.8, 0, 2.2), (0.78, 0.07, 0.95), WM)
        k.box((0.8, 0, 2.2), (0.86, 0.06, 1.03), WD)
        k.frustum((0.8, 0, 1.63), 0.86, 0.06, 0.3, 0.06, 0.2, WD)
        P.emblem_mark(k, "anvil", (0.8, -0.05, 2.25), 0.62)
        P.emblem_mark(k, "anvil", (0.8, 0.05, 2.25), 0.62)
    with k.frame(F["left"].M(4.75, 2.05)):
        A.wall_lantern(k, 0.3)
    # -- dressing from the hero view
    if k.detail:
        with k.at((2.75, y0 - 0.25, 0.47), rz=-8):
            P.wheel(k, 0.47)
        with k.at((x0 - 0.45, y0 + 0.5, 0)):
            P.barrel(k, 0.9, 0.32)
        with k.at((hx + 0.1, -2.1, 0)):
            P.anvil(k)
        with k.at((lx1 - 0.9, -1.9, 0)):
            P.stool(k)
        with k.at((lx1 - 0.7, -0.6, 0), rz=12):
            P.crate(k, 0.62)
        with k.at((lx1 - 0.55, 1.4, 0)):
            P.barrel(k, 0.85, 0.33, water=True)
        with k.at((0.6, y0 - 0.45, 0), rz=4):
            P.crate(k, 0.5)
        # tongs and hammers hanging from the lean-to's front beam
        for i, x in enumerate((lx0 + 1.9, lx0 + 2.2, lx0 + 2.5)):
            k.box((x, y0 + 0.12, 2.05), (0.03, 0.03, 0.75), A.IRON, r=(0, 4 * (i - 1), 0))
            k.box((x + 0.03, y0 + 0.12, 2.05), (0.03, 0.03, 0.75), A.IRON, r=(0, -4 * (i - 1), 0))
        # firewood stacked under the lean-to at the back, a fence off its outer side
        with k.at((lx0 + 1.9, y1 - 0.5, 0), rz=90):
            P.firewood(k, 4, 1.4)
        for yy in (-2.2, -0.7):
            with k.at((lx1 + 0.35, yy, 0), rz=90):
                P.fence(k, 1.4, 0.95, 2)
        for (x, y) in ((-3.0, -3.05), (-2.2, -3.2), (1.5, -3.0), (3.2, -2.9), (-3.8, -1.6)):
            with k.at((x, y, 0)):
                P.grass_tuft(k, 1.2)
    k.meta.update(floors=1, style="blacksmith")
    if k.detail:
        return [_door_leaf()]
    return []


SMITH = [("BlacksmithHouse", blacksmith_house)]
