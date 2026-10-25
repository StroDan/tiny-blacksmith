# Tiny Blacksmith - Tiny_Blacksmith_Castle_Church_Clock_Fountain_Pack/references (sheets 01-04): the city's landmarks.
#   Landmark_Castle      01 "A. Large keep": round towers with tall cone roofs, crenellated curtain walls, a gatehouse
#                        with portcullis between two towers, red fleur banners, a tall keep with its great tower behind.
#   Landmark_Church      02: square west tower with an octagonal spire (dormer, cross), clasping buttresses, arched
#                        door with steps, stone nave with pointed windows and buttresses, low aisles, corner pinnacles,
#                        polygonal apse, a small side chapel.
#   Landmark_ClockHouse  03: stone clock tower (big clock face, bell stage, four corner turrets round a tall spire),
#                        half-timbered hall either side on a stone ground floor, corner turrets, dormers, chimney,
#                        arched door between two red banners.
#   Landmark_Fountain    04: octagonal basin with corner posts and diamond panels, hexagonal centre column with
#                        quatrefoils and lion-head spouts, an arcaded top stage under a red roof and a cross.
# Only the city's shared palette is used (stone, cream plaster, dark timber, the city's red roof tiles, dark slate for
# the castle's cones, red cloth), so the landmarks sit in the same colour family as every house.
# Front faces -Y (Unity turns it to +X); origin = centre of the footprint on the ground.
import math
from tb_lib import V
import tb_arch as A

SL, SM, SD = A.SL, A.SM, A.SD
CREAM, WD, WM, WX = A.CREAM, A.WD, A.WM, A.WX
IRON, GOLD, RED = "TB_MetalDark", "TB_Gold", "TB_ClothRed"
SLATE, SLATE_D = "TB_RoofSlate", "TB_RoofSlateDark"
BLUE, BLUE_D = "TB_RoofBlue", "TB_RoofBlueDark"
TILE, TILE_D = "TB_RoofRed", "TB_RoofRedDark"
WATER, FOAM = "TB_Water", "TB_WaterFoam"


# ---- shared pieces ---------------------------------------------------------------------------------------------

def cone_roof(k, x, y, z, r, h, mat=SLATE_D, n=8, skirt=True, trim=None, finial=None):
    """A steep faceted cone with a flared skirt at its eaves (the reference towers' roof profile)."""
    trim = trim or SD
    if skirt:
        k.prism((x, y, z + 0.25), n, r * 1.12, r * 0.96, 0.5, mat, phase=180 / n)
        k.prism((x, y, z + 0.02), n, r * 1.14, r * 1.14, 0.08, trim, phase=180 / n)
        k.prism((x, y, z + 0.5 + h / 2), n, r * 0.96, 0, h, mat, phase=180 / n)
        top = z + 0.5 + h
    else:
        k.prism((x, y, z + h / 2), n, r, 0, h, mat, phase=180 / n)
        top = z + h
    if finial == "cross":
        cross(k, x, y, top - 0.1, max(0.9, h * 0.14))
    elif finial == "flag":
        flag(k, x, y, top - 0.2, max(2.0, h * 0.25))
    elif finial == "spike":
        k.prism((x, y, top + 0.3), 4, 0.07, 0, 0.8, IRON)
    return top


def cross(k, x, y, z, s):
    k.box((x, y, z + s * 0.55), (0.12 * s, 0.12 * s, s * 1.1), IRON)
    k.box((x, y, z + s * 0.72), (0.6 * s, 0.12 * s, 0.12 * s), IRON)


def flag(k, x, y, z, mast):
    k.box((x, y, z + mast / 2), (0.1, 0.1, mast), IRON)
    k.extrude([(0, 0), (0, -0.8), (1.5, -0.55), (1.2, -0.4), (1.5, -0.25)], 0.04, RED, c=(x + 0.05, y - 0.02, z + mast))


def merlons_ring(k, x, y, z, r, n, mat=None, h=0.85, w=0.75):
    mat = mat or SM
    for i in range(n):
        a = 2 * math.pi * (i + 0.5) / n
        with k.at((x + math.cos(a) * r, y + math.sin(a) * r, z), rz=math.degrees(a) + 90):
            k.box((0, 0, h / 2), (w, 0.55, h), mat)


def outward(k, x, y, r, ang_deg, z):
    """A frame on the surface of a round tower, facing outwards (feature frames look along -Y)."""
    a = math.radians(ang_deg)
    return k.at((x + math.cos(a) * r, y + math.sin(a) * r, z), rz=ang_deg + 90)


def slit(k, w=0.45, h=1.0, lit=True):
    A.window(k, w, h, glass="TB_WindowA" if lit else WX, frame=SD, panes=0, sill=False, arch=True)


