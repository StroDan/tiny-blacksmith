# Tiny Blacksmith - vegetation and ground clutter from 05_nature_assets.png.
# Faceted icosphere canopies, stacked-cone pines, blade-card grass, blob rocks. Origin on the ground.
# Seasonal naming: broadleaf trees, saplings and bushes are "Tree_*" in Unity (MeadowLife tints their TB_Leaf*
# materials through the seasons); pines, rocks and ground clutter are not tinted.
import math
from tb_lib import V

BARK, LEAF, LEAF_L, LEAF_D = "TB_Bark", "TB_Leaf", "TB_LeafLight", "TB_LeafDark"
ROCK, ROCK_L, ROCK_D = "TB_StoneMedium", "TB_StoneLight", "TB_StoneDark"


def _trunk(k, h, r0, r1, lean=(0, 0), sides=7):
    k.prism((lean[0] / 2, lean[1] / 2, h / 2), sides, r0, r1, h, BARK, r=(math.degrees(-lean[1] / h), math.degrees(lean[0] / h), 0))
    # root flare
    for i in range(4):
        a = i * 90 + k.r(-20, 20)
        k.span((0, 0, 0.25), (math.cos(math.radians(a)) * r0 * 2.2, math.sin(math.radians(a)) * r0 * 2.2, 0.0), r0 * 0.7, r0 * 0.6, BARK)


def _branch(k, a, b, r):
    k.span(a, b, r, r, BARK)


def broadleaf(k, h=6.0, crown=2.3, main=LEAF, light=LEAF_L, blobs=11, squash=0.95):
    """A round broadleaf tree: forked trunk, a dome of faceted leaf clumps with lighter ones on top."""
    th = h * 0.36
    _trunk(k, th, 0.3 * h / 6, 0.2 * h / 6)
    top = V(0, 0, th)
    for i in range(3):
        a = math.radians(i * 120 + k.r(-15, 15))
        end = top + V(math.cos(a) * crown * 0.45, math.sin(a) * crown * 0.45, h * 0.2)
        _branch(k, top - V(0, 0, 0.2), end, 0.1 * h / 6)
    cz = th + crown * 0.62
    for i in range(blobs):
        a = 2 * math.pi * i / blobs + k.r(-0.3, 0.3)
        ring = 0.62 if i % 3 else 0.35
        z = cz + k.r(-0.4, 0.6) * crown * 0.4 + (0.3 if ring < 0.5 else 0)
        k.blob((math.cos(a) * crown * ring, math.sin(a) * crown * ring, z), crown * k.r(0.42, 0.52), main, sub=2, squash=squash)
    for i in range(4):
        a = 2 * math.pi * i / 4 + 0.4
        k.blob((math.cos(a) * crown * 0.3, math.sin(a) * crown * 0.3, cz + crown * 0.55), crown * k.r(0.36, 0.44), light, sub=2, squash=squash)
    k.blob((0, 0, cz + crown * 0.8), crown * 0.4, light, sub=2, squash=squash)
    k.meta["seasonal"] = True


def tree_green(k):
    broadleaf(k, 6.2, 2.4, LEAF, LEAF_L)


def tree_lime(k):
    broadleaf(k, 5.6, 2.0, LEAF_L, "TB_LeafYellow", blobs=10, squash=1.1)


def tree_orange(k):
    broadleaf(k, 5.2, 1.8, "TB_LeafOrange", "TB_LeafRed", blobs=10, squash=1.15)


