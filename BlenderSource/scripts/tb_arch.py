# Tiny Blacksmith - architecture kit (walls, timber framing, stonework, windows, doors, roofs, dormers, chimneys,
# balconies, awnings, lanterns, banners, ivy, steps). Reference language: sheets 01-04.
#
# Feature frames: x along the wall (left -> right seen from outside), y INTO the wall (negative = sticking out),
# z up. Origin on the wall surface.
import math
from mathutils import Matrix
from tb_lib import V
import tb_props as P

WL, WM, WD, WX = "TB_WoodLight", "TB_WoodMedium", "TB_WoodDark", "TB_WoodDarker"
SL, SM, SD = "TB_StoneLight", "TB_StoneMedium", "TB_StoneDark"
CREAM, BEIGE = "TB_PlasterCream", "TB_PlasterBeige"
IRON = "TB_MetalDark"

ROOF = {
    "red": ("TB_RoofRed", "TB_RoofRedDark"),
    "blue": ("TB_RoofBlue", "TB_RoofBlueDark"),
    "thatch": ("TB_RoofThatch", "TB_RoofThatchDark"),
    "green": ("TB_RoofGreen", "TB_RoofGreenDark"),
    "slate": ("TB_RoofSlate", "TB_RoofSlateDark"),
}


class Face:
    """One outside wall: origin at its left end on the ground (seen from outside), running along u."""

    def __init__(self, origin, u, length):
        self.o = V(origin)
        self.u = V(u).normalized()
        self.L = length
        self.inward = V(0, 0, 1).cross(self.u)

    def M(self, s, z=0.0, out=0.0):
        p = self.o + self.u * s + V(0, 0, z) - self.inward * out
        u, i = self.u, self.inward
        return Matrix(((u.x, i.x, 0, p.x), (u.y, i.y, 0, p.y), (u.z, i.z, 1, p.z), (0, 0, 0, 1)))

    def point(self, s, z=0.0, out=0.0):
        return self.o + self.u * s + V(0, 0, z) - self.inward * out


def faces(x0, x1, y0, y1):
    return {
        "front": Face((x0, y0, 0), (1, 0, 0), x1 - x0),
        "right": Face((x1, y0, 0), (0, 1, 0), y1 - y0),
        "back": Face((x1, y1, 0), (-1, 0, 0), x1 - x0),
        "left": Face((x0, y1, 0), (0, -1, 0), y1 - y0),
    }


# ---- walls -------------------------------------------------------------------------------------------------------

def stones(k, f, s0, s1, z0, z1, holes=(), mats=(SL, SM), course=(0.4, 0.55), block=(0.55, 1.0), out=0.035):
    """Irregular coursed stonework on a wall (the darker wall core shows in the joints). holes = [(s, z0, w, h)]."""
    if not k.detail:
        return
    z = z0
    row = 0
    while z < z1 - 0.05:
        ch = min(k.r(*course), z1 - z)
        s = s0 - (k.r(0, block[0]) if row % 2 else 0)
        while s < s1 - 0.05:
            bw = k.r(*block)
            a, b = max(s, s0), min(s + bw, s1)
            if b - a > 0.12:
                mid = (a + b) / 2
                blocked = False
                for (hs, hz, hw, hh) in holes:
                    if abs(mid - hs) < (b - a) / 2 + hw / 2 + 0.02 and z < hz + hh + 0.02 and z + ch > hz - 0.02:
                        blocked = True
                        break
                if not blocked:
                    with k.frame(f.M(mid, z + ch / 2, out / 2)):
                        k.box((0, 0, 0), (b - a - 0.05, out + 0.04, ch - 0.05), k.pick(mats), skip=(3,))
            s += bw
        z += ch
        row += 1


def quoins(k, f, z0, z1, mats=(SL, SM)):
    """Alternating long/short corner stones at both ends of a wall."""
    z, i = z0, 0
    while z < z1 - 0.1:
        h = min(0.4, z1 - z)
        for end, sgn in ((0.0, 1), (f.L, -1)):
            w = 0.62 if i % 2 == 0 else 0.38
            with k.frame(f.M(end + sgn * w / 2, z + h / 2, 0.04)):
                k.box((0, 0, 0), (w, 0.1, h - 0.04), mats[i % 2], skip=(3,))
        z += h
        i += 1


