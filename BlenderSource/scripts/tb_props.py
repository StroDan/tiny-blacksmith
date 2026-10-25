# Tiny Blacksmith - props (reference: 06_city_props.png and the props strip of 04_blacksmith_house_turnaround.png).
# Every function builds at the kit's current frame: origin on the ground, the prop's front toward -Y.
import math
from tb_lib import V

WL, WM, WD, WX = "TB_WoodLight", "TB_WoodMedium", "TB_WoodDark", "TB_WoodDarker"
SL, SM, SD = "TB_StoneLight", "TB_StoneMedium", "TB_StoneDark"
IRON = "TB_MetalDark"


# ---- containers --------------------------------------------------------------------------------------------------

def barrel(k, h=0.95, r=0.36, water=False, lid=True):
    """Stave barrel: bulged 12-sided body, three dark iron hoops, a lid (or water for the quench barrel)."""
    n = 12
    rb, rm = r * 0.84, r
    k.prism((0, 0, h * 0.25), n, rb, rm, h * 0.5, WM, phase=15)
    k.prism((0, 0, h * 0.75), n, rm, rb, h * 0.5, WM, phase=15)
    if k.detail:
        for z in (0.12, 0.5, 0.88):
            t = 1 - abs(z - 0.5) * 2               # 0 at the ends, 1 at the belly
            rad = rb + (rm - rb) * t + 0.014
            k.prism((0, 0, h * z), n, rad, rad, h * 0.07, IRON, phase=15)
        # a few darker staves so it reads as planks
        for i in range(0, n, 3):
            a = math.radians(15 + 360 * i / n + 15)
            k.box((math.cos(a) * rm * 0.97, math.sin(a) * rm * 0.97, h * 0.5), (0.02, 0.02, h * 0.8), WD, r=(0, 0, math.degrees(a)))
    top = water and "TB_Water" or WD
    k.prism((0, 0, h - 0.02), n, rb * 0.92, rb * 0.92, 0.03, top, phase=15)


def crate(k, s=0.8, h=None, filled=None):
    """Plank crate: body, dark edge frame, an X brace on the front and back."""
    h = h or s
    k.box((0, 0, h / 2), (s * 0.96, s * 0.96, h * 0.96), WM)
    e = s * 0.09
    for x in (-1, 1):
        for y in (-1, 1):
            k.box((x * (s / 2 - e / 2), y * (s / 2 - e / 2), h / 2), (e, e, h), WD)
    for z in (e / 2, h - e / 2):
        for y in (-1, 1):
            k.box((0, y * (s / 2 - e / 2), z), (s, e, e), WD)
        for x in (-1, 1):
            k.box((x * (s / 2 - e / 2), 0, z), (e, s, e), WD)
    if k.detail:
        diag = math.degrees(math.atan2(h - 2 * e, s - 2 * e))
        L = math.hypot(s - 2 * e, h - 2 * e)
        for y in (-1, 1):
            k.box((0, y * (s / 2 - 0.005), h / 2), (L, 0.03, e * 0.9), WD, r=(0, -diag * y, 0))
        for x in (-1, 1):
            k.box((x * (s / 2 - 0.005), 0, h / 2), (0.03, L, e * 0.9), WD, r=(diag * x, 0, 0))
    if filled:
        filled(k, s, h)


def crate_lid_nails(k, s, h):
    for i in range(14):
        x, y = k.r(-s * 0.35, s * 0.35), k.r(-s * 0.35, s * 0.35)
        k.box((x, y, h - 0.02), (0.02, 0.14, 0.02), IRON, r=(0, 0, k.r(0, 180)))


def sack(k, s=1.0, mat="TB_Sack"):
    k.blob((0, 0, 0.24 * s), 0.3 * s, mat, sub=1, squash=0.85, jitter=0.08)
    k.frustum((0, 0, 0.52 * s), 0.2 * s, 0.2 * s, 0.1 * s, 0.1 * s, 0.14 * s, mat, r=(0, 0, 20))
    k.prism((0, 0, 0.58 * s), 6, 0.07 * s, 0.07 * s, 0.04 * s, "TB_Rope")
    k.frustum((0, 0, 0.64 * s), 0.1 * s, 0.1 * s, 0.18 * s, 0.14 * s, 0.1 * s, mat, r=(0, 0, 35))


def bucket(k, water=True, handle=True):
    k.prism((0, 0, 0.17), 10, 0.16, 0.2, 0.34, WM)
    for z in (0.07, 0.28):
        rr = 0.16 + 0.04 * z / 0.34 + 0.012
        k.prism((0, 0, z), 10, rr, rr, 0.04, IRON)
    if water:
        k.prism((0, 0, 0.32), 10, 0.18, 0.18, 0.01, "TB_Water")
    if handle and k.detail:
        for a in range(0, 181, 30):
            t = math.radians(a)
            k.box((math.cos(t) * 0.2, 0, 0.34 + math.sin(t) * 0.16), (0.02, 0.02, 0.08), IRON, r=(0, -a + 90, 0))