def tree_birch(k):
    """White trunk with dark marks, loose light-green clumps up the stem."""
    h = 6.0
    k.prism((0, 0, h * 0.4), 6, 0.15, 0.09, h * 0.8, "TB_BirchBark")
    for i in range(7):
        z = 0.4 + i * 0.55
        a = k.r(0, 360)
        k.box((math.cos(math.radians(a)) * 0.12, math.sin(math.radians(a)) * 0.12, z), (0.14, 0.06, 0.05), "TB_WoodDarker", r=(0, 0, a))
    for i in range(7):
        a = math.radians(i * 137)
        z = h * 0.45 + i * 0.42
        rr = 0.7 - i * 0.06
        _branch(k, (0, 0, z - 0.4), (math.cos(a) * rr, math.sin(a) * rr, z), 0.05)
        k.blob((math.cos(a) * rr, math.sin(a) * rr, z + 0.1), k.r(0.5, 0.65), LEAF_L if i % 2 else LEAF, sub=2)
    k.blob((0, 0, h + 0.1), 0.55, LEAF_L, sub=2)
    k.meta["seasonal"] = True


def pine(k, h=7.0, tiers=6, r=1.7):
    """Stacked faceted cones, darker below and lighter toward the tip (05 Pine Trees)."""
    k.prism((0, 0, h * 0.15), 6, 0.2 * h / 7, 0.14 * h / 7, h * 0.3, BARK)
    z = h * 0.2
    for i in range(tiers):
        t = i / tiers
        rr = r * (1 - t * 0.8)
        th = h * 0.28 * (1 - t * 0.4)
        k.prism((0, 0, z + th / 2), 8, rr, 0.0, th, "TB_Pine" if i % 2 == 0 else "TB_PineLight", phase=k.r(0, 45))
        z += th * 0.52
    k.prism((0, 0, z + 0.3), 6, r * 0.18, 0.0, 0.7, "TB_PineLight")


def pine_tall(k):
    pine(k, 8.0, 7, 1.9)


def pine_mid(k):
    pine(k, 6.2, 6, 1.6)


def pine_small(k):
    pine(k, 4.4, 5, 1.2)


def sapling(k, h=1.8, main=LEAF):
    k.prism((0, 0, h * 0.45), 5, 0.05, 0.035, h * 0.9, BARK)
    for i in range(6):
        a = math.radians(i * 140)
        z = h * (0.4 + i * 0.1)
        rr = 0.25 + 0.05 * (i % 3)
        _branch(k, (0, 0, z - 0.15), (math.cos(a) * rr, math.sin(a) * rr, z), 0.025)
        k.blob((math.cos(a) * rr, math.sin(a) * rr, z + 0.05), 0.16, main if i % 2 else LEAF_L, sub=2, squash=1.2)
    k.blob((0, 0, h + 0.05), 0.15, LEAF_L, sub=2, squash=1.3)
    k.meta["seasonal"] = True


def sapling_a(k):
    sapling(k, 1.7)


def sapling_b(k):
    sapling(k, 2.1, LEAF_D)


def sapling_pine(k):
    pine(k, 2.2, 5, 0.7)


def dead_tree(k):
    """Gnarled bare trunk with forking branches and a few stones at the foot."""
    h = 3.4
    k.prism((0, 0, h / 2), 7, 0.35, 0.18, h, BARK, r=(0, 4, 0))
    for i in range(5):
        a = math.radians(i * 72 + 20)
        k.span((0, 0, 0.3), (math.cos(a) * 0.9, math.sin(a) * 0.9, 0.0), 0.2, 0.16, BARK)
    def fork(p, d, L, r, depth):
        e = p + d * L
        _branch(k, p, e, r)
        if depth == 0:
            return
        for s in (-1, 1):
            nd = (d + V(k.r(-0.6, 0.6), k.r(-0.6, 0.6), 0.3) * 0.8 + V(s * 0.5, 0, 0)).normalized()
            fork(e, nd, L * 0.65, r * 0.65, depth - 1)
    for a in (0, 130, 250):
        d = V(math.cos(math.radians(a)) * 0.6, math.sin(math.radians(a)) * 0.6, 1).normalized()
        fork(V(0, 0, h * 0.85), d, 1.2, 0.12, 2)
    for i in range(4):
        a = math.radians(i * 95 + 30)
        k.blob((math.cos(a) * 1.0, math.sin(a) * 1.0, 0.1), k.r(0.18, 0.3), ROCK, sub=2, squash=0.7)