def timber_face(k, f, z0, z1, openings=(), mat=WD, w=0.15, braces=True, rails=True, sill_rail=None, flip=False):
    """Half-timbering on one wall between z0 and z1: corner posts, posts either side of every opening (s, w), sole
    and head plates, diagonal braces in the wider bare bays, a sill rail if asked."""
    posts = {0.08, f.L - 0.08}
    for (s, ow) in openings:
        posts.add(s - ow / 2 - w / 2 - 0.03)
        posts.add(s + ow / 2 + w / 2 + 0.03)
    posts = sorted(p for p in posts if 0.05 <= p <= f.L - 0.05)
    # merge posts that crowd each other, split very wide bays
    merged = []
    for p in posts:
        if merged and p - merged[-1] < 0.3:
            merged[-1] = (merged[-1] + p) / 2
        else:
            merged.append(p)
    posts = []
    for i, p in enumerate(merged):
        if i > 0 and p - merged[i - 1] > 1.9:
            n = int((p - merged[i - 1]) / 1.3)
            for j in range(1, n + 1):
                posts.append(merged[i - 1] + (p - merged[i - 1]) * j / (n + 1))
        posts.append(p)
    posts.sort()
    h = z1 - z0
    for p in posts:
        with k.frame(f.M(p, z0 + h / 2, 0.03)):
            k.box((0, 0, 0), (w, 0.1, h), mat, skip=(3,))
    if rails:
        for z in (z0 + 0.07, z1 - 0.07):
            with k.frame(f.M(f.L / 2, z, 0.045)):
                k.box((0, 0, 0), (f.L + 0.04, 0.12, 0.15), mat, skip=(3,))
    if sill_rail is not None:
        for i in range(len(posts) - 1):
            a, b = posts[i], posts[i + 1]
            mid = (a + b) / 2
            if any(abs(mid - s) < ow / 2 + 0.05 for (s, ow) in openings):
                continue
            with k.frame(f.M(mid, sill_rail, 0.03)):
                k.box((0, 0, 0), (b - a, 0.1, 0.12), mat, skip=(3,))
    if braces:
        for i in range(len(posts) - 1):
            a, b = posts[i] + w / 2, posts[i + 1] - w / 2
            if b - a < 0.55:
                continue
            mid = (a + b) / 2
            if any(abs(mid - s) < ow / 2 + 0.2 for (s, ow) in openings):
                continue
            # one brace per bare bay, leaning alternately, like the references' framing
            up = ((i + (1 if flip else 0)) % 2 == 0)
            za, zb = z0 + 0.12, z1 - 0.12
            pa, pb = (a, b) if up else (b, a)
            k.span(f.point(pa, za, 0.03), f.point(pb, zb, 0.03), w * 0.9, 0.1, mat, up=tuple(-f.inward))


def joists(k, f, z, spacing=0.5, mat=WD):
    """Beam ends sticking out under a jettied floor."""
    if not k.detail:
        return
    n = int(f.L / spacing)
    for i in range(n + 1):
        s = 0.1 + i * (f.L - 0.2) / max(1, n)
        with k.frame(f.M(s, z, 0.12)):
            k.box((0, 0, 0), (0.12, 0.3, 0.14), mat)


def plaster_block(k, x0, x1, y0, y1, z0, z1, mat=CREAM):
    k.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), mat)


def floor_band(k, x0, x1, y0, y1, z, mat=WD, h=0.18, over=0.06):
    k.box(((x0 + x1) / 2, (y0 + y1) / 2, z), (x1 - x0 + over * 2, y1 - y0 + over * 2, h), mat)


# ---- windows & doors ---------------------------------------------------------------------------------------------

def window(k, w=0.8, h=1.0, glass="TB_WindowA", frame=WD, shutters=None, box=None, panes=4, sill=True, lead=False,
           arch=False):
    """A window centred on the frame origin. shutters = material (open, flat against the wall), box = flower list."""
    t = 0.1
    k.box((0, -0.01, 0), (w, 0.06, h), glass)
    k.box((-w / 2 - t / 2, -0.04, 0), (t, 0.1, h + t * 2), frame)
    k.box((w / 2 + t / 2, -0.04, 0), (t, 0.1, h + t * 2), frame)
    k.box((0, -0.04, h / 2 + t / 2), (w + t * 2, 0.1, t), frame)
    if arch and k.detail:
        k.wedge((0, -0.04, h / 2 + t), w + t * 2, 0.1, 0.18, frame, r=(0, 0, 90))
    if sill:
        k.box((0, -0.07, -h / 2 - 0.05), (w + 0.3, 0.16, 0.08), frame)
    else:
        k.box((0, -0.04, -h / 2 - t / 2), (w + t * 2, 0.1, t), frame)
    if k.detail:
        if lead:   # diamond leaded glass (04 blacksmith windows)
            for s in (-1, 1):
                for i in range(3):
                    off = -w / 2 + (i + 0.5) * w / 3
                    k.box((off, -0.045, 0), (0.025, 0.02, h * 1.05), IRON, r=(0, s * 35, 0))
        elif panes >= 2:
            k.box((0, -0.05, 0), (0.05, 0.05, h), frame)
            if panes >= 4:
                k.box((0, -0.05, 0), (w, 0.05, 0.05), frame)
    if shutters and k.detail:
        for s in (-1, 1):
            x = s * (w / 2 + t + w * 0.26)
            k.box((x, -0.03, 0), (w * 0.5, 0.05, h + 0.06), shutters)
            for zz in (-h * 0.3, h * 0.3):
                k.box((x, -0.06, zz), (w * 0.48, 0.02, 0.07), WD)
    if box:
        with k.at((0, -0.2, -h / 2 - 0.32)):
            P.flower_box(k, w + 0.2, box)