def firewood(k, rows=3, length=1.0):
    for row in range(rows):
        for i in range(rows + 1 - row):
            x = -rows * 0.12 + (i + row * 0.5) * 0.24
            z = 0.12 + row * 0.2
            L = length * k.r(0.9, 1.08)
            k.prism((x, 0, z), 6, 0.11, 0.11, L, WM, r=(90, 0, 0), phase=k.r(0, 60))
            k.prism((x, -L / 2 - 0.004, z), 6, 0.09, 0.09, 0.01, WL, r=(90, 0, 0))


def firewood_pile(k):
    """Pyramid of split logs (06: Firewood Pile / 04 back view)."""
    for row in range(4):
        for i in range(5 - row):
            x = -0.5 + (i + row * 0.5) * 0.25
            z = 0.12 + row * 0.21
            L = 0.9 * k.r(0.9, 1.1)
            k.prism((x, k.r(-0.05, 0.05), z), 5, 0.12, 0.12, L, "TB_WoodMedium" if (i + row) % 3 else WL, r=(90, 0, 0), phase=k.r(0, 70))
            k.prism((x, -L / 2 - 0.004, z), 5, 0.1, 0.1, 0.01, WL, r=(90, 0, 0))


# ---- furniture & village street furniture --------------------------------------------------------------------------

def _post(k, x, y, h, w=0.14, mat=WM, cap=True):
    k.box((x, y, h / 2), (w, w, h), mat)
    if cap:
        k.frustum((x, y, h + 0.07), w, w, 0.01, 0.01, 0.14, mat)


def fence(k, length=2.0, h=1.0, posts=3):
    """06 Fence (Straight): pointed square posts, two plank rails on the front."""
    for i in range(posts):
        x = -length / 2 + length * i / (posts - 1)
        _post(k, x, 0, h)
    for z in (h * 0.38, h * 0.72):
        k.box((0, -0.1, z), (length + 0.1, 0.05, 0.15), WL)


def fence_corner(k, length=2.0, h=1.0):
    with k.at((0, 0, 0)):
        for x in (-length / 2, 0):
            _post(k, x, 0, h)
        for z in (h * 0.38, h * 0.72):
            k.box((-length / 4, -0.1, z), (length / 2 + 0.1, 0.05, 0.15), WL)
    _post(k, 0, length / 2, h)
    for z in (h * 0.38, h * 0.72):
        k.box((0.1, length / 4, z), (0.05, length / 2 + 0.1, 0.15), WL)


def fence_gate(k, width=1.3, h=1.1):
    for x in (-width / 2 - 0.1, width / 2 + 0.1):
        _post(k, x, 0, h + 0.15, w=0.16, mat=WD)
    gw = width
    for z in (0.22, h - 0.12):
        k.box((0, -0.02, z), (gw, 0.06, 0.14), WM)
    for i in range(5):
        k.box((-gw / 2 + 0.12 + i * (gw - 0.24) / 4, -0.06, h / 2 + 0.05), (0.13, 0.04, h - 0.15), WL if i % 2 else WM)
    if k.detail:
        a = math.degrees(math.atan2(h - 0.34, gw - 0.2))
        k.box((0, -0.1, h / 2 + 0.05), (math.hypot(gw - 0.2, h - 0.34), 0.04, 0.12), WD, r=(0, -a, 0))
        for z in (0.25, h - 0.15):
            k.box((-gw / 2 + 0.05, -0.12, z), (0.28, 0.02, 0.05), IRON)


def bench(k, length=1.5):
    k.box((0, -0.09, 0.47), (length, 0.17, 0.08), WL)
    k.box((0, 0.09, 0.47), (length, 0.17, 0.08), WM)
    for x in (-length / 2 + 0.2, length / 2 - 0.2):
        k.frustum((x, 0, 0.215), 0.12, 0.42, 0.12, 0.3, 0.43, WD)
    if k.detail:
        k.box((0, 0, 0.16), (length - 0.35, 0.06, 0.08), WD)


def stool(k, h=0.5, square=True):
    if square:
        k.box((0, 0, h - 0.03), (0.4, 0.4, 0.07), WL)
    else:
        k.prism((0, 0, h - 0.03), 8, 0.21, 0.21, 0.07, WL)
    for i in range(4 if square else 3):
        a = math.radians(45 + i * (90 if square else 120))
        x, y = math.cos(a) * 0.14, math.sin(a) * 0.14
        k.span((x * 1.35, y * 1.35, 0), (x * 0.8, y * 0.8, h - 0.06), 0.06, 0.06, WM)
    if square and k.detail:
        for s in (-1, 1):
            k.box((0, s * 0.13, h * 0.35), (0.34, 0.04, 0.04), WM)