def bush(k, main=LEAF, light=LEAF_L, dots=None, dot_n=10):
    for (x, y, z, r, m) in ((0, 0, 0.45, 0.55, main), (0.45, 0.1, 0.35, 0.42, main), (-0.42, -0.1, 0.35, 0.42, main),
                            (0.1, -0.35, 0.4, 0.4, light), (0.05, 0.3, 0.65, 0.38, light)):
        k.blob((x, y, z), r, m, sub=2, squash=0.85)
    if dots:
        for i in range(dot_n):
            a = k.r(0, 2 * math.pi)
            e = k.r(-0.3, 1.0)
            k.blob((math.cos(a) * 0.62 * math.cos(e), math.sin(a) * 0.62 * math.cos(e), 0.45 + math.sin(e) * 0.5), 0.07, dots, sub=0, jitter=0.1)
    k.meta["seasonal"] = True


def bush_flower(k):
    bush(k, LEAF, LEAF_L, "TB_FlowerWhite", 12)


def bush_berry(k):
    bush(k, LEAF_D, LEAF, "TB_FlowerRed", 14)


def bush_yellow(k):
    bush(k, "TB_LeafYellow", LEAF_L)


def bush_green(k):
    bush(k, LEAF_D, LEAF)


def _blades(k, n, h, spread, mats, lean=18):
    for i in range(n):
        a = k.r(0, 360)
        d = k.r(0, spread)
        x, y = math.cos(math.radians(a)) * d, math.sin(math.radians(a)) * d
        hh = h * k.r(0.7, 1.15)
        with k.at((x, y, 0), rz=a):
            k.frustum((0, 0, hh / 2), 0.07, 0.025, 0.012, 0.012, hh, k.pick(mats), r=(0, k.r(4, lean), 0), dx=0.0)


def grass_a(k):
    _blades(k, 16, 0.55, 0.18, ("TB_Grass", LEAF_L))


def grass_b(k):
    _blades(k, 22, 0.45, 0.25, ("TB_Grass", LEAF, LEAF_L))


def grass_dry(k):
    _blades(k, 14, 0.55, 0.2, ("TB_GrassDry", "TB_Wheat"))
    for i in range(5):
        a = math.radians(i * 72)
        k.box((math.cos(a) * 0.12, math.sin(a) * 0.12, 0.62), (0.03, 0.03, 0.14), "TB_Wheat", r=(0, 10, i * 72))


def fern(k, fronds=7, L=0.8):
    for i in range(fronds):
        a = 360 * i / fronds + k.r(-10, 10)
        with k.at((0, 0, 0.05), rz=a):
            pts = [V(0, 0, 0), V(L * 0.35, 0, L * 0.45), V(L * 0.75, 0, L * 0.45), V(L, 0, L * 0.2)]
            for j in range(3):
                k.span(pts[j], pts[j + 1], 0.03, 0.03, LEAF_D)
            for j in range(1, 7):
                t = j / 7
                seg = min(2, int(t * 3))
                p = pts[seg].lerp(pts[seg + 1], t * 3 - seg)
                w = 0.2 * (1 - t) + 0.05
                k.box((p.x, p.y, p.z + 0.01), (0.07, w * 2, 0.02), LEAF if j % 2 else LEAF_L, r=(0, -20 + 40 * t, 0))
    k.meta["seasonal"] = True


def fern_b(k):
    fern(k, 6, 0.65)


def _flower(k, x, y, h, petal, center, size=0.06):
    k.box((x, y, h / 2), (0.02, 0.02, h), LEAF)
    k.prism((x, y, h), 6, size, size, 0.02, petal)
    if center:
        k.prism((x, y, h + 0.015), 5, size * 0.4, size * 0.4, 0.02, center)