def door(k, w=1.0, h=2.0, arch=True, frame=WD, leaf=WM, stone_arch=False, ring=True, steps=0, step_mat=SL, glass_top=False):
    """Plank door (arched top by default) with its frame; origin at the bottom centre. steps = stone steps in front."""
    sh = 0.0
    if steps:
        for i in range(steps):
            d = 0.34 * (steps - i)
            k.box((0, -d / 2 - 0.02, 0.09 + i * 0.18), (w + 0.5 - i * 0.04, d, 0.18), step_mat if i % 2 == 0 else SM)
        sh = steps * 0.18
    r = w / 2
    body_h = h - (r if arch else 0)
    outline = [(-r, 0), (r, 0), (r, body_h)]
    if arch:
        for i in range(1, 8):
            a = math.pi * i / 8
            outline.append((math.cos(a) * r, body_h + math.sin(a) * r * 0.85))
    outline.append((-r, body_h))
    with k.at((0, 0, sh)):
        k.extrude([(x * 1.08, z * 1.02) for x, z in outline], 0.02, WX, c=(0, -0.025, 0))    # the dark recess
        k.extrude(outline, 0.06, leaf, c=(0, -0.05, 0))
        if k.detail:
            n = 4
            for i in range(1, n):
                x = -r + i * w / n
                k.box((x, -0.06, body_h / 2), (0.03, 0.03, body_h * 0.96), WD)
            for z in (0.35, body_h - 0.25):
                k.box((-r * 0.2, -0.075, z), (w * 0.75, 0.02, 0.07), IRON)
            if ring:
                k.prism((r * 0.55, -0.08, body_h * 0.55), 8, 0.07, 0.07, 0.02, IRON, r=(90, 0, 0), caps=True)
        # frame: posts + arch of short spans
        fm = SL if stone_arch else frame
        fw = 0.2 if stone_arch else 0.12
        for s in (-1, 1):
            k.box((s * (r + fw / 2), -0.05, body_h / 2), (fw, 0.14, body_h), fm)
        if arch:
            pts = [(math.cos(math.pi * i / 8) * (r + fw / 2), body_h + math.sin(math.pi * i / 8) * (r * 0.85 + fw / 2)) for i in range(9)]
            for i in range(8):
                a, b = pts[i], pts[i + 1]
                k.span((a[0], -0.05, a[1]), (b[0], -0.05, b[1]), 0.15, fw, fm if (not stone_arch or i % 2) else SM, up=(0, -1, 0))
        else:
            k.box((0, -0.05, body_h + fw / 2), (w + fw * 2, 0.14, fw), fm)
    return sh


# ---- roofs -------------------------------------------------------------------------------------------------------

class Roof:
    def __init__(self, x0, x1, y0, y1, zb, rise, ridge):
        self.x0, self.x1, self.y0, self.y1, self.zb, self.rise, self.ridge = x0, x1, y0, y1, zb, rise, ridge

    def z_at(self, x, y):
        if self.ridge == "x":
            half = (self.y1 - self.y0) / 2
            d = abs(y - (self.y0 + self.y1) / 2)
        else:
            half = (self.x1 - self.x0) / 2
            d = abs(x - (self.x0 + self.x1) / 2)
        return self.zb + self.rise * max(0.0, 1 - d / half)