def lantern(k, glow=True, s=1.0):
    """Small iron lantern, glowing core (06 Lantern (Small)); origin at its base."""
    k.box((0, 0, 0.03 * s), (0.2 * s, 0.2 * s, 0.05 * s), IRON)
    k.box((0, 0, 0.17 * s), (0.14 * s, 0.14 * s, 0.22 * s), "TB_LanternGlow" if glow else "TB_Glass")
    for x in (-1, 1):
        for y in (-1, 1):
            k.box((x * 0.085 * s, y * 0.085 * s, 0.17 * s), (0.03 * s, 0.03 * s, 0.25 * s), IRON)
    k.frustum((0, 0, 0.33 * s), 0.22 * s, 0.22 * s, 0.06 * s, 0.06 * s, 0.1 * s, IRON)
    k.prism((0, 0, 0.42 * s), 8, 0.04 * s, 0.04 * s, 0.06 * s, IRON, r=(90, 0, 0), caps=True)


def lantern_post(k):
    """06 Lantern Post: square post on an iron shoe, top arm with a brace, lantern hung from the arm (light at
    meta 'lights')."""
    k.frustum((0, 0, 0.12), 0.34, 0.34, 0.22, 0.22, 0.24, IRON)
    k.box((0, 0, 1.3), (0.17, 0.17, 2.4), WM)
    k.box((0, 0, 2.53), (0.22, 0.22, 0.08), WD)
    k.box((0.3, 0, 2.38), (0.78, 0.13, 0.13), WM)
    k.span((0.08, 0, 1.95), (0.45, 0, 2.33), 0.08, 0.08, WM)
    k.box((0.55, 0, 2.2), (0.02, 0.02, 0.2), IRON)
    with k.at((0.55, 0, 1.72)):
        lantern(k, s=1.1)
    k.meta.setdefault("lights", []).append([0.55, 0, 1.9])


def sign_post(k):
    """06 Sign Post: a post with three arrow boards."""
    k.box((0, 0, 1.2), (0.14, 0.14, 2.4), WM)
    k.frustum((0, 0, 2.47), 0.14, 0.14, 0.02, 0.02, 0.14, WM)
    for z, yaw, L in ((2.1, 0, 0.9), (1.7, 180, 0.8), (1.3, 20, 0.85)):
        with k.at((0, 0, z), rz=yaw):
            k.extrude([(0.08, -0.13), (L - 0.15, -0.13), (L, 0), (L - 0.15, 0.13), (0.08, 0.13)], 0.05, WL, c=(0, -0.1, 0))


def hanging_sign(k, emblem="anvil", iron=True, board=0.7):
    """06 Blacksmith Sign (Hanging): iron post + arm (iron=True) or a short wooden wall arm, chains, a framed board
    with a painted emblem on both faces. Board centre ~1.0 below the arm."""
    arm = 0.95
    if iron:
        k.box((0, 0, 1.4), (0.1, 0.1, 2.8), IRON)
        k.box((0, 0, 0.05), (0.3, 0.3, 0.1), IRON)
    k.box((arm / 2, 0, 2.72), (arm + 0.1, 0.09, 0.09), IRON if iron else WD)
    if k.detail:
        k.span((0.05, 0, 2.3), (0.45, 0, 2.7), 0.05, 0.05, IRON if iron else WD)
        k.prism((arm + 0.03, 0, 2.78), 8, 0.06, 0.06, 0.03, IRON, r=(90, 0, 0))
    bx = arm * 0.55
    for s in (-1, 1):
        x = bx + s * board * 0.36
        for i in range(3):
            k.box((x, 0, 2.62 - i * 0.1), (0.025, 0.035, 0.09), IRON, r=(0, 0, 90 * (i % 2)))
    c = (bx, 0, 2.1)
    k.box(c, (board, 0.06, board * 0.78), WM)
    k.box(c, (board + 0.06, 0.05, board * 0.78 + 0.06), WD)
    emblem_mark(k, emblem, (bx, -0.04, 2.1), board * 0.62)
    emblem_mark(k, emblem, (bx, 0.04, 2.1), board * 0.62)


def emblem_mark(k, emblem, c, s, mat=IRON):
    x, y, z = c
    if emblem == "anvil":
        k.box((x, y, z + s * 0.14), (s * 0.62, 0.012, s * 0.16), mat)                     # face
        k.frustum((x - s * 0.4, y, z + s * 0.16), s * 0.22, 0.012, 0.01, 0.012, s * 0.1, mat, r=(0, 90, 0))  # horn
        k.box((x + s * 0.02, y, z - s * 0.02), (s * 0.22, 0.012, s * 0.2), mat)           # waist
        k.box((x + s * 0.02, y, z - s * 0.17), (s * 0.5, 0.012, s * 0.1), mat)            # foot
    elif emblem == "mug":
        k.box((x, y, z), (s * 0.34, 0.012, s * 0.46), "TB_Gold")
        k.box((x, y, z + s * 0.24), (s * 0.4, 0.012, s * 0.12), "TB_ClothCream")
        k.box((x + s * 0.24, y, z), (s * 0.08, 0.012, s * 0.26), "TB_Gold")
    elif emblem == "lion":
        k.blob((x, y, z), s * 0.25, "TB_Gold", sub=1, sx=1, sy=0.05, squash=1.2)
        k.box((x + s * 0.18, y, z + s * 0.2), (s * 0.18, 0.012, s * 0.2), "TB_Gold")
    elif emblem == "fleur":
        k.frustum((x, y, z + s * 0.1), s * 0.1, 0.012, 0.01, 0.012, s * 0.4, "TB_ClothCream")
        for sx in (-1, 1):
            k.box((x + sx * s * 0.13, y, z + s * 0.05), (s * 0.1, 0.012, s * 0.3), "TB_ClothCream", r=(0, sx * 30, 0))
        k.box((x, y, z - s * 0.08), (s * 0.34, 0.012, s * 0.06), "TB_ClothCream")