def round_tower(k, x, y, r, h, roof_h, roof=SLATE_D, sides=12, windows=(), crenel=True, finial=None, banner=None,
                plinth=1.3, body=None):
    """Round stone tower: dark plinth, body, corbelled parapet band with merlons, cone roof set inside them."""
    body = body or SM
    k.prism((x, y, plinth / 2), sides, r + 0.18, r + 0.12, plinth, SD)
    k.prism((x, y, h / 2), sides, r, r, h, body)
    band = 1.0
    k.prism((x, y, h - band / 2), sides, r + 0.35, r + 0.35, band, SL)
    k.prism((x, y, h - band - 0.2), sides, r + 0.05, r + 0.33, 0.4, SL)   # the corbels' slope
    if crenel:
        merlons_ring(k, x, y, h, r + 0.18, max(6, int(r * 2.6)))
    if k.detail:
        k.prism((x, y, h * 0.55), sides, r + 0.04, r + 0.04, 0.18, SL)    # a string course half way up
    for (z, ang) in windows:
        with outward(k, x, y, r, ang, z):
            slit(k)
    if banner:
        z, ang, bh = banner
        with outward(k, x, y, r + 0.02, ang, z):
            A.wall_banner(k, min(1.1, r * 0.5), bh, RED, "fleur")
    top = h
    if roof_h > 0:
        top = cone_roof(k, x, y, h + 0.15, r + 0.1, roof_h, roof, n=max(8, sides - 2), finial=finial)
    return top


def square_tower(k, x, y, w, d, h, roof_h=0.0, roof=SLATE_D, windows=(), crenel=True, finial=None, plinth=1.3, body=None):
    body = body or SM
    k.box((x, y, plinth / 2), (w + 0.3, d + 0.3, plinth), SD)
    k.box((x, y, h / 2), (w, d, h), body)
    k.box((x, y, h - 0.5), (w + 0.6, d + 0.6, 1.0), SL)
    if crenel:
        for (sx, sy, L, horiz) in ((0, -d / 2 - 0.15, w + 0.6, True), (0, d / 2 + 0.15, w + 0.6, True),
                                   (-w / 2 - 0.15, 0, d + 0.6, False), (w / 2 + 0.15, 0, d + 0.6, False)):
            n = max(2, int(L / 1.5))
            for i in range(n):
                t = -L / 2 + (i + 0.5) * L / n
                c = (x + (t if horiz else sx), y + (sy if horiz else t), h + 0.42)
                k.box(c, (0.75 if horiz else 0.5, 0.5 if horiz else 0.75, 0.85), SM)
    for (face, s, z) in windows:
        F = A.faces(x - w / 2, x + w / 2, y - d / 2, y + d / 2)[face]
        with k.frame(F.M(s, z)):
            slit(k)
    if roof_h > 0:
        return cone_roof(k, x, y, h + 0.15, max(w, d) * 0.62, roof_h, roof, n=4, finial=finial)
    return h


def wall_run(k, a, b, h, t=2.4, stones=False, windows=()):
    """A crenellated curtain wall from a to b (2D, seen from outside a->b runs left->right) with a dark plinth."""
    ax, ay = a
    bx, by = b
    L = math.hypot(bx - ax, by - ay)
    ang = math.degrees(math.atan2(by - ay, bx - ax))
    with k.at(((ax + bx) / 2, (ay + by) / 2, 0), rz=ang):
        k.box((0, 0, h / 2), (L, t, h), SM)
        k.box((0, -0.1, 0.65), (L, t + 0.3, 1.3), SD)
        k.box((0, 0, h - 0.3), (L, t + 0.35, 0.6), SL)
        n = max(2, int(L / 1.6))
        for i in range(n):
            x = -L / 2 + (i + 0.5) * L / n
            k.box((x, -t / 2 + 0.1, h + 0.42), (0.8, 0.55, 0.85), SM)
        if stones:
            F = A.Face((-L / 2, -t / 2, 0), (1, 0, 0), L)
            A.stones(k, F, 0.2, L - 0.2, 1.3, h - 0.6, mats=(SL, SM, SM), course=(0.55, 0.75), block=(0.9, 1.7), out=0.06)
        for (s, z) in windows:
            with k.at((-L / 2 + s, -t / 2, z)):
                slit(k, 0.4, 0.9)


# ---- A. the castle (01 "Large keep") -------------------------------------------------------------------------------

def landmark_castle(k):
    """Seen mostly from far off: the silhouette (towers, steep cones, battlements) carries it, the detail is sparse.
    Built in its own warm cream stone (the menu art's sunlit castle) - the city's grey stone would read as a ruin
    against the grey rock of the mountains behind it. Red cones on the front and the crown, blue slate on the halls."""
    global SL, SM, SD
    saved = (SL, SM, SD)
    SL, SM, SD = "TB_CastleStoneLight", "TB_CastleStone", "TB_CastleStoneDark"
    try:
        _castle(k)
    finally:
        SL, SM, SD = saved