def _slope(k, O, d, n, La, Ld, style, mats, tile=0.72, dark_ratio=0.2):
    """Tiles / shingles / thatch over a planar slope: O = midpoint of its top (ridge) edge, d = down-slope unit,
    n = outward normal, La = width along the ridge, Ld = length down the slope."""
    a = n.cross(d)
    M = Matrix(((d.x, a.x, n.x, O.x), (d.y, a.y, n.y, O.y), (d.z, a.z, n.z, O.z), (0, 0, 0, 1)))
    main, dark = mats
    with k.frame(M):
        if not k.detail:
            k.box((Ld / 2, 0, 0.08), (Ld, La, 0.16), main)
            return
        if style == "thatch":
            # thick layered thatch: overlapping courses, each with a rounded lip, broken into a few bundles along
            # the ridge; the eave is one fat rolled edge (sheets 01 #3, 02 #2/#7, 03 cottage)
            k.box((Ld / 2, 0, 0.1), (Ld, La, 0.2), main)
            rows = max(3, int(Ld / 0.62))
            step = Ld / rows
            for i in range(1, rows):
                v = i * step
                segs = max(2, int(La / 1.3))
                for j in range(segs):
                    a0 = -La / 2 + j * La / segs
                    a1 = a0 + La / segs
                    lift = k.r(0.0, 0.035)
                    k.box((v - step * 0.42, (a0 + a1) / 2, 0.25 + lift), (step * 0.9, a1 - a0 + 0.015, 0.1),
                          main, r=(0, -5, 0))
                    k.prism((v - 0.03, (a0 + a1) / 2, 0.24 + lift), 6, 0.085, 0.085, a1 - a0 + 0.01,
                            dark if (i + j) % 4 == 0 else main, r=(90, 0, 0), phase=30)
            k.prism((Ld - 0.08, 0, 0.12), 6, 0.2, 0.2, La + 0.02, dark, r=(90, 0, 0), phase=30)   # the rolled eave
            return
        thin = style == "slate"
        tw = tile * (0.8 if thin else 1.0)
        tl = tw * 0.9
        th = 0.07 if thin else 0.1
        lift = 3.0 if thin else 6.0
        pitch = tl * 0.78
        k.box((Ld / 2, 0, 0.03), (Ld, La, 0.06), dark)
        rows = max(1, int(math.ceil((Ld - tl * 0.2) / pitch)))
        for i in range(rows):
            v = min(i * pitch + tl / 2, Ld - tl / 2)
            off = (tw / 2) if i % 2 else 0.0
            t = -La / 2 - off
            while t < La / 2 - 0.02:
                a0, b0 = max(t, -La / 2), min(t + tw, La / 2)
                if b0 - a0 > 0.08:
                    k.box((v, (a0 + b0) / 2, 0.06 + th / 2), (tl, b0 - a0 - 0.035, th),
                          dark if k.rng.random() < dark_ratio else main, r=(0, -lift + k.r(-1.5, 1.5), 0), skip=(0,))
                t += tw