def flowers_daisy(k):
    for i in range(7):
        a = k.r(0, 6.28)
        d = k.r(0, 0.22)
        _flower(k, math.cos(a) * d, math.sin(a) * d, k.r(0.25, 0.45), "TB_FlowerWhite", "TB_FlowerYellow")
    _blades(k, 8, 0.25, 0.2, (LEAF, "TB_Grass"))


def flowers_bell(k):
    for i in range(5):
        a = k.r(0, 6.28)
        d = k.r(0, 0.18)
        x, y = math.cos(a) * d, math.sin(a) * d
        h = k.r(0.35, 0.5)
        k.box((x, y, h / 2), (0.02, 0.02, h), LEAF)
        for j in range(3):
            k.prism((x + 0.05 * (j - 1), y, h - 0.05 * j), 6, 0.035, 0.05, 0.08, "TB_FlowerBlue")
    _blades(k, 8, 0.3, 0.18, (LEAF, "TB_Grass"))


def flowers_lupine(k):
    for i in range(4):
        a = k.r(0, 6.28)
        d = k.r(0, 0.16)
        x, y = math.cos(a) * d, math.sin(a) * d
        h = k.r(0.45, 0.65)
        k.box((x, y, h / 2), (0.025, 0.025, h), LEAF)
        k.prism((x, y, h + 0.1), 6, 0.05, 0.015, 0.3, "TB_FlowerPurple")
    _blades(k, 10, 0.3, 0.2, (LEAF, "TB_Grass"))


def flowers_poppy(k):
    for i in range(5):
        a = k.r(0, 6.28)
        d = k.r(0, 0.18)
        _flower(k, math.cos(a) * d, math.sin(a) * d, k.r(0.25, 0.4), "TB_FlowerRed", "TB_WoodDarker", 0.055)
    _blades(k, 8, 0.25, 0.2, (LEAF, "TB_Grass"))


def wheat(k, n=26, spread=0.35):
    for i in range(n):
        a = k.r(0, 360)
        d = k.r(0, spread)
        x, y = math.cos(math.radians(a)) * d, math.sin(math.radians(a)) * d
        h = k.r(0.75, 1.0)
        tilt = k.r(2, 10)
        with k.at((x, y, 0), rz=a, ry=tilt):
            k.box((0, 0, h / 2), (0.02, 0.02, h), "TB_GrassDry")
            k.prism((0, 0, h + 0.09), 6, 0.03, 0.012, 0.18, "TB_Wheat")
            k.box((0.02, 0, h * 0.5), (0.06, 0.012, 0.2), "TB_Wheat", r=(0, 25, 0))


def wheat_patch(k):
    wheat(k, 26, 0.35)


def wheat_patch_wide(k):
    wheat(k, 44, 0.6)


def pebbles(k, n=14, spread=0.6, mats=(ROCK, ROCK_L, "TB_ClayLight")):
    for i in range(n):
        a = k.r(0, 6.28)
        d = k.r(0, spread)
        k.blob((math.cos(a) * d, math.sin(a) * d, 0.02), k.r(0.04, 0.08), k.pick(mats), sub=0, squash=0.5, jitter=0.2)


def pebbles_grey(k):
    pebbles(k, 14, 0.6, (ROCK, ROCK_L, ROCK_D))


def pebbles_warm(k):
    pebbles(k, 14, 0.6, (ROCK_L, "TB_ClayLight", ROCK))


def rock(k, r=0.45, squash=0.7, mat=ROCK):
    k.blob((0, 0, r * squash * 0.55), r, mat, sub=2, squash=squash, jitter=0.18)


def rocks_medium(k):
    rock(k, 0.5, 0.75)
    with k.at((0.65, 0.2, 0)):
        rock(k, 0.32, 0.7, ROCK_L)
    with k.at((0.2, -0.5, 0)):
        rock(k, 0.22, 0.6, ROCK)