def _castle(k):
    X0, X1, Y0, Y1, WH = -22.0, 22.0, -16.0, 20.0, 12.0
    # Curtain walls (the gate in the middle of the front).
    wall_run(k, (X0, Y0), (-5.5, Y0), WH, stones=True, windows=((6.0, 8.5), (11.0, 8.5)))
    wall_run(k, (5.5, Y0), (X1, Y0), WH, stones=True, windows=((5.5, 8.5), (10.5, 8.5)))
    wall_run(k, (X1, Y0), (X1, Y1), WH)
    wall_run(k, (X1, Y1), (X0, Y1), WH)
    wall_run(k, (X0, Y1), (X0, Y0), WH)
    # Red fleur banners hung down the front curtain walls, either side of the gate (the menu art's long banners).
    for x in (-13.5, 13.5):   # between the wall's arrow slits, clear of the gate towers
        with k.at((x, Y0 - 1.26, WH - 0.9)):
            A.wall_banner(k, 1.1, 4.2, RED, "fleur")
    # Corner towers - the front two bigger, red-roofed like the gate, with banners; the back two blue slate.
    round_tower(k, X0, Y0, 3.8, 19.0, 11.5, roof=TILE, windows=((8, 250), (13, 250), (13, 200)), banner=(15.5, 255, 4.0), finial="flag")
    round_tower(k, X1, Y0, 3.8, 19.0, 11.5, roof=TILE, windows=((8, 290), (13, 290), (13, 340)), banner=(15.5, 285, 4.0), finial="flag")
    round_tower(k, X0, Y1, 3.4, 17.0, 9.0, roof=BLUE, windows=((12, 180),), finial="spike")
    round_tower(k, X1, Y1, 3.4, 17.0, 9.0, roof=BLUE, windows=((12, 0),), finial="spike")
    # Side towers half way along.
    square_tower(k, X0, 2.0, 6.0, 6.0, 16.5, 7.0, roof=BLUE, windows=(("left", 3.0, 11.0), ("front", 3.0, 11.0)), finial="spike")
    square_tower(k, X1, 2.0, 6.0, 6.0, 16.5, 7.0, roof=BLUE, windows=(("right", 3.0, 11.0), ("front", 3.0, 11.0)), finial="spike")

    # Gatehouse: a crenellated block between two round towers, the arched gate with its portcullis and banners.
    gy = Y0 - 1.0
    k.box((0, gy + 1.0, 8.0), (11.0, 7.0, 16.0), SM)
    k.box((0, gy + 1.0, 0.65), (11.4, 7.3, 1.3), SD)
    k.box((0, gy + 1.0, 15.6), (11.6, 7.6, 0.8), SL)
    for i in range(7):
        x = -5.4 + (i + 0.5) * 10.8 / 7
        k.box((x, gy - 2.55, 16.45), (0.8, 0.55, 0.9), SM)
    Fg = A.Face((-5.5, gy - 2.5, 0), (1, 0, 0), 11.0)
    A.stones(k, Fg, 0.3, 10.7, 1.3, 15.2, holes=[(5.5, 0, 5.2, 7.4)], mats=(SL, SM, SM), course=(0.5, 0.7), block=(0.8, 1.5), out=0.06)
    with k.frame(Fg.M(5.5, 0)):
        A.door(k, 4.2, 6.6, arch=True, stone_arch=True, leaf=WX, ring=False)
        if k.detail:
            for i in range(7):   # the portcullis
                k.box((-1.8 + i * 0.6, -0.14, 2.7), (0.1, 0.08, 5.4), IRON)
            for z in (1.0, 2.4, 3.8, 5.0):
                k.box((0, -0.15, z), (3.9, 0.08, 0.1), IRON)
        for s in (-1, 1):
            with k.at((s * 3.6, 0, 13.4)):
                A.wall_banner(k, 1.0, 3.6, RED, "fleur")
        for s in (-1, 1):
            with k.at((s * 1.4, 0, 11.0)):
                slit(k, 0.5, 1.1)
    for s in (-1, 1):
        round_tower(k, s * 6.8, gy, 3.1, 18.0, 10.0, roof=TILE, windows=((9, 270), (14, 270)), banner=(12.5, 270 + s * 30, 3.2), finial="flag")
    k.meta["door"] = list(k.world((0, gy - 3.5, 0)))

    # A great hall behind the gate: dark gable roof (the dark roof masses of the reference).
    k.box((0, -4.0, 6.5), (13.0, 8.0, 13.0), SM)
    k.wedge((0, -4.0, 13.0), 14.0, 8.8, 5.2, BLUE_D, r=(0, 0, 90))
    for x in (-4.0, 0.0, 4.0):
        with k.at((x, -8.0, 9.5)):
            slit(k, 0.7, 1.4)

    # The keep: a tall crenellated block with turrets, the great round tower and a square tower with a corbelled turret.
    kx, ky, KW, KD, KH = 0.0, 8.0, 18.0, 13.0, 21.0
    k.box((kx, ky, KH / 2), (KW, KD, KH), SM)
    k.box((kx, ky, 0.65), (KW + 0.3, KD + 0.3, 1.3), SD)
    k.box((kx, ky, KH - 0.4), (KW + 0.6, KD + 0.6, 0.8), SL)
    for i in range(10):
        x = kx - KW / 2 + (i + 0.5) * KW / 10
        k.box((x, ky - KD / 2 - 0.15, KH + 0.42), (0.8, 0.5, 0.85), SM)
    for fl in range(3):
        for x in (-6.0, -2.0, 2.0, 6.0):
            with k.at((kx + x, ky - KD / 2, 7.5 + fl * 4.5)):
                slit(k, 0.6, 1.3)
    for sx in (-1, 1):
        for sy in (-1, 1):
            round_tower(k, kx + sx * KW / 2, ky + sy * KD / 2, 2.1, KH + 8.0, 7.5, roof=TILE if sy < 0 else BLUE, sides=10, plinth=0.0, finial="flag" if sy < 0 else "spike",
                        windows=((KH + 2.0, 270 if sy < 0 else 90),))
    # a steep blue slate hip roof over the keep, inside its battlements (the menu art's dark-blue roof masses)
    k.frustum((kx, ky, KH + 0.1 + 4.0), KW - 1.6, KD - 1.6, KW - KD, 0.2, 8.0, BLUE)
    # The great tower - the castle's crown, tallest and red-roofed so it reads against the mountains.
    round_tower(k, 5.0, 11.5, 4.6, 39.0, 15.0, roof=TILE, sides=14, plinth=0.0, finial="flag",
                windows=((24, 270), (28.5, 270), (33, 270), (24, 200), (28.5, 330)), banner=(35.5, 270, 3.8))
    square_tower(k, -6.0, 5.5, 6.4, 6.4, 29.0, 10.0, roof=BLUE_D, plinth=0.0, finial="flag",
                 windows=(("front", 3.2, 23.0), ("front", 3.2, 26.5), ("left", 3.2, 25.0)))
    # Two slimmer towers rising behind the keep, so the crown of cones stays busy from any side.
    round_tower(k, -13.0, 16.0, 2.6, 27.0, 9.0, roof=TILE, sides=10, windows=((20, 270), (24, 180)), finial="flag")
    round_tower(k, 14.5, 16.5, 2.4, 26.0, 9.0, roof=TILE, sides=10, finial="flag", windows=((19, 0),))
    # the corbelled turret clinging to its side, up high
    k.prism((-9.6, 5.5, 22.5), 10, 0.4, 1.3, 1.6, SL)
    k.prism((-9.6, 5.5, 27.3), 10, 1.3, 1.3, 8.0, SM)
    merlons_ring(k, -9.6, 5.5, 31.3, 1.45, 6)
    cone_roof(k, -9.6, 5.5, 31.4, 1.45, 5.0, BLUE, finial="spike")
    k.meta.update(floors=5, style="castle", ridge=55.0, walls=[X0 - 4.0, X1 + 4.0, Y0 - 4.5, Y1 + 4.0])