def gable_roof(k, x0, x1, y0, y1, zb, rise, ridge="x", color="red", style="tile", over=0.35, gable_over=0.3,
               wall=CREAM, gables=(True, True), gable_timber=True, barge=WD, ridge_cap=True, tile=0.72,
               gable_windows=(None, None), fill=True):
    """A gable roof over the rectangle, ridge along x or y, eaves at zb. Fills the gable triangles with wall
    (+ timbering), adds barge boards and a ridge cap. Returns a Roof (z_at for chimneys/dormers)."""
    mats = ROOF[color] if isinstance(color, str) else color
    if ridge == "y":
        # build it as an x-ridge roof in a frame turned 90 degrees
        cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
        with k.at((cx, cy, 0), rz=90):
            hx, hy = (y1 - y0) / 2, (x1 - x0) / 2
            gable_roof(k, -hx, hx, -hy, hy, zb, rise, "x", color, style, over, gable_over, wall, gables, gable_timber,
                       barge, ridge_cap, tile, gable_windows, fill)
        return Roof(x0, x1, y0, y1, zb, rise, "y")
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    half = (y1 - y0) / 2
    L = x1 - x0
    theta = math.atan2(rise, half)
    c, s = math.cos(theta), math.sin(theta)
    zr = zb + rise
    Ld = math.hypot(half, rise) + over
    k.meta["ridge"] = max(k.meta.get("ridge", 0.0), k.world((cx, cy, zr + 0.2)).z)
    La = L + gable_over * 2
    for side in (-1, 1):
        d = V(0, side * c, -s)
        n = V(0, side * s, c)
        O = V(cx, cy, zr) + V(0, 0, 0.02)
        _slope(k, O, d, n, La, Ld, style, mats, tile=tile)
    # gable walls
    if fill:
        k.wedge((cx, cy, zb), y1 - y0, L, rise, wall, r=(0, 0, 90))
    if barge:
        for gx, gi in ((x0 - gable_over + 0.06, 0), (x1 + gable_over - 0.06, 1)):
            for side in (-1, 1):
                top = V(gx, cy, zr + 0.14)
                bot = V(gx, cy + side * (half + over * c), zb - over * s + 0.1)
                k.span(top, bot, 0.1, 0.26 if style != "thatch" else 0.36, barge, up=(0, -side * s, c))
    if gable_timber and k.detail:
        for gx, sign in ((x0, -1), (x1, 1)):
            f = Face((gx, cy - sign * half, zb), (0, sign, 0), 2 * half)  # seen from outside that end
            k.span(f.point(half, 0, 0.03), f.point(half, rise - 0.15, 0.03), 0.14, 0.1, WD, up=tuple(-f.inward))
            k.span(f.point(0.25, 0.05, 0.03), f.point(half - 0.1, rise * 0.62, 0.03), 0.12, 0.1, WD, up=tuple(-f.inward))
            k.span(f.point(2 * half - 0.25, 0.05, 0.03), f.point(half + 0.1, rise * 0.62, 0.03), 0.12, 0.1, WD, up=tuple(-f.inward))
            if rise > 1.4:
                zc = rise * 0.42
                w = 2 * half * (1 - zc / rise)
                k.span(f.point(half - w / 2 + 0.05, zc, 0.035), f.point(half + w / 2 - 0.05, zc, 0.035), 0.12, 0.1, WD, up=tuple(-f.inward))
    for gx, sign, win in ((x0, -1, gable_windows[0]), (x1, 1, gable_windows[1])):
        if win:
            f = Face((gx, cy - sign * half, zb), (0, sign, 0), 2 * half)
            with k.frame(f.M(half, rise * 0.3 + win[1] / 2)):
                window(k, win[0], win[1], shutters=win[2] if len(win) > 2 else None)
    if ridge_cap:
        if style == "thatch":
            k.prism((cx, cy, zr + 0.2), 6, 0.2, 0.2, La + 0.1, mats[1], r=(0, 90, 0))
        elif k.detail:
            n = int(La / 0.5)
            for i in range(n):
                x = cx - La / 2 + (i + 0.5) * La / n
                k.wedge((x, cy, zr + 0.1), La / n - 0.03, 0.46, 0.14, mats[1], r=(0, 0, 90))
        else:
            k.box((cx, cy, zr + 0.14), (La, 0.3, 0.12), mats[1])
    return Roof(x0, x1, y0, y1, zb, rise, "x")


def shed_roof(k, x0, x1, y_wall, depth, z_top, z_low, color="red", style="tile", over=0.25, tile=0.55):
    """A lean-to / porch roof falling away from a wall toward -Y (front). Tiles along x."""
    mats = ROOF[color] if isinstance(color, str) else color
    theta = math.atan2(z_top - z_low, depth)
    d = V(0, -math.cos(theta), -math.sin(theta))
    n = V(0, -math.sin(theta), math.cos(theta))
    O = V((x0 + x1) / 2, y_wall, z_top)
    _slope(k, O, d, n, x1 - x0 + over * 2, math.hypot(depth, z_top - z_low) + over, style, mats, tile=tile)


def chimney(k, x, y, z0, z1, w=0.62, cap=SD, pot="TB_Clay", mats=(SL, SM)):
    """Coursed stone chimney from z0 up to z1 (+ cap and a terracotta top)."""
    if k.detail:
        P.stone_stack(k, (x, y, z0), w, z1 - z0, courses=max(2, int((z1 - z0) / 0.42)), mats=mats)
    else:
        k.box((x, y, (z0 + z1) / 2), (w, w, z1 - z0), mats[0])
    k.box((x, y, z1 + 0.06), (w + 0.14, w + 0.14, 0.12), cap)
    if pot:
        k.box((x, y, z1 + 0.22), (w * 0.62, w * 0.62, 0.22), pot)
        top = z1 + 0.33
    else:
        top = z1 + 0.12
    p = k.world((x, y, top + 0.1))
    k.meta.setdefault("chimneys", []).append([p.x, p.y, p.z])