def rock_a(k):
    rock(k, 0.55, 0.75)


def rock_b(k):
    rock(k, 0.4, 0.85, ROCK_L)


def boulder(k):
    k.blob((0, 0, 0.75), 1.25, ROCK, sub=2, squash=0.75, jitter=0.15)


def standing_stones(k):
    k.frustum((0, 0, 1.0), 0.6, 0.45, 0.35, 0.3, 2.0, ROCK, r=(0, 4, 8))
    k.frustum((0.7, 0.2, 0.55), 0.55, 0.4, 0.25, 0.25, 1.1, ROCK_L, r=(0, -8, 20))
    rock(k, 0.25, 0.7)


def rock_mossy(k):
    k.blob((0, 0, 0.5), 0.85, ROCK, sub=2, squash=0.72, jitter=0.15)
    k.blob((0.1, 0.05, 0.95), 0.55, "TB_Moss", sub=2, squash=0.35, jitter=0.2, sx=1.2)
    k.blob((-0.45, -0.3, 0.55), 0.3, "TB_Moss", sub=2, squash=0.3)


def stump(k):
    k.prism((0, 0, 0.3), 9, 0.45, 0.4, 0.6, BARK)
    k.prism((0, 0, 0.605), 9, 0.36, 0.36, 0.02, "TB_WoodLight")
    k.prism((0, 0, 0.615), 9, 0.2, 0.2, 0.02, "TB_WoodMedium")
    for i in range(5):
        a = math.radians(i * 72 + 15)
        k.span((0, 0, 0.3), (math.cos(a) * 0.8, math.sin(a) * 0.8, 0.0), 0.22, 0.18, BARK)
    k.blob((0.3, -0.35, 0.08), 0.12, "TB_Moss", sub=0, squash=0.5)


def fallen_log(k):
    k.prism((0, 0, 0.32), 8, 0.32, 0.3, 2.4, BARK, r=(0, 90, 0))
    for s in (-1, 1):
        k.prism((s * 1.21, 0, 0.32), 8, 0.26, 0.26, 0.02, "TB_WoodLight", r=(0, 90, 0))
    k.span((0.3, 0, 0.5), (0.8, 0.5, 0.9), 0.08, 0.08, BARK)
    k.span((-0.6, 0.1, 0.3), (-1.4, 0.7, 0.05), 0.12, 0.12, BARK)
    k.blob((0.1, 0.1, 0.62), 0.2, "TB_Moss", sub=0, squash=0.4)
    k.blob((-0.7, -0.15, 0.6), 0.15, "TB_Moss", sub=0, squash=0.4)


def sticks(k):
    for i in range(5):
        a = k.r(0, 360)
        c = V(k.r(-0.3, 0.3), k.r(-0.3, 0.3), 0.03)
        d = V(math.cos(math.radians(a)), math.sin(math.radians(a)), 0) * k.r(0.25, 0.4)
        k.span(c - d, c + d, 0.035, 0.035, BARK)
        k.span(c + d * 0.3, c + d * 0.3 + V(-d.y, d.x, 0) * 0.4, 0.02, 0.02, BARK)


def leaves(k):
    for i in range(10):
        a = k.r(0, 360)
        k.box((k.r(-0.4, 0.4), k.r(-0.4, 0.4), 0.01), (0.14, 0.07, 0.015),
              k.pick((LEAF, LEAF_L, "TB_LeafOrange", "TB_LeafYellow", "TB_LeafRed")), r=(0, 0, a))