def bridge_railing(k, length=2.6):
    """06 Bridge Railing Section: two capped stone pillars, iron balusters between two rails, on a stone curb."""
    for x in (-length / 2, length / 2):
        stone_stack(k, (x, 0, 0), 0.42, 1.15, courses=3)
        k.box((x, 0, 1.2), (0.5, 0.5, 0.1), SL)
    k.box((0, 0, 0.12), (length, 0.36, 0.24), SM)
    k.box((0, 0, 0.3), (length - 0.4, 0.07, 0.07), IRON)
    k.box((0, 0, 0.95), (length - 0.4, 0.08, 0.08), IRON)
    n = int((length - 0.4) / 0.2)
    for i in range(n + 1):
        x = -(length - 0.5) / 2 + i * (length - 0.5) / n
        k.box((x, 0, 0.62), (0.045, 0.045, 0.64), IRON)
        if k.detail and i % 2 == 0:
            k.prism((x, 0, 0.62), 6, 0.05, 0.05, 0.08, IRON)


def stone_stack(k, c, w, h, courses=3, mats=(SL, SM)):
    """A square pillar of jittered stone blocks (chimneys, pillars, gate posts)."""
    x, y, z0 = c
    ch = h / courses
    for i in range(courses):
        jx, jy = k.r(-0.02, 0.02), k.r(-0.02, 0.02)
        k.box((x + jx, y + jy, z0 + ch * (i + 0.5)), (w + k.r(-0.02, 0.04), w + k.r(-0.02, 0.04), ch - 0.02), mats[i % len(mats)])


def wheel(k, r=0.45, spokes=8, t=0.08, mat=WM, rim=WD, hub=IRON):
    """A spoked cart wheel standing in the XZ plane (axle along Y), centred on the origin."""
    n = 16
    for i in range(n):
        a0 = 2 * math.pi * i / n
        a1 = 2 * math.pi * (i + 1) / n
        p0 = V(math.cos(a0) * r, 0, math.sin(a0) * r)
        p1 = V(math.cos(a1) * r, 0, math.sin(a1) * r)
        k.span(p0 * 0.93, p1 * 0.93, t, 0.1 * r / 0.45 + 0.02, rim, up=(0, 1, 0))
    for i in range(spokes):
        a = 2 * math.pi * i / spokes
        k.span((0, 0, 0), (math.cos(a) * r * 0.9, 0, math.sin(a) * r * 0.9), 0.045, 0.045, mat)
    k.prism((0, 0, 0), 8, r * 0.18, r * 0.18, t * 1.6, hub, r=(90, 0, 0))


def wheelbarrow(k):
    k.frustum((0, 0.1, 0.62), 0.62, 0.8, 0.82, 1.05, 0.34, WM)
    k.frustum((0, 0.1, 0.8), 0.72, 0.95, 0.74, 0.97, 0.02, WD)
    with k.at((0, -0.62, 0.3), rz=90):
        wheel(k, r=0.28, spokes=6, t=0.06, mat=WD, rim=IRON)
    for s in (-1, 1):
        k.span((s * 0.22, -0.62, 0.3), (s * 0.3, 1.0, 0.72), 0.06, 0.06, WD)
        k.span((s * 0.25, 0.4, 0.45), (s * 0.25, 0.45, 0.0), 0.05, 0.05, WD)


def cart(k):
    """06 Cart: plank box bed on two big spoked wheels, shafts forward (-Y)."""
    k.box((0, 0.1, 0.72), (1.2, 1.9, 0.08), WM)
    for s in (-1, 1):
        k.box((s * 0.58, 0.1, 0.98), (0.06, 1.9, 0.45), WL)
        for y in (-0.8, 0.1, 1.0):
            k.box((s * 0.62, y, 0.95), (0.07, 0.08, 0.52), WD)
    for y in (-0.83, 1.03):
        k.box((0, y, 0.98), (1.2, 0.06, 0.45), WL)
    for s in (-1, 1):
        with k.at((s * 0.75, 0.2, 0.55), rz=90):
            wheel(k, r=0.55, spokes=8, t=0.08)
        k.span((s * 0.35, -0.85, 0.7), (s * 0.4, -2.1, 0.45), 0.07, 0.08, WD)
    k.prism((0, 0.2, 0.55), 8, 0.05, 0.05, 1.5, IRON, r=(0, 90, 0))
    k.box((0, -2.08, 0.2), (0.08, 0.08, 0.5), WD)