def dormer(k, w=1.1, h=1.0, depth=1.6, color="red", wall=CREAM, glass="TB_WindowA", rise=0.6, style="tile", shutters=None, box=None):
    """A dormer facing -Y built at the frame origin = centre of its front wall bottom."""
    k.box((0, depth / 2, h / 2), (w, depth, h), wall)
    for s in (-1, 1):
        k.box((s * (w / 2 - 0.05), -0.02, h / 2), (0.12, 0.1, h), WD)
    k.box((0, -0.02, h - 0.05), (w + 0.04, 0.1, 0.1), WD)
    gable_roof(k, -w / 2 - 0.02, w / 2 + 0.02, -0.05, depth, h, rise, "y", color, style, over=0.18, gable_over=0.2,
               wall=wall, gable_timber=False, tile=0.42, ridge_cap=True)
    with k.at((0, 0, h * 0.5)):
        window(k, w * 0.5, h * 0.52, glass=glass, sill=True, shutters=shutters, box=box)


def balcony(k, w, depth=0.85, rail=WM, floor=WM, flowers=True, pots=True, open_sides=(True, True)):
    """A wooden balcony on the wall below the frame origin level: floor at z = 0, sticking out along -Y."""
    k.box((0, -depth / 2, -0.06), (w, depth, 0.12), floor)
    k.box((0, -depth - 0.02, -0.09), (w + 0.06, 0.08, 0.18), WD)
    for x in (-w / 2 + 0.15, w / 2 - 0.15):
        k.span((x, 0.0, -0.8), (x, -depth + 0.1, -0.1), 0.1, 0.1, WD)
    posts = [-w / 2 + 0.05, w / 2 - 0.05]
    for x in posts:
        k.box((x, -depth + 0.05, 0.45), (0.1, 0.1, 0.9), rail)
    k.box((0, -depth + 0.05, 0.9), (w, 0.1, 0.08), rail)
    k.box((0, -depth + 0.05, 0.12), (w, 0.08, 0.06), rail)
    for side, on in ((-1, open_sides[0]), (1, open_sides[1])):
        if on:
            k.box((side * (w / 2 - 0.05), -depth / 2, 0.9), (0.08, depth, 0.08), rail)
            k.box((side * (w / 2 - 0.05), -depth / 2, 0.12), (0.08, depth, 0.06), rail)
    if k.detail:
        n = int(w / 0.18)
        for i in range(1, n):
            x = -w / 2 + i * w / n
            k.box((x, -depth + 0.05, 0.5), (0.05, 0.04, 0.72), WL if i % 2 else rail)
        for side, on in ((-1, open_sides[0]), (1, open_sides[1])):
            if on:
                for j in range(1, int(depth / 0.18)):
                    k.box((side * (w / 2 - 0.05), -depth + j * 0.18, 0.5), (0.04, 0.05, 0.72), rail)
        if flowers:
            with k.at((0, -depth + 0.05, 0.94)):
                P.flower_box(k, min(w - 0.3, 1.3), ("TB_FlowerWhite", "TB_FlowerRed") if flowers is True else flowers)
        if pots:
            with k.at((w / 2 - 0.35, -0.3, 0)):
                P.potted_plant(k, 0.8)


def tile_awning(k, w, depth, z, color="red", posts=False, brackets=True, drop=0.55, style="tile"):
    """A small tiled roof over a door/window: from the wall at height z sloping out and down."""
    shed_roof(k, -w / 2, w / 2, 0.0, depth, z, z - drop, color, style, over=0.12, tile=0.46)
    if posts:
        for x in (-w / 2 + 0.1, w / 2 - 0.1):
            k.box((x, -depth + 0.08, (z - drop) / 2), (0.14, 0.14, z - drop), WM)
            k.span((x, -depth + 0.08, z - drop - 0.45), (x, -0.02, z - drop - 0.05), 0.08, 0.08, WM)
        k.box((0, -depth + 0.08, z - drop - 0.04), (w, 0.14, 0.14), WM)
    elif brackets:
        for x in (-w / 2 + 0.15, w / 2 - 0.15):
            k.span((x, 0.0, z - 0.75), (x, -depth + 0.15, z - drop - 0.02), 0.09, 0.09, WD)
            k.box((x, -depth / 2, z - drop / 2 - 0.08), (0.09, depth, 0.09), WD, r=(-math.degrees(math.atan2(drop, depth)), 0, 0))


def cloth_awning(k, w, depth, z, stripes=("TB_ClothCream",), poles=True, drop=0.5):
    """A canvas awning (townhouse 8 in sheet 01): cloth slope, a scalloped valance, two poles."""
    ang = math.degrees(math.atan2(drop, depth))
    n = max(1, int(w / 0.5))
    for i in range(n):
        x = -w / 2 + (i + 0.5) * w / n
        mat = stripes[i % len(stripes)]
        k.box((x, -depth / 2, z - drop / 2), (w / n + 0.005, math.hypot(depth, drop), 0.04), mat, r=(-ang, 0, 0))
        if k.detail:
            k.wedge((x, -depth - 0.01, z - drop - 0.16), w / n, 0.03, 0.16, mat, r=(180, 0, 0))
    if poles:
        for x in (-w / 2 + 0.05, w / 2 - 0.05):
            k.box((x, -depth, (z - drop) / 2), (0.08, 0.08, z - drop), WM)