# ---- B. the church (02) ----------------------------------------------------------------------------------------

def pinnacle(k, x, y, z0, h=2.6, r=0.55, roof_h=1.9):
    k.prism((x, y, z0 + h / 2), 8, r, r, h, SM, phase=22.5)
    k.prism((x, y, z0 + h), 8, r + 0.1, r + 0.1, 0.14, SL, phase=22.5)
    cone_roof(k, x, y, z0 + h + 0.07, r + 0.08, roof_h, TILE_D, n=8, skirt=False, finial="cross")


def buttress(k, f, s, h, depth=0.9, w=0.75):
    """A stepped buttress against a wall frame (x along the wall, -y out of it)."""
    with k.frame(f.M(s, 0)):
        k.box((0, -depth / 2, h * 0.3), (w, depth, h * 0.6), SL)
        k.box((0, -depth * 0.35, h * 0.75), (w * 0.9, depth * 0.7, h * 0.3), SL)
        k.frustum((0, -depth * 0.2, h * 0.95), w * 0.9, depth * 0.4, w * 0.9, 0.05, h * 0.1, SM)


def landmark_church(k):
    NX, NY0, NY1, NH = 4.6, 0.0, 14.0, 8.2
    # Nave: coursed stone, tall pointed windows, buttresses between them.
    wins = [(s, 3.6) for s in (3.0, 7.0, 11.0)]
    F = A.storey(k, (-NX, NX, NY0, NY1), 0.0, NH, [
        ("right", "win", NY1 - s, dict(zc=z, w=1.0, h=2.4, arch=True, panes=2, frame=SL, glass="TB_WindowA")) for s, z in wins] + [
        ("left", "win", s, dict(zc=z, w=1.0, h=2.4, arch=True, panes=2, frame=SL, glass="TB_WindowB")) for s, z in wins],
        kind="stone")
    for s in (1.0, 5.0, 9.0, 13.0):
        buttress(k, F["left"], s, 6.0)
        buttress(k, F["right"], NY1 - s, 6.0)
    A.gable_roof(k, -NX, NX, NY0 - 0.2, NY1 + 0.2, NH, 5.6, "y", (TILE_D, TILE), over=0.45, gable_over=0.3, wall=SM,
                 gable_timber=False, barge=SD)
    # Low aisles either side of the tower front, lean-to roofs.
    for s in (-1, 1):
        ax = s * (NX + 1.1)
        k.box((ax, 6.25, 2.3), (2.2, 12.5, 4.6), SM)
        k.box((ax, 6.25, 0.4), (2.4, 12.7, 0.8), SD)
        k.box((ax + s * 0.15, 6.25, 5.05), (2.9, 13.1, 0.22), TILE_D, r=(0, s * 28, 0))
        for y in (2.5, 6.5, 10.5):
            with k.at((ax + s * 1.1, y, 2.4), rz=-90 if s < 0 else 90):
                A.window(k, 0.55, 1.2, glass="TB_WindowA", frame=SL, panes=0, sill=False, arch=True)
        with k.at((ax, -0.0, 2.4)):
            A.window(k, 0.55, 1.2, glass="TB_WindowB", frame=SL, panes=0, sill=False, arch=True)
        pinnacle(k, s * (NX + 2.0), -0.1, 4.6, 1.8, 0.45, 1.5)
    # Pinnacles on the nave's front corners (either side of the tower, as in the front view).
    for s in (-1, 1):
        pinnacle(k, s * (NX - 0.5), -0.1, NH, 3.0, 0.55, 2.2)

    # The west tower: square, stone, clasping buttresses, the arched door with steps, window, belfry, spire.
    TW, TY0, TY1, TH = 5.0, -5.0, 0.4, 15.5
    T = A.storey(k, (-TW / 2, TW / 2, TY0, TY1), 0.0, TH, [
        ("front", "door", TW / 2, dict(w=1.9, h=3.4, z=0.54, steps=3, main=True, stone_arch=True, leaf=WD)),
        ("front", "win", TW / 2, dict(zc=7.8, w=0.9, h=2.2, arch=True, panes=2, frame=SL, glass="TB_WindowA")),
        ("left", "win", 2.7, dict(zc=7.8, w=0.7, h=1.6, arch=True, panes=0, frame=SL)),
        ("right", "win", 2.7, dict(zc=7.8, w=0.7, h=1.6, arch=True, panes=0, frame=SL)),
    ], kind="stone")
    for fn in ("front", "left", "right", "back"):
        f = T[fn]
        with k.frame(f.M(f.L / 2, 12.4)):   # the belfry: louvred arched opening
            k.extrude([(-0.7, -1.3), (0.7, -1.3), (0.7, 0.5), (0.35, 1.05), (0, 1.25), (-0.35, 1.05), (-0.7, 0.5)], 0.08, WX, c=(0, -0.02, 0))
            if k.detail:
                for z in (-0.9, -0.45, 0.0, 0.45):
                    k.box((0, -0.08, z), (1.3, 0.06, 0.12), WD, r=(25, 0, 0))
            for sx in (-0.85, 0.85):
                k.box((sx, -0.07, -0.1), (0.2, 0.14, 2.6), SL)
    for sx in (-1, 1):   # clasping buttresses on the front corners, stepping back up the tower
        for sy in (TY0,):
            k.box((sx * (TW / 2 + 0.25), sy - 0.25, 3.2), (1.0, 1.0, 6.4), SL)
            k.box((sx * (TW / 2 + 0.15), sy - 0.15, 8.4), (0.8, 0.8, 4.0), SL)
            k.frustum((sx * (TW / 2 + 0.1), sy - 0.1, 10.9), 0.7, 0.7, 0.3, 0.3, 1.0, SM)
    k.box((0, (TY0 + TY1) / 2, TH + 0.2), (TW + 0.5, TY1 - TY0 + 0.5, 0.4), SL)          # cornice
    cy = (TY0 + TY1) / 2
    top = cone_roof(k, 0, cy, TH + 0.4, TW * 0.56, 10.0, TILE_D, n=8, finial="cross")
    with k.at((0, TY0 + 0.5, TH + 1.4)):   # the little gabled dormer on the spire's front
        k.box((0, -0.3, 0.5), (1.0, 1.0, 1.0), SM)
        k.wedge((0, -0.3, 1.0), 1.25, 1.2, 0.8, TILE_D)
        with k.at((0, -0.82, 0.55)):
            A.window(k, 0.35, 0.55, glass=WX, frame=SL, panes=0, sill=False, arch=True)

    # The apse at the east end: half an octagon with its own conical roof, pointed windows, little pinnacles.
    AR = 4.3
    k.prism((0, NY1, NH / 2 - 0.4), 8, AR, AR, NH - 0.8, SM, phase=22.5)
    k.prism((0, NY1, 0.4), 8, AR + 0.15, AR + 0.15, 0.8, SD, phase=22.5)
    cone_roof(k, 0, NY1, NH - 0.8, AR + 0.2, 4.6, TILE_D, n=8, finial="cross")
    for ang in (45, 90, 135):
        with outward(k, 0, NY1, AR * math.cos(math.radians(22.5)), ang, 3.6):
            A.window(k, 0.8, 2.2, glass="TB_WindowA", frame=SL, panes=2, sill=False, arch=True)
    for sx in (-1, 1):
        pinnacle(k, sx * (AR + 0.3), NY1 - 1.8, NH - 1.0, 2.0, 0.45, 1.6)

    # A small side chapel on the right, with its own gable and cross (the 3/4 view).
    C = A.storey(k, (NX + 2.2, NX + 4.6, 3.0, 7.0), 0.0, 4.0, [
        ("right", "win", 2.0, dict(zc=2.1, w=0.6, h=1.4, arch=True, panes=0, frame=SL)),
        ("front", "win", 1.2, dict(zc=2.1, w=0.5, h=1.3, arch=True, panes=0, frame=SL)),
    ], kind="stone")
    A.gable_roof(k, NX + 2.2, NX + 4.6, 3.0, 7.0, 4.0, 2.2, "y", (TILE_D, TILE), over=0.3, gable_over=0.25, wall=SM, gable_timber=False, barge=SD)
    cross(k, NX + 3.4, 2.8, 6.2, 0.8)
    k.meta.update(floors=3, style="church", ridge=top, walls=[-NX - 2.4, NX + 4.6, TY0 - 1.1, NY1 + AR])


