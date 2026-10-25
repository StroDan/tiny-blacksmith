# Tiny Blacksmith - the prop library exported as individual assets (06_city_props.png + the props strip and the
# modular forge setup of 04_blacksmith_house_turnaround.png). Builders live in tb_props.
import math
import tb_props as P
import tb_arch as A


def forge_setup(k):
    """04 FORGE SETUP (MODULAR) / 06 Blacksmith Forge: stone hearth with an arched fire mouth, iron hood and a stone
    chimney, a slate-roofed timber shelter, anvil, quench bucket and crate."""
    # chimney + hearth
    P.stone_stack(k, (-0.9, 0.5, 0), 1.0, 4.6, courses=9)
    k.box((-0.9, 0.5, 4.66), (1.1, 1.1, 0.12), A.SD)
    for i in range(3):
        for j in range(3):
            k.box((-0.6 + j * 0.62 - 0.3, 0.1, 0.2 + i * 0.36), (0.6, 1.3, 0.34), A.SL if (i + j) % 2 else A.SM)
    k.box((0.02, 0.1, 1.18), (1.95, 1.4, 0.12), A.SD)
    k.box((0.0, -0.56, 0.62), (0.9, 0.05, 0.6), A.WX)
    k.box((0.0, -0.45, 0.55), (0.75, 0.25, 0.42), "TB_Fire")
    k.box((0.0, 0.1, 1.28), (1.3, 1.1, 0.08), "TB_Coal")
    k.frustum((0.0, 0.05, 2.0), 1.7, 1.4, 0.55, 0.55, 1.2, A.IRON)
    k.box((0.0, 0.05, 2.9), (0.45, 0.45, 0.8), A.IRON)
    k.meta.setdefault("glow", []).append([0.0, -0.6, 0.6])
    # shelter
    for (x, y) in ((1.55, -1.1), (1.55, 1.1), (-1.6, -1.1)):
        k.box((x, y, 1.35), (0.18, 0.18, 2.7), A.WM)
    k.box((0.0, -1.1, 2.7), (3.4, 0.16, 0.16), A.WM)
    k.box((1.55, 0, 2.7), (0.16, 2.4, 0.16), A.WM)
    with k.at((0, 1.2, 0)):
        A.shed_roof(k, -1.9, 1.9, 0.0, 2.6, 3.2, 2.55, "slate", style="slate", over=0.3, tile=0.55)
    for i in range(3):
        k.box((1.0 + i * 0.15, -1.1, 2.2), (0.03, 0.03, 0.8), A.IRON, r=(0, (i - 1) * 5, 0))
    with k.at((0.2, -1.6, 0), rz=90):
        P.anvil(k)
    with k.at((-1.2, -1.5, 0)):
        P.bucket(k)
    with k.at((1.1, -1.6, 0), rz=10):
        P.crate(k, 0.5)
    with k.at((1.1, 0.3, 0)):
        P.barrel(k, 0.8, 0.3, water=True)


PROPS = [
    ("Fence_Straight", lambda k: P.fence(k)),
    ("Fence_Corner", lambda k: P.fence_corner(k)),
    ("Fence_Gate", lambda k: P.fence_gate(k)),
    ("Bench", lambda k: P.bench(k)),
    ("LanternPost", lambda k: P.lantern_post(k)),
    ("Sign_Blacksmith", lambda k: P.hanging_sign(k, "anvil", iron=True)),
    ("SignPost", lambda k: P.sign_post(k)),
    ("Bridge_Railing", lambda k: P.bridge_railing(k)),
    ("Barrel_Large", lambda k: P.barrel(k, 0.95, 0.36)),
    ("Barrel_Small", lambda k: P.barrel(k, 0.72, 0.28)),
    ("Crate_Large", lambda k: P.crate(k, 0.8)),
    ("Crate_Small", lambda k: P.crate(k, 0.55)),
    ("Sacks", lambda k: _sacks(k)),
    ("Firewood_Pile", lambda k: P.firewood_pile(k)),
    ("Wheelbarrow", lambda k: P.wheelbarrow(k)),
    ("Cart", lambda k: P.cart(k)),
    ("Market_Stall", lambda k: P.market_stall(k)),
    ("Stone_Well", lambda k: P.well(k)),
    ("Water_Trough", lambda k: P.water_trough(k)),
    ("Stone_Bridge_Small", lambda k: P.stone_bridge(k)),
    ("Forge_Setup", forge_setup),
    ("Anvil", lambda k: P.anvil(k)),
    ("Grindstone", lambda k: P.grindstone(k)),
    ("Tool_Rack", lambda k: P.tool_rack(k)),
    ("Metal_Ingots", lambda k: P.ingots(k)),
    ("Quench_Barrel", lambda k: P.barrel(k, 0.9, 0.36, water=True)),
    ("Coal_Bin", lambda k: P.coal_bin(k)),
    ("Horseshoes", lambda k: P.horseshoes(k)),
    ("Chains", lambda k: P.chain(k)),
    ("Bucket", lambda k: P.bucket(k)),
    ("Stool", lambda k: P.stool(k)),
    ("Bottles", lambda k: _bottles(k)),
    ("Jugs", lambda k: _jugs(k)),
    ("Lantern_Small", lambda k: _small_lantern(k)),
    ("Nails_Crate", lambda k: P.crate(k, 0.5, 0.3, P.crate_lid_nails)),
    ("Nails_Loose", lambda k: P.nails(k, 6)),
    ("Cloth_And_Mug", lambda k: P.cloth_and_mug(k)),
    ("Cart_Wheel", lambda k: _wheel(k)),
    ("Flower_Box", lambda k: P.flower_box(k, 1.0, ("TB_FlowerWhite",))),
]


def _sacks(k):
    P.sack(k, 1.0)
    with k.at((0.45, 0.15, 0), rz=40):
        P.sack(k, 0.8)


def _bottles(k):
    P.bottle(k, "TB_GlassGreen", 1.2)
    with k.at((0.16, 0.05, 0)):
        P.bottle(k, "TB_GlassBrown", 1.0)
    with k.at((0.3, -0.06, 0)):
        P.bottle(k, "TB_GlassGreen", 0.8)


def _jugs(k):
    P.jug(k, "TB_Clay", 1.3)
    with k.at((0.32, 0.05, 0)):
        P.jug(k, "TB_ClayLight", 0.9)


def _small_lantern(k):
    P.lantern(k, s=1.2)
    k.meta.setdefault("lights", []).append([0, 0, 0.2])


def _wheel(k):
    with k.at((0, 0, 0.47), rx=0):
        P.wheel(k, 0.47)