def wall_lantern(k, arm=0.42):
    """A lantern on an iron bracket (sheet 01/04: beside every door). Origin on the wall at bracket height."""
    k.box((0, -0.02, 0), (0.08, 0.04, 0.3), IRON)
    k.box((0, -arm / 2, 0.12), (0.04, arm, 0.04), IRON)
    if k.detail:
        k.span((0, -0.02, -0.12), (0, -arm * 0.7, 0.11), 0.03, 0.03, IRON)
    with k.at((0, -arm + 0.04, -0.28)):
        P.lantern(k, s=0.95)
    p = k.world((0, -arm + 0.04, -0.12))
    k.meta.setdefault("lanterns", []).append([p.x, p.y, p.z])


def hanging_bracket_lantern(k, arm=0.8):
    """A long wooden arm off the wall with a lantern hanging from its end (sheet 01 houses 5, 7, 8)."""
    k.box((0, -arm / 2, 0), (0.1, arm, 0.1), WD)
    k.span((0, -0.02, -0.5), (0, -arm * 0.6, -0.02), 0.08, 0.08, WD)
    k.box((0, -arm + 0.1, -0.15), (0.02, 0.02, 0.22), IRON)
    with k.at((0, -arm + 0.1, -0.62)):
        P.lantern(k, s=1.0)
    p = k.world((0, -arm + 0.1, -0.45))
    k.meta.setdefault("lanterns", []).append([p.x, p.y, p.z])


def wall_banner(k, w=0.55, h=1.2, cloth="TB_ClothBlue", emblem="fleur"):
    """A banner hanging flat against the wall from a rod (origin = rod centre)."""
    k.box((0, -0.08, 0), (w + 0.2, 0.05, 0.05), WD)
    k.extrude([(-w / 2, 0), (-w / 2, -h), (0, -h - w * 0.3), (w / 2, -h), (w / 2, 0)], 0.03, cloth, c=(0, -0.08, -0.03))
    if k.detail:
        k.box((0, -0.105, -0.06), (w, 0.01, 0.06), "TB_Gold")
        P.emblem_mark(k, emblem, (0, -0.1, -h * 0.5), w * 0.9)


def arm_banner(k, arm=0.8, w=0.45, h=0.95, cloth="TB_ClothBlue", emblem="fleur"):
    """A banner hung from an iron arm sticking out of the wall (01 house 5)."""
    k.box((0, -arm / 2, 0), (0.05, arm, 0.05), IRON)
    k.prism((0, -arm, 0.02), 8, 0.05, 0.05, 0.02, IRON, r=(0, 90, 0))
    with k.at((0, -arm * 0.55, -0.04), rz=90):
        k.extrude([(-w / 2, 0), (-w / 2, -h), (0, -h - w * 0.3), (w / 2, -h), (w / 2, 0)], 0.03, cloth, c=(0, -0.015, 0))
        if k.detail:
            P.emblem_mark(k, emblem, (0, -0.03, -h * 0.5), w * 0.9)
            P.emblem_mark(k, emblem, (0, 0.03, -h * 0.5), w * 0.9)


def ivy(k, x, z0, z1, spread=0.35, dense=1.0):
    """Climbing ivy: a column of leaf clumps up the wall (frame origin on the wall)."""
    if not k.detail:
        return
    z = z0
    while z < z1:
        n = 2 if dense >= 1 else 1
        for _ in range(n):
            k.blob((x + k.r(-spread, spread), -0.06, z), k.r(0.1, 0.16), k.pick(("TB_Leaf", "TB_LeafLight", "TB_LeafDark")),
                   sub=1, squash=0.9, sy=0.55)
        z += k.r(0.16, 0.26) / dense
        spread *= 0.97