def market_stall(k):
    """06 Market Stall: four posts, a counter with an emblem panel, striped red/cream canopy, goods."""
    W, D = 2.4, 1.3
    for x in (-W / 2, W / 2):
        k.box((x, -D / 2, 1.2), (0.12, 0.12, 2.4), WM)
        k.box((x, D / 2, 1.45), (0.12, 0.12, 2.9), WM)
    k.box((0, -0.35, 0.45), (W - 0.1, 0.6, 0.9), WM)
    k.box((0, -0.35, 0.93), (W + 0.1, 0.72, 0.06), WL)
    if k.detail:
        for i in range(6):
            k.box((-W / 2 + 0.25 + i * (W - 0.5) / 5, -0.66, 0.45), (0.14, 0.02, 0.84), WL if i % 2 else WD)
        k.box((0, -0.68, 0.5), (0.7, 0.03, 0.5), "TB_ClothCream")
        emblem_mark(k, "anvil", (0, -0.7, 0.5), 0.5)
    # striped canopy sloping to the front
    stripes = 8
    for i in range(stripes):
        x = -W / 2 - 0.1 + (i + 0.5) * (W + 0.2) / stripes
        mat = "TB_ClothRed" if i % 2 == 0 else "TB_ClothCream"
        k.box((x, 0, 2.66), ((W + 0.2) / stripes, D + 0.5, 0.05), mat, r=(-14, 0, 0))
        if k.detail:
            k.box((x, -D / 2 - 0.26, 2.4), ((W + 0.2) / stripes, 0.04, 0.24), mat)
    k.box((0, D / 2, 2.9), (W + 0.2, 0.1, 0.1), WD)
    k.box((0, -D / 2, 2.35), (W + 0.2, 0.1, 0.1), WD)
    if k.detail:
        with k.at((-0.8, -0.35, 0.96)):
            sack(k, 0.55)
        with k.at((-0.35, -0.3, 0.96)):
            sack(k, 0.45)
        with k.at((0.5, -0.3, 0.96)):
            bottle(k, "TB_GlassGreen")
        with k.at((0.7, -0.45, 0.96)):
            bottle(k, "TB_GlassBrown", 0.8)
        with k.at((0.9, -0.2, 0.96)):
            lantern(k)
        with k.at((0.7, 0.35, 0.0)):
            crate(k, 0.5)
        with k.at((-1.65, -0.2, 0)):
            sack(k, 0.9)
        with k.at((1.75, -0.5, 0), rz=12):
            crate(k, 0.55)


def well(k):
    """06 Stone Well: ring of stone blocks, two posts, a crank beam with a bucket, a slate roof."""
    R = 0.85
    for course in range(3):
        n = 12
        for i in range(n):
            a = 2 * math.pi * (i + 0.5 * (course % 2)) / n
            k.box((math.cos(a) * R, math.sin(a) * R, 0.17 + course * 0.28), (0.3, 0.44, 0.26), SL if (i + course) % 3 else SM,
                  r=(0, 0, math.degrees(a)))
    k.prism((0, 0, 0.8), 12, R + 0.2, R + 0.2, 0.06, SM)
    k.prism((0, 0, 0.72), 12, R - 0.15, R - 0.15, 0.02, "TB_Water")
    for s in (-1, 1):
        k.box((s * (R + 0.05), 0, 1.5), (0.14, 0.14, 1.9), WM)
    k.prism((0, 0, 1.75), 8, 0.07, 0.07, 2.1, WD, r=(0, 90, 0))
    if k.detail:
        k.box((R + 0.3, 0, 1.75), (0.06, 0.06, 0.3), WD)
        k.box((0, 0, 1.45), (0.02, 0.02, 0.55), "TB_Rope")
        with k.at((0, 0, 0.98)):
            bucket(k, water=False, handle=False)
    for s in (-1, 1):
        for i in range(4):
            y = s * (0.18 + i * 0.2)
            k.box((0, y, 2.62 - i * 0.12), (2.2, 0.24, 0.06), "TB_RoofSlate" if i % 2 else "TB_RoofSlateDark", r=(s * -30, 0, 0))
    k.box((0, 0, 2.7), (2.25, 0.16, 0.12), "TB_RoofSlateDark")