# ---- C. the clock house (03) -----------------------------------------------------------------------------------

def clock_face(k, r=1.55):
    """A big cream clock face on a stone surround (frame on the wall, facing -Y): hour marks and two hands."""
    k.box((0, -0.06, 0), (r * 2 + 0.7, 0.14, r * 2 + 0.7), SL)
    k.prism((0, -0.16, 0), 16, r + 0.18, r + 0.18, 0.12, SD, r=(90, 0, 0))
    k.prism((0, -0.23, 0), 16, r, r, 0.08, CREAM, r=(90, 0, 0))
    for i in range(12):
        a = math.radians(i * 30)
        L = 0.34 if i % 3 == 0 else 0.2
        k.box((math.sin(a) * (r - 0.3), -0.28, math.cos(a) * (r - 0.3)), (0.08, 0.03, L), IRON, r=(0, i * 30, 0))
    k.prism((0, -0.31, 0), 8, 0.12, 0.12, 0.06, IRON, r=(90, 0, 0))
    for ang, L, w in ((-60, r * 0.55, 0.12), (65, r * 0.82, 0.08)):   # about ten past ten, like the sheet
        a = math.radians(ang)
        k.box((math.sin(a) * L / 2, -0.32, math.cos(a) * L / 2), (w, 0.03, L), IRON, r=(0, ang, 0))