def steps(k, w, n=3, rise=0.17, run=0.32, mat=SL, rail=False, side=1):
    """Stone steps coming out of the wall (toward -Y), origin at the wall bottom centre."""
    for i in range(n):
        d = run * (n - i)
        k.box((0, -d / 2, rise * (i + 0.5)), (w - i * 0.03, d, rise), mat if i % 2 == 0 else SM)
    if rail and k.detail:
        x = side * (w / 2 + 0.05)
        k.box((x, -run * n + 0.05, 0.55), (0.1, 0.1, 1.1), WM)
        k.box((x, -0.05, 0.55 + rise * n), (0.1, 0.1, 1.1), WM)
        k.span((x, -run * n + 0.05, 1.05), (x, -0.05, 1.05 + rise * n), 0.08, 0.08, WM)


# ---- storeys -----------------------------------------------------------------------------------------------------

def storey(k, box, z0, z1, feats=(), kind="timber", wall=CREAM, plinth=0.0, braces=True, sill_rail=True, flip=False,
           core=None, stone_mats=(SL, SM), skip_faces=()):
    """One storey of a rectangular block box = (x0, x1, y0, y1) from z0 to z1.
    kind: "timber" (plaster + half-timbering), "stone" (coursed stone), "plaster" (plain).
    plinth: height of a stone base course at the bottom of a timber/plaster storey.
    feats: [(face, "win" | "door" | "hole", s, opts)] - windows (centre at z0 + opts["zc"]) and doors (bottom at z0 +
    opts.get("z", 0)) are built here too, and cut out of the stonework / timbering. Returns the faces."""
    x0, x1, y0, y1 = box
    F = faces(x0, x1, y0, y1)
    if z1 - z0 > 0.5:
        cs = [k.world((x, y, 0)) for x in (x0, x1) for y in (y0, y1)]
        w = k.meta.get("walls", [1e9, -1e9, 1e9, -1e9])
        k.meta["walls"] = [min(w[0], min(c.x for c in cs)), max(w[1], max(c.x for c in cs)),
                           min(w[2], min(c.y for c in cs)), max(w[3], max(c.y for c in cs))]
    core = core or (SD if kind == "stone" else wall)
    k.box(((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), core)
    holes = {n: [] for n in F}
    opens = {n: [] for n in F}
    for (fn, typ, s, o) in feats:
        if typ == "door":
            w, h = o.get("w", 1.0), o.get("h", 2.1)
            zb = z0 + o.get("z", 0.0)
            holes[fn].append((s, zb, w + 0.3, h + 0.3))
        else:
            w, h = o.get("w", 0.8), o.get("h", 1.0)
            zb = z0 + o.get("zc", 1.45) - h / 2
            holes[fn].append((s, zb - 0.15, w + (0.6 if o.get("shutters") else 0.3), h + 0.35))
        opens[fn].append((s, w + (0.0 if typ == "door" else 0.0)))
    for n, f in F.items():
        if n in skip_faces:
            continue
        if kind == "stone":
            if n == "back":
                quoins(k, f, z0, z1, stone_mats)
            else:
                stones(k, f, 0.0, f.L, z0, z1, holes[n], mats=stone_mats)
        else:
            zt = z0
            if plinth > 0:
                k.box(((x0 + x1) / 2, (y0 + y1) / 2, z0 + plinth / 2), (x1 - x0 + 0.08, y1 - y0 + 0.08, plinth), SD)
                stones(k, f, 0.0, f.L, z0, z0 + plinth, [hh for hh in holes[n] if hh[1] < z0 + plinth], mats=stone_mats, out=0.07)
                zt = z0 + plinth
            if kind == "timber":
                timber_face(k, f, zt, z1, opens[n], braces=braces, flip=flip,
                            sill_rail=(z0 + 0.95) if (sill_rail and z1 - z0 > 2.0) else None)
    for (fn, typ, s, o) in feats:
        f = F[fn]
        if typ == "door":
            with k.frame(f.M(s, z0 + o.get("z", 0.0))):
                door(k, o.get("w", 1.0), o.get("h", 2.1), arch=o.get("arch", True), stone_arch=o.get("stone_arch", kind == "stone"),
                     steps=o.get("steps", 0), leaf=o.get("leaf", WM))
            if o.get("main"):
                p = k.world(f.point(s, 0, 0.9 + 0.34 * o.get("steps", 0)))
                k.meta["door"] = [p.x, p.y, 0.0]
        elif typ == "win":
            with k.frame(f.M(s, z0 + o.get("zc", 1.45))):
                window(k, o.get("w", 0.8), o.get("h", 1.0), glass=o.get("glass", k.pick(("TB_WindowA", "TB_WindowB"))),
                       shutters=o.get("shutters"), box=o.get("box"), panes=o.get("panes", 4), lead=o.get("lead", False),
                       arch=o.get("arch", False), frame=o.get("frame", WD))
    return F