def water_trough(k):
    """06 Water Trough: a long basin of stone blocks with water, a spout pillar at one end."""
    L, W, H = 2.4, 1.0, 0.6
    for s in (-1, 1):
        for i in range(4):
            k.box((-L / 2 + (i + 0.5) * L / 4, s * (W / 2 - 0.1), H / 2), (L / 4 - 0.03, 0.22, H), SL if i % 2 else SM)
        k.box((s * (L / 2 - 0.1), 0, H / 2), (0.22, W - 0.4, H), SM)
    k.box((0, 0, 0.08), (L, W, 0.16), SD)
    k.box((0, 0, H - 0.08), (L - 0.3, W - 0.3, 0.02), "TB_Water")
    stone_stack(k, (L / 2 + 0.1, 0.25, 0), 0.34, 1.35, courses=3)
    k.box((L / 2 + 0.1, 0.25, 1.4), (0.42, 0.42, 0.08), SL)
    k.box((L / 2 - 0.12, 0.25, 1.05), (0.3, 0.06, 0.06), WD)


def stone_bridge(k, length=6.0, width=2.4, rise=0.9):
    """06 Small Stone Bridge: arched deck, parapet walls with capped end pillars, the arch opening underneath."""
    half = length / 2
    n = 10

    def deck(x):
        return 0.35 + rise * (1 - (x / half) ** 2)
    for i in range(n):
        x0, x1 = -half + i * length / n, -half + (i + 1) * length / n
        xm = (x0 + x1) / 2
        ang = math.degrees(math.atan2(deck(x1) - deck(x0), x1 - x0))
        k.box((xm, 0, deck(xm) - 0.12), (x1 - x0 + 0.04, width, 0.24), SM, r=(0, -ang, 0))
        for s in (-1, 1):
            k.box((xm, s * (width / 2 + 0.12), deck(xm) + 0.28), (x1 - x0 + 0.04, 0.26, 0.56), SL if i % 2 else SM, r=(0, -ang, 0))
    # the arch: spandrel blocks either side of an opening
    for s in (-1, 1):
        for i in range(8):
            a0 = math.pi * i / 8
            a1 = math.pi * (i + 1) / 8
            r0 = half * 0.62
            p0 = V(math.cos(a0) * r0, s * (width / 2), math.sin(a0) * rise * 0.9)
            p1 = V(math.cos(a1) * r0, s * (width / 2), math.sin(a1) * rise * 0.9)
            k.span(p0, p1, 0.3, 0.34, SL, up=(0, 1, 0))
        for x in (-1, 1):
            k.box((x * half * 0.82, s * width / 2, (deck(half * 0.82) - 0.1) / 2), (half * 0.42, 0.3, deck(half * 0.82) - 0.1), SM)
    for x in (-half, half):
        for s in (-1, 1):
            stone_stack(k, (x, s * (width / 2 + 0.12), 0), 0.46, 1.0, courses=3)
            k.box((x, s * (width / 2 + 0.12), 1.04), (0.54, 0.54, 0.1), SL)
    k.meta["deck"] = {"half": half, "base": 0.35, "rise": rise, "width": width}


# ---- the smithy's tools --------------------------------------------------------------------------------------------

def anvil(k, stump=True):
    """04/06 Anvil on a stump."""
    z = 0.0
    if stump:
        k.prism((0, 0, 0.3), 9, 0.36, 0.33, 0.6, WM, phase=10)
        k.prism((0, 0, 0.605), 9, 0.3, 0.3, 0.02, WL, phase=10)
        z = 0.6
    k.frustum((0, 0, z + 0.06), 0.52, 0.34, 0.36, 0.22, 0.12, IRON)
    k.frustum((0, 0, z + 0.2), 0.3, 0.16, 0.22, 0.14, 0.16, IRON)
    k.frustum((0, 0, z + 0.36), 0.24, 0.16, 0.56, 0.24, 0.16, IRON)
    k.box((0.04, 0, z + 0.48), (0.66, 0.26, 0.08), IRON)
    k.prism((-0.44, 0, z + 0.47), 6, 0.1, 0.0, 0.34, IRON, r=(0, -90, 0), sy=0.9)
    k.box((0.38, 0, z + 0.44), (0.1, 0.2, 0.14), IRON)


def grindstone(k):
    """06 Grindstone: pale stone wheel in a wooden trestle, crank handle."""
    for s in (-1, 1):
        k.span((s * 0.25, -0.45, 0), (s * 0.25, -0.15, 0.62), 0.08, 0.08, WM)
        k.span((s * 0.25, 0.45, 0), (s * 0.25, 0.15, 0.62), 0.08, 0.08, WM)
        k.box((s * 0.25, 0, 0.62), (0.1, 0.5, 0.1), WM)
    k.box((0, -0.4, 0.2), (0.6, 0.06, 0.06), WD)
    k.box((0, 0.4, 0.2), (0.6, 0.06, 0.06), WD)
    k.prism((0, 0, 0.75), 14, 0.36, 0.36, 0.14, "TB_ClayLight", r=(0, 90, 0))
    k.prism((0, 0, 0.75), 8, 0.04, 0.04, 0.72, IRON, r=(0, 90, 0))
    k.box((0.38, 0, 0.66), (0.04, 0.04, 0.2), IRON)
    k.box((0.42, 0, 0.56), (0.12, 0.05, 0.05), WD)