def landmark_clock_house(k):
    BX, BY0, BY1 = 6.8, -1.0, 8.0
    # The hall: stone ground floor with arched windows, half-timbered cream upper floor, a big red roof.
    G = A.storey(k, (-BX, BX, BY0, BY1), 0.0, 3.8, [
        ("front", "win", 1.6, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("front", "win", 2 * BX - 1.6, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("left", "win", 3.0, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("left", "win", 6.5, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("right", "win", 3.0, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("right", "win", 6.5, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("back", "win", 3.5, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("back", "win", 10.1, dict(zc=1.9, w=0.7, h=1.3, arch=True, panes=2)),
        ("back", "door", 6.8, dict(w=1.0, h=2.1)),
    ], kind="stone")
    A.floor_band(k, -BX, BX, BY0, BY1, 3.85)
    U = A.storey(k, (-BX - 0.2, BX + 0.2, BY0 - 0.2, BY1 + 0.2), 3.95, 7.2, [
        ("front", "win", 2.0, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("front", "win", 2 * BX + 0.4 - 2.0, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("left", "win", 2.6, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("left", "win", 6.8, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("right", "win", 2.6, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("right", "win", 6.8, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("back", "win", 3.5, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("back", "win", 7.2, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
        ("back", "win", 10.9, dict(zc=1.7, w=0.7, h=1.2, arch=True)),
    ])
    roof = A.gable_roof(k, -BX - 0.2, BX + 0.2, BY0 - 0.2, BY1 + 0.2, 7.2, 4.6, "x", "red", over=0.55, gable_over=0.4,
                        gable_windows=((0.55, 0.75), (0.55, 0.75)))
    for x in (-4.3, 4.3):
        with k.at((x, BY0 + 1.1, roof.z_at(x, BY0 + 1.1) - 0.3)):
            A.dormer(k, 1.0, 1.0, 1.4, "red")
    A.chimney(k, 3.6, BY1 - 1.6, roof.z_at(3.6, BY1 - 1.6) - 0.5, 14.2, w=0.95)
    # Round corner turrets on the hall's front corners, cone roofs rising past the eaves.
    for s in (-1, 1):
        x, y = s * (BX + 0.25), BY0 - 0.25
        k.prism((x, y, 0.5), 10, 1.15, 1.05, 1.0, SD)
        k.prism((x, y, 4.9), 10, 1.0, 1.0, 9.8, SL)
        k.prism((x, y, 9.9), 10, 1.12, 1.12, 0.2, SD)
        with outward(k, x, y, 1.0, 270 + s * 30, 6.2):
            slit(k, 0.35, 0.8)
        cone_roof(k, x, y, 10.0, 1.1, 3.4, TILE_D, n=10, finial="spike")

    # The clock tower, standing forward of the hall's front.
    TW, TY0, TY1 = 4.6, BY0 - 1.8, BY1 - 5.0
    TH = 17.0
    cy = (TY0 + TY1) / 2
    T = A.storey(k, (-TW / 2, TW / 2, TY0, TY1), 0.0, 5.0, [
        ("front", "door", TW / 2, dict(w=1.6, h=2.9, z=0.36, steps=2, main=True, stone_arch=True, leaf=WD)),
    ], kind="stone")
    A.quoins(k, T["front"], 0, 5.0)
    # Upper shaft: plaster panels framed by stone quoins (the sheet's stone + plaster tower).
    k.box((0, cy, 5.0 + (TH - 5.0) / 2), (TW, TY1 - TY0, TH - 5.0), CREAM)
    for fn in ("front", "left", "right", "back"):
        A.quoins(k, A.faces(-TW / 2, TW / 2, TY0, TY1)[fn], 5.0, TH)
    A.floor_band(k, -TW / 2, TW / 2, TY0, TY1, 5.05, mat=SL, h=0.3, over=0.1)
    TF = A.faces(-TW / 2, TW / 2, TY0, TY1)
    for fn in ("front", "left", "right"):
        with k.frame(TF[fn].M(TW / 2 if fn == "front" else (TY1 - TY0) / 2, 7.6)):
            A.window(k, 0.6, 1.3, glass="TB_WindowA", frame=SL, panes=2, sill=False, arch=True)
        with k.frame(TF[fn].M(TW / 2 if fn == "front" else (TY1 - TY0) / 2, 12.3)):
            clock_face(k, 1.5)
    with k.frame(TF["back"].M((TW) / 2, 11.0)):
        A.window(k, 0.6, 1.3, glass="TB_WindowB", frame=SL, panes=2, sill=False, arch=True)
    # Banners either side of the door.
    for s in (-1.35, 1.35):
        with k.frame(T["front"].M(TW / 2 + s * 1.0, 4.4)):
            A.wall_banner(k, 0.75, 2.2, RED, "fleur")
    # Bell stage: a corbelled band, the open arches with the bell, corner turrets and the tall spire.
    k.box((0, cy, TH + 0.2), (TW + 0.7, TY1 - TY0 + 0.7, 0.4), SL)
    k.frustum((0, cy, TH - 0.25), TW, TY1 - TY0, TW + 0.7, TY1 - TY0 + 0.7, 0.5, SL)
    BH = 3.6
    bw = TW - 0.4
    k.box((0, cy, TH + 0.4 + BH / 2), (bw, TY1 - TY0 - 0.4, BH), SM)
    BF = A.faces(-bw / 2, bw / 2, TY0 + 0.2, TY1 - 0.2)
    for fn in ("front", "left", "right", "back"):
        f = BF[fn]
        with k.frame(f.M(f.L / 2, TH + 0.9)):
            k.extrude([(-0.8, 0), (0.8, 0), (0.8, 1.7), (0.4, 2.3), (0, 2.5), (-0.4, 2.3), (-0.8, 1.7)], 0.08, WX, c=(0, -0.02, 0))
    k.prism((0, cy, TH + 2.2), 10, 0.2, 0.7, 1.2, IRON)          # the bell
    k.prism((0, cy, TH + 1.55), 10, 0.72, 0.72, 0.12, GOLD)
    k.box((0, cy, TH + 0.4 + BH + 0.2), (bw + 0.5, TY1 - TY0 + 0.1, 0.4), SL)
    stage_top = TH + 0.4 + BH + 0.4
    for sx in (-1, 1):
        for sy in (TY0 + 0.1, TY1 - 0.1):
            x = sx * (bw / 2 + 0.05)
            k.prism((x, sy, stage_top - 1.2), 8, 0.5, 0.5, 2.4, SL)
            k.prism((x, sy, stage_top - 2.55), 8, 0.2, 0.5, 0.3, SL)
            cone_roof(k, x, sy, stage_top, 0.55, 2.4, TILE_D, n=8, skirt=False, finial="spike")
    top = cone_roof(k, 0, cy, stage_top - 0.1, bw * 0.6, 7.5, TILE_D, n=8, finial="spike")
    k.meta.update(floors=3, style="civic", ridge=top, walls=[-BX - 1.3, BX + 1.3, TY0 - 1.0, BY1 + 0.2])


# ---- D. the fountain (04) --------------------------------------------------------------------------------------

def lion_head(k, x, y, z, ang):
    """A low-poly lion mask on the column, facing outwards (ang degrees)."""
    with k.at((x, y, z), rz=ang + 90):
        k.blob((0, -0.08, 0), 0.34, SL, sub=1, squash=1.0, jitter=0.1, sx=1.1)        # the mane
        k.box((0, -0.3, -0.02), (0.26, 0.28, 0.3), SM)                                 # the muzzle
        k.box((0, -0.42, -0.12), (0.16, 0.1, 0.1), SD)                                 # the mouth
        for sx in (-0.09, 0.09):
            k.box((sx, -0.36, 0.1), (0.05, 0.04, 0.05), SD)                             # eyes


def water_arc(k, x0, y0, z0, dx, dy, reach, drop, w=0.22):
    """A curving jet from a spout, falling into the basin (a chain of short flat spans), foam where it lands."""
    pts = []
    for i in range(9):
        t = i / 8
        pts.append((x0 + dx * reach * t, y0 + dy * reach * t, z0 + 0.25 * math.sin(t * math.pi * 0.6) - drop * t * t))
    for a, b in zip(pts, pts[1:]):
        k.span(a, b, w, 0.1, WATER, up=(0, 0, 1))
    end = pts[-1]
    if k.detail:
        k.prism((end[0], end[1], end[2] + 0.03), 8, 0.32, 0.22, 0.06, FOAM)
        k.blob((end[0], end[1], end[2] + 0.05), 0.14, FOAM, sub=1, squash=0.6)


def landmark_fountain(k):
    R = 3.0                      # basin circumradius (octagon)
    ap = R * math.cos(math.pi / 8)
    side = 2 * R * math.sin(math.pi / 8)
    WH, T = 0.8, 0.42
    # Basin floor + water.
    k.prism((0, 0, 0.08), 8, R, R, 0.16, SD, phase=22.5)
    k.prism((0, 0, WH - 0.14), 8, ap - T + 0.05, ap - T + 0.05, 0.05, WATER, phase=22.5)
    for i in range(8):
        a = math.radians(i * 45)
        with k.at((math.cos(a) * (ap - T / 2), math.sin(a) * (ap - T / 2), 0), rz=math.degrees(a) + 90):
            k.box((0, 0, WH / 2), (side + 0.02, T, WH), SM)
            k.box((0, 0, 0.1), (side + 0.12, T + 0.12, 0.2), SD)                  # the base course
            k.box((0, 0, WH + 0.06), (side + 0.1, T + 0.14, 0.12), SL)            # the coping
            if k.detail:   # a raised panel with a diamond, like the sheet
                k.box((0, -T / 2 - 0.02, WH * 0.48), (side * 0.62, 0.05, WH * 0.5), SL)
                k.box((0, -T / 2 - 0.05, WH * 0.48), (0.22, 0.04, 0.22), SD, r=(0, 45, 0))
    for i in range(8):             # corner posts with pyramid caps
        a = math.radians(i * 45 + 22.5)
        x, y = math.cos(a) * R, math.sin(a) * R
        with k.at((x, y, 0), rz=math.degrees(a)):
            k.box((0, 0, 0.62), (0.52, 0.52, 1.24), SL)
            k.box((0, 0, 1.28), (0.62, 0.62, 0.1), SM)
            k.frustum((0, 0, 1.47), 0.58, 0.58, 0.02, 0.02, 0.28, SL)

    # The centre column: plinth, hexagonal shaft with quatrefoils, lion-head spouts.
    k.prism((0, 0, 0.45), 8, 1.1, 1.0, 0.7, SD, phase=22.5)
    k.prism((0, 0, 0.83), 8, 1.02, 1.02, 0.1, SL, phase=22.5)
    SH0, SH1, SR = 0.88, 3.1, 0.8
    k.prism((0, 0, (SH0 + SH1) / 2), 6, SR, SR, SH1 - SH0, SM, phase=30)
    k.prism((0, 0, SH1 + 0.08), 6, SR + 0.16, SR + 0.16, 0.16, SL, phase=30)
    for i in range(6):   # quatrefoil windows on every face (dark, four lobes)
        a = math.radians(i * 60)
        with k.at((math.cos(a) * SR * 0.87, math.sin(a) * SR * 0.87, 1.7), rz=math.degrees(a) + 90):
            k.box((0, -0.01, 0), (0.46, 0.04, 0.46), SL)
            for dx, dz in ((0.09, 0), (-0.09, 0), (0, 0.09), (0, -0.09)):
                k.prism((dx, -0.04, dz), 8, 0.09, 0.09, 0.03, WX, r=(90, 0, 0))
    for i in range(4):   # the lions and their jets, one to each side
        ang = 45 + i * 90
        a = math.radians(ang)
        lion_head(k, math.cos(a) * (SR * 0.86), math.sin(a) * (SR * 0.86), 2.55, ang)
        water_arc(k, math.cos(a) * (SR + 0.45), math.sin(a) * (SR + 0.45), 2.43, math.cos(a), math.sin(a), 1.0, 1.78)

    # The arcaded top stage with pointed openings, a cornice, the red roof and the cross.
    ST0, ST1, TR = SH1 + 0.16, SH1 + 1.7, 0.66
    k.prism((0, 0, (ST0 + ST1) / 2), 6, TR, TR, ST1 - ST0, SL, phase=30)
    for i in range(6):
        a = math.radians(i * 60)
        with k.at((math.cos(a) * TR * 0.87, math.sin(a) * TR * 0.87, ST0 + 0.25), rz=math.degrees(a) + 90):
            k.extrude([(-0.2, 0), (0.2, 0), (0.2, 0.7), (0.1, 0.92), (0, 1.0), (-0.1, 0.92), (-0.2, 0.7)], 0.05, WX, c=(0, -0.02, 0))
    for i in range(6):   # little shafts between the arches
        a = math.radians(i * 60 + 30)
        k.box((math.cos(a) * TR, math.sin(a) * TR, (ST0 + ST1) / 2), (0.14, 0.14, ST1 - ST0), SM)
    k.prism((0, 0, ST1 + 0.07), 6, TR + 0.18, TR + 0.18, 0.14, SL, phase=30)
    top = cone_roof(k, 0, 0, ST1 + 0.1, TR + 0.2, 1.45, TILE_D, n=6, skirt=False)
    k.prism((0, 0, top + 0.05), 6, 0.1, 0.1, 0.1, SL, phase=30)
    cross(k, 0, 0, top + 0.05, 0.75)
    k.meta.update(style="fountain", ridge=top + 0.9)


LANDMARKS = [("Landmark_Castle", landmark_castle, "building"), ("Landmark_Church", landmark_church, "building"),
             ("Landmark_ClockHouse", landmark_clock_house, "building"), ("Landmark_Fountain", landmark_fountain, "prop")]