def mushrooms(k):
    def shroom(x, y, h, r, cap, spots=False):
        k.prism((x, y, h / 2), 6, r * 0.3, r * 0.25, h, "TB_ClothCream")
        k.prism((x, y, h + r * 0.25), 8, r, r * 0.35, r * 0.5, cap)
        if spots:
            for i in range(4):
                a = i * 1.6
                k.blob((x + math.cos(a) * r * 0.55, y + math.sin(a) * r * 0.55, h + r * 0.35), r * 0.12, "TB_FlowerWhite", sub=0)
    shroom(0, 0, 0.25, 0.18, "TB_MushroomRed", True)
    shroom(0.25, 0.1, 0.14, 0.1, "TB_MushroomRed", True)
    shroom(-0.25, 0.05, 0.2, 0.14, "TB_MushroomCap")
    shroom(-0.1, -0.22, 0.12, 0.08, "TB_MushroomCap")
    for (x, y) in ((0.35, -0.2), (0.45, 0.05)):
        k.prism((x, y, 0.06), 6, 0.025, 0.02, 0.12, "TB_Chanterelle")
        k.prism((x, y, 0.15), 7, 0.03, 0.08, 0.07, "TB_Chanterelle")


def clover(k):
    for i in range(14):
        a = k.r(0, 6.28)
        d = k.r(0, 0.3)
        x, y = math.cos(a) * d, math.sin(a) * d
        h = k.r(0.08, 0.16)
        k.box((x, y, h / 2), (0.015, 0.015, h), LEAF)
        for j in range(3):
            b = j * 2.09 + a
            k.prism((x + math.cos(b) * 0.035, y + math.sin(b) * 0.035, h), 6, 0.035, 0.035, 0.012, LEAF_L if j else LEAF)
    for (x, y) in ((0.1, 0.05), (-0.15, -0.1)):
        _flower(k, x, y, 0.22, "TB_FlowerWhite", None, 0.04)


NATURE = [
    ("Tree_Broadleaf_Green", tree_green, "tree"), ("Tree_Broadleaf_Lime", tree_lime, "tree"),
    ("Tree_Broadleaf_Orange", tree_orange, "tree"), ("Tree_Birch", tree_birch, "tree"),
    ("Pine_Tall", pine_tall, "pine"), ("Pine_Medium", pine_mid, "pine"), ("Pine_Small", pine_small, "pine"),
    ("Tree_Sapling_A", sapling_a, "tree"), ("Tree_Sapling_B", sapling_b, "tree"), ("Pine_Sapling", sapling_pine, "pine"),
    ("DeadTree", dead_tree, "rock"),
    ("Tree_Bush_Flowering", bush_flower, "bush"), ("Tree_Bush_Berry", bush_berry, "bush"),
    ("Tree_Bush_Yellow", bush_yellow, "bush"), ("Tree_Bush_Green", bush_green, "bush"),
    ("Grass_Clump_A", grass_a, "ground"), ("Grass_Clump_B", grass_b, "ground"), ("Grass_Clump_Dry", grass_dry, "ground"),
    ("Tree_Fern_A", fern, "ground"), ("Tree_Fern_B", fern_b, "ground"),
    ("Flowers_Daisy", flowers_daisy, "ground"), ("Flowers_Bluebell", flowers_bell, "ground"),
    ("Flowers_Lupine", flowers_lupine, "ground"), ("Flowers_Poppy", flowers_poppy, "ground"),
    ("Wheat_Patch", wheat_patch, "ground"), ("Wheat_Patch_Wide", wheat_patch_wide, "ground"),
    ("Pebbles_Grey", pebbles_grey, "ground"), ("Pebbles_Warm", pebbles_warm, "ground"),
    ("Rocks_Medium", rocks_medium, "rock"), ("Rock_A", rock_a, "rock"), ("Rock_B", rock_b, "rock"),
    ("Boulder_Large", boulder, "rock"), ("Standing_Stones", standing_stones, "rock"), ("Rock_Mossy", rock_mossy, "rock"),
    ("Stump", stump, "rock"), ("Fallen_Log", fallen_log, "rock"), ("Sticks", sticks, "ground"),
    ("Leaves_Scatter", leaves, "ground"), ("Mushrooms", mushrooms, "ground"), ("Clover_Patch", clover, "ground"),
]