def tool_rack(k):
    """06 Tool Rack: A-frame trestle, a beam with hammers, tongs and a file hanging from it."""
    for s in (-1, 1):
        k.span((s * 0.7, -0.3, 0), (s * 0.7, 0, 1.45), 0.08, 0.08, WM)
        k.span((s * 0.7, 0.3, 0), (s * 0.7, 0, 1.45), 0.08, 0.08, WM)
    k.box((0, 0, 1.45), (1.6, 0.1, 0.1), WM)
    k.box((0, 0, 0.35), (1.5, 0.06, 0.06), WD)
    for i, tool in enumerate(("hammer", "tongs", "hammer", "tongs", "file")):
        x = -0.5 + i * 0.25
        k.box((x, -0.06, 1.42), (0.03, 0.05, 0.1), IRON)
        if tool == "hammer":
            k.box((x, -0.08, 1.12), (0.04, 0.04, 0.5), WD)
            k.box((x, -0.08, 0.86), (0.08, 0.18, 0.08), IRON)
        elif tool == "tongs":
            for s in (-1, 1):
                k.box((x + s * 0.02, -0.08, 1.1), (0.02, 0.02, 0.56), IRON, r=(0, s * 3, 0))
        else:
            k.box((x, -0.08, 1.15), (0.03, 0.03, 0.4), IRON)
            k.box((x, -0.08, 0.92), (0.04, 0.04, 0.12), WD)


def ingots(k):
    """06 Metal Ingots: a small stack of trapezoid bars."""
    def bar(x, y, z, yaw):
        k.frustum((x, y, z + 0.045), 0.4, 0.16, 0.34, 0.1, 0.09, "TB_MetalLight", r=(0, 0, yaw))
    for i in range(3):
        bar(-0.2 + i * 0.2, -0.05 + (i % 2) * 0.03, 0, 90)
    for i in range(2):
        bar(-0.1 + i * 0.2, 0, 0.09, 90)
    bar(0.3, 0.3, 0, 20)


def coal_bin(k):
    """06 Coal Bin: slant-fronted plank box heaped with coal."""
    k.box((0, 0.15, 0.4), (0.9, 0.5, 0.8), WM)
    k.box((0, -0.25, 0.25), (0.9, 0.35, 0.5), WM)
    k.box((0, -0.43, 0.35), (0.92, 0.04, 0.7), WL, r=(-12, 0, 0))
    for x in (-0.45, 0.45):
        k.box((x, 0, 0.42), (0.06, 0.9, 0.84), WD)
    for i in range(14 if k.detail else 5):
        k.blob((k.r(-0.35, 0.35), k.r(-0.35, 0.3), 0.6 + k.r(-0.05, 0.12)), k.r(0.07, 0.11), "TB_Coal", sub=0, jitter=0.2)


def horseshoe(k):
    for i in range(9):
        a0 = math.radians(-30 + i * 240 / 9)
        a1 = math.radians(-30 + (i + 1) * 240 / 9)
        k.span((math.cos(a0) * 0.1, math.sin(a0) * 0.1, 0.01), (math.cos(a1) * 0.1, math.sin(a1) * 0.1, 0.01), 0.035, 0.02, IRON)


def horseshoes(k):
    for x, y, yaw in ((0, 0, 0), (0.25, 0.1, 40), (0.12, -0.2, 190)):
        with k.at((x, y, 0), rz=yaw):
            horseshoe(k)


def chain(k, links=7):
    for i in range(links):
        with k.at((i * 0.09, 0, 0.03), rx=90 * (i % 2)):
            for a, b in (((-0.05, -0.025), (0.05, -0.025)), ((-0.05, 0.025), (0.05, 0.025)),
                         ((-0.05, -0.025), (-0.05, 0.025)), ((0.05, -0.025), (0.05, 0.025))):
                k.span((a[0], a[1], 0), (b[0], b[1], 0), 0.018, 0.018, IRON)


def bottle(k, mat="TB_GlassGreen", s=1.0):
    k.prism((0, 0, 0.1 * s), 8, 0.06 * s, 0.06 * s, 0.2 * s, mat)
    k.prism((0, 0, 0.23 * s), 8, 0.06 * s, 0.025 * s, 0.06 * s, mat)
    k.prism((0, 0, 0.29 * s), 6, 0.022 * s, 0.022 * s, 0.07 * s, mat)
    k.prism((0, 0, 0.335 * s), 6, 0.02 * s, 0.02 * s, 0.02 * s, WM)


def jug(k, mat="TB_Clay", s=1.0):
    k.prism((0, 0, 0.1 * s), 9, 0.1 * s, 0.16 * s, 0.2 * s, mat)
    k.prism((0, 0, 0.25 * s), 9, 0.16 * s, 0.08 * s, 0.1 * s, mat)
    k.prism((0, 0, 0.33 * s), 9, 0.06 * s, 0.07 * s, 0.07 * s, mat)
    if k.detail:
        k.span((0.14 * s, 0, 0.26 * s), (0.2 * s, 0, 0.18 * s), 0.025 * s, 0.03 * s, mat)
        k.span((0.2 * s, 0, 0.18 * s), (0.14 * s, 0, 0.08 * s), 0.025 * s, 0.03 * s, mat)


def mug(k):
    k.prism((0, 0, 0.07), 8, 0.05, 0.05, 0.14, "TB_MetalLight")
    k.box((0.065, 0, 0.07), (0.03, 0.02, 0.08), "TB_MetalLight")


def nails(k, n=5):
    for i in range(n):
        with k.at((k.r(-0.12, 0.12), k.r(-0.12, 0.12), 0.01), rz=k.r(0, 180)):
            k.box((0, 0, 0), (0.1, 0.012, 0.012), IRON)
            k.box((-0.05, 0, 0), (0.012, 0.03, 0.03), IRON)


def cloth_and_mug(k):
    k.blob((0, 0, 0.1), 0.26, "TB_ClothRed", sub=1, squash=0.45, jitter=0.2, sx=1.2)
    k.blob((0.18, 0.1, 0.06), 0.16, "TB_Sack", sub=1, squash=0.5, jitter=0.2)
    with k.at((0.42, 0, 0)):
        mug(k)


def flower_box(k, w=0.9, flowers=("TB_FlowerWhite",), leaves="TB_Leaf"):
    """04 Flower Box: planked trough on two feet, leafy clump with flowers. Origin at its bottom centre."""
    k.box((0, 0, 0.13), (w, 0.26, 0.22), WM)
    k.box((0, 0, 0.245), (w + 0.04, 0.3, 0.03), WD)
    if not k.detail:
        k.box((0, 0, 0.3), (w - 0.08, 0.24, 0.12), leaves)
        return
    n = max(3, int(w / 0.16))
    for i in range(n):
        x = -w / 2 + (i + 0.5) * w / n
        k.blob((x, k.r(-0.03, 0.03), 0.3), k.r(0.08, 0.1), leaves if i % 2 else "TB_LeafLight", sub=1, squash=0.8)
        if i % 2 == 0 or len(flowers) > 1:
            k.blob((x + k.r(-0.03, 0.03), -0.06, 0.38), 0.045, flowers[i % len(flowers)], sub=0, jitter=0.1)


def potted_plant(k, s=1.0, pot=WM, shrub=True):
    k.prism((0, 0, 0.14 * s), 8, 0.14 * s, 0.18 * s, 0.28 * s, pot)
    k.prism((0, 0, 0.27 * s), 8, 0.19 * s, 0.19 * s, 0.04 * s, WD)
    if shrub:
        k.blob((0, 0, 0.45 * s), 0.2 * s, "TB_Leaf", sub=1, squash=1.0)
        if k.detail:
            k.blob((0.06 * s, -0.04 * s, 0.6 * s), 0.13 * s, "TB_LeafLight", sub=1)


def topiary(k, h=1.1):
    """The small cone shrubs flanking the reference houses."""
    k.prism((0, 0, 0.08), 6, 0.06, 0.05, 0.16, "TB_Bark")
    k.blob((0, 0, h * 0.35), h * 0.3, "TB_Leaf", sub=1, squash=1.2)
    k.blob((0, 0, h * 0.72), h * 0.2, "TB_LeafLight", sub=1, squash=1.3)


def bush_small(k, s=1.0, mat="TB_Leaf", mat2="TB_LeafLight"):
    k.blob((0, 0, 0.28 * s), 0.34 * s, mat, sub=1, squash=0.85)
    if k.detail:
        k.blob((0.22 * s, 0.05 * s, 0.22 * s), 0.24 * s, mat2, sub=1, squash=0.85)
        k.blob((-0.2 * s, -0.06 * s, 0.2 * s), 0.22 * s, mat2, sub=1, squash=0.85)


def grass_tuft(k, s=1.0, mat="TB_Grass"):
    for i in range(5):
        a = i * 72 + k.r(-20, 20)
        with k.at((0, 0, 0), rz=a):
            k.frustum((0.03 * s, 0, 0.12 * s), 0.07 * s, 0.03 * s, 0.01, 0.01, 0.26 * s * k.r(0.8, 1.2), mat, r=(0, k.r(10, 25), 0))


def laundry_line(k, length=1.6, cloths=("TB_ClothCream", "TB_ClothBlue", "TB_ClothCream")):
    for x in (-length / 2, length / 2):
        k.box((x, 0, 1.0), (0.08, 0.08, 2.0), WM)
        k.box((x, 0, 1.95), (0.1, 0.5, 0.06), WM)
    k.box((0, -0.2, 1.93), (length, 0.012, 0.012), "TB_Rope")
    for i, c in enumerate(cloths):
        x = -length / 2 + (i + 1) * length / (len(cloths) + 1)
        k.box((x, -0.2, 1.62), (0.32, 0.03, 0.6 if i % 2 else 0.5), c)
