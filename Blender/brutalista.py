# Builds a brutalist stone-clad tower group (Soviet opera-house style):
#  - left slab tower with a dark vertical slot
#  - sloped "leaning" lower block carrying an overhanging upper box
#  - tallest twin tower with rounded outer corners and a deep slot between the halves
#  - right block with a darker penthouse band and 2x3 square vents
#  - low wing with a slanted front, dark colonnade and warm lamps
# Everything goes in the collection "Edificio_Brutalista" under the empty "Edificio_Brutalista".
# Usage: blender -b [file.blend] --python brutalista.py -- <out.blend> <tex_dir>
import bpy, bmesh, sys, os, math, random
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
OUT, TEX_DIR = argv[0], argv[1]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png

TILE = 4.0          # metres per texture repeat
rnd = random.Random(1979)
os.makedirs(TEX_DIR, exist_ok=True)

# ---------------------------------------------------------------- textures
def paint_stone(path, seed, base=(158, 160, 163), spread=9):
    """Tileable 256x256 ashlar: rows of rectangular stone tiles, grey with a cold tint."""
    N = 256; rng = np.random.default_rng(seed)
    img = np.zeros((N, N, 3))
    rows = 8; rh = N // rows
    for r in range(rows):
        cuts = sorted(set(int(c) for c in np.cumsum(rng.integers(22, 60, 12)) % N) | {int(rng.integers(0, N))})
        cuts = sorted(set(cuts))
        for i, c0 in enumerate(cuts):
            c1 = cuts[(i + 1) % len(cuts)]
            w = (c1 - c0) % N or N
            tone = rng.normal(0, spread)
            col = np.array(base) + tone + rng.normal(0, 1.5, 3) + np.array((0, 0, 2))
            xs = (c0 + np.arange(w)) % N
            block = col + rng.normal(0, 7, (rh, w, 1))                     # speckled stone
            if rng.random() < 0.25:                                        # a few darker veined tiles
                block *= 0.93
            img[r * rh:(r + 1) * rh][:, xs] = block
            img[r * rh:(r + 1) * rh, c0 % N] = np.array(base) * 0.8       # vertical joint
        img[r * rh] = np.array(base) * 0.74                                # horizontal joint
    # rain streaks running down (vertical, tileable)
    for _ in range(40):
        x = int(rng.integers(0, N)); y0 = int(rng.integers(0, N)); ln = int(rng.integers(20, 120))
        ys = (y0 + np.arange(ln)) % N
        img[ys, x] *= rng.uniform(0.8, 0.92)
    save_png(np.clip(img, 0, 255), path)


STONE_PNG = os.path.join(TEX_DIR, 'T_Piedra_Brutalista.png')
paint_stone(STONE_PNG, 7)


def material(name, img_path=None, color=(0.5, 0.5, 0.5), emission=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Roughness'].default_value = 0.95
    if img_path:
        t = nt.nodes.new('ShaderNodeTexImage')
        t.image = bpy.data.images.load(img_path, check_existing=True); t.image.reload()
        t.interpolation = 'Closest'; t.location = (-350, 250)
        nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    else:
        bsdf.inputs['Base Color'].default_value = (*color, 1)
    if emission:
        bsdf.inputs['Emission Color'].default_value = (*color, 1)
        bsdf.inputs['Emission Strength'].default_value = emission
    m.diffuse_color = (*color, 1)
    return m


M_STONE = material('M_Piedra_Brutalista', STONE_PNG, color=(0.62, 0.63, 0.65))
M_DARK = material('M_Ranura_Oscura', color=(0.035, 0.038, 0.042))
M_ROOF = material('M_Tejado_Oscuro', color=(0.16, 0.16, 0.17))
M_METAL = material('M_Metal_Barandilla', color=(0.25, 0.26, 0.27))
M_LAMP = material('M_Lampara_Calida', color=(1.0, 0.62, 0.32), emission=8.0)
MATS = [M_STONE, M_DARK, M_ROOF, M_METAL, M_LAMP]
STONE, DARK, ROOF, METAL, LAMP = range(5)

# ---------------------------------------------------------------- geometry helpers
bm = bmesh.new()
uvl = bm.loops.layers.uv.new('UVMap')


def add_face(vs, mat):
    f = bm.faces.new(vs); f.material_index = mat; return f


def prism(profile, z0, z1, mat=STONE, top_mat=None, bottom=False):
    """Vertical extrusion of a CCW 2D profile [(x, y), ...]."""
    lo = [bm.verts.new((x, y, z0)) for x, y in profile]
    hi = [bm.verts.new((x, y, z1)) for x, y in profile]
    n = len(profile)
    for i in range(n):
        j = (i + 1) % n
        add_face([lo[i], lo[j], hi[j], hi[i]], mat)
    add_face(hi, ROOF if top_mat is None else top_mat)
    if bottom:
        add_face(lo[::-1], mat)


def box(x0, y0, z0, x1, y1, z1, mat=STONE, top_mat=None):
    prism([(x0, y0), (x1, y0), (x1, y1), (x0, y1)], z0, z1, mat, mat if top_mat is None else top_mat, bottom=True)


def rrect(x0, y0, x1, y1, r=(0, 0, 0, 0), seg=8):
    """Rounded rectangle, CCW, corner radii (front-left, front-right, back-right, back-left); front = -Y."""
    pts = []
    corners = [((x0, y0), (1, 1), 180), ((x1, y0), (-1, 1), 270), ((x1, y1), (-1, -1), 0), ((x0, y1), (1, -1), 90)]
    for (cx, cy), (sx, sy), a0, rad in [(c[0], c[1], c[2], r[i]) for i, c in enumerate(corners)]:
        if rad <= 0:
            pts.append((cx, cy)); continue
        ox, oy = cx + sx * rad, cy + sy * rad
        for k in range(seg + 1):
            a = math.radians(a0 + 90 * k / seg)
            pts.append((ox + math.cos(a) * rad, oy + math.sin(a) * rad))
    return pts


def hull(verts8, mat=STONE):
    """Box from 8 explicit corners: bottom (fl, fr, br, bl) then top (fl, fr, br, bl)."""
    v = [bm.verts.new(p) for p in verts8]
    for a, b in ((0, 1), (1, 2), (2, 3), (3, 0)):
        add_face([v[a], v[b], v[b + 4], v[a + 4]], mat)
    add_face(v[4:], ROOF); add_face(v[:4][::-1], mat)


def railing(x0, y0, x1, y1, z, h=1.1, step=2.0):
    """Thin metal roof railing around a rectangle."""
    t = 0.06
    for (ax, ay), (bx, by) in (((x0, y0), (x1, y0)), ((x1, y0), (x1, y1)), ((x1, y1), (x0, y1)), ((x0, y1), (x0, y0))):
        L = math.hypot(bx - ax, by - ay); n = max(1, int(L / step))
        for k in range(n + 1):
            px, py = ax + (bx - ax) * k / n, ay + (by - ay) * k / n
            box(px - t, py - t, z, px + t, py + t, z + h, METAL)
        box(min(ax, bx) - t, min(ay, by) - t, z + h - 0.08, max(ax, bx) + t, max(ay, by) + t, z + h, METAL)


def antenna(x, y, z, h):
    box(x - 0.08, y - 0.08, z, x + 0.08, y + 0.08, z + h, METAL)
    box(x - 0.6, y - 0.05, z + h * 0.7, x + 0.6, y + 0.05, z + h * 0.7 + 0.1, METAL)
    box(x - 0.35, y - 0.25, z + h * 0.45, x + 0.35, y + 0.25, z + h * 0.45 + 0.7, METAL)   # dish box


def vent(x, y, z, s=1.6):
    """Square stone frame sticking out of a -Y facade with a dark hole."""
    box(x - s / 2, y - 0.45, z - s / 2, x + s / 2, y, z + s / 2, STONE)
    box(x - s * 0.32, y - 0.47, z - s * 0.32, x + s * 0.32, y - 0.3, z + s * 0.32, DARK)


# ---------------------------------------------------------------- the building (metres, front = -Y)
# A. left slab tower, rounded front-left corner, dark slot on the front face
H_A = 60
prism(rrect(0, 0, 4.4, 14, (1.5, 0, 0, 1.5)), 0, H_A)
prism(rrect(5.6, 0, 10, 14, (0, 0, 0, 0)), 0, H_A)
box(4.4, 0.9, 0, 5.6, 14, H_A - 0.4, DARK)                       # recessed slot
box(4.4, 0, H_A - 0.4, 5.6, 14, H_A, STONE)                      # slot capped at the top
box(-1.2, 3.0, 2.0, -0.01, 3.8, 50, DARK)                        # long side slit (left face)
box(-0.01, 2.6, 1.6, 0.0, 4.2, 50.4, STONE)
railing(0.3, 0.3, 9.7, 13.7, H_A)

# B. sloped lower block: the front leans back as it rises
hull([(10, -9, 0), (24.5, -9, 0), (24.5, 14, 0), (10, 14, 0),
      (10, -1.5, 36), (24.5, -1.5, 36), (24.5, 14, 36), (10, 14, 36)])
box(15.5, -9.3, 0, 17.3, -8.6, 3.2, DARK)                        # door
box(20, -6.2, 14, 20.6, -5.6, 15, DARK)                          # small slit window
box(20.6, -2.6, 31, 21.2, -1.9, 32, DARK)
# relief sign on the sloped face (stone blocks in a "5"-ish pattern)
for (rx, rz, w, h) in ((18.0, 21.2, 1.6, 0.4), (18.0, 19.6, 0.4, 1.8), (18.0, 19.4, 1.6, 0.4), (19.2, 17.8, 0.4, 1.8), (18.0, 17.6, 1.6, 0.4)):
    yf = -9 + (rz / 36) * 7.5
    box(rx, yf - 0.5, rz, rx + w, yf + 0.3, rz + h, DARK)

# C. overhanging upper box on top of B, with a deep ledge underneath
box(9.4, -1.0, 35.0, 25.2, 14, 36.0, DARK)                       # shadow ledge
box(9.0, -4.5, 36.0, 25.2, 14, 64.0)
box(14, -4.9, 44, 18.5, -4.4, 46, DARK)                          # dark relief panel / sign
for k in range(4):
    box(14.3 + k * 1.1, -5.3, 44.4, 14.9 + k * 1.1, -4.8, 45.6, STONE)
box(23.2, -4.9, 37.5, 25.2, -4.4, 39, STONE)                     # protruding block at the corner
railing(9.3, -4.2, 24.9, 13.7, 64)
antenna(11, 0, 64, 4.5); antenna(13.5, 10, 64, 3)

# D. tallest twin tower, big rounded outer corners, deep slot between the halves
H_D = 80
prism(rrect(24, -6, 34, 16, (5, 0, 0, 5), seg=10), 0, H_D)
prism(rrect(35.8, -6, 46, 16, (0, 5, 5, 0), seg=10), 0, H_D)
box(34, -4.8, 0, 35.8, 16, H_D - 0.2, DARK)
box(33.99, -6, H_D - 0.2, 35.81, 16, H_D, STONE)                 # top bridge over the slot
for z in (26, 54):                                                # faint horizontal band joints
    for x0, x1 in ((24.3, 34), (35.8, 45.7)):
        box(x0, -6.06, z, x1, -5.98, z + 0.12, DARK)
box(27.5, -6.4, 0, 29.3, -5.9, 3.4, DARK)                         # doors
box(40, -6.4, 0, 41.8, -5.9, 3.4, DARK)
railing(25, -5.7, 45, 15.7, H_D, step=2.5)
antenna(44, 12, H_D, 3.5)

# E. right block: darker penthouse band and 2x3 square vents
box(46, -2, 0, 64, 16, 56)
box(46.6, -1.2, 56, 63.4, 15.4, 62, ROOF, ROOF)                   # dark, slightly set-back top floors
box(46.4, -1.6, 59.4, 63.6, 15.6, 59.6, STONE)                    # thin band across the dark floors
for z in (49, 45.5):
    for x in (50, 55, 60):
        vent(x, -2, z)
# facade frame lines (thin dark grooves)
for x in (49.5, 53, 56.5, 60):
    box(x, -2.04, 4, x + 0.08, -1.98, 52, DARK)
for z in (30, 40):
    box(47, -2.04, z, 63, -1.98, z + 0.08, DARK)
railing(46.8, -1.0, 63.2, 15.2, 62)
antenna(52, 8, 62, 3.0); antenna(61, 3, 62, 2.2)

# F. low wing: slanted front over a dark colonnade, warm lamps under the cornice
hull([(62, -14, 4), (96, -14, 4), (96, 14, 4), (62, 14, 4),
      (62, -10, 15), (96, -10, 15), (96, 14, 15), (62, 14, 15)])
box(62, -14.4, 3.4, 96, 14, 4.0)                                  # cornice slab
box(62.5, -11, 0, 95.5, 14, 3.4, DARK)                            # shadowed colonnade
for k in range(9):
    x = 64 + k * 3.9
    box(x, -13.5, 0, x + 0.9, -12.6, 3.4)                         # columns
    box(x + 1.7, -12.3, 4.6, x + 2.3, -11.6, 5.3, LAMP)          # lamps up-lighting the slant
box(63, -13.55, 8.0, 95, -12.3, 9.2, DARK)                        # dark lettering strip

# ---------------------------------------------------------------- mesh, UVs, object
bm.normal_update()
for f in bm.faces:
    n = f.normal
    ax = max(range(3), key=lambda i: abs(n[i]))
    for l in f.loops:
        p = l.vert.co
        if ax == 2:
            u, v = p.x, p.y
        elif ax == 0:
            u, v = (p.y if n.x > 0 else -p.y), p.z
        else:
            u, v = (-p.x if n.y > 0 else p.x), p.z
        l[uvl].uv = (u / TILE, v / TILE)

me = bpy.data.meshes.new('Edificio_Brutalista')
bm.normal_update(); bm.to_mesh(me); bm.free()
for m in MATS:
    me.materials.append(m)

col = bpy.data.collections.get('Edificio_Brutalista') or bpy.data.collections.new('Edificio_Brutalista')
if col.name not in bpy.context.scene.collection.children:
    bpy.context.scene.collection.children.link(col)
ob = bpy.data.objects.new('Edificio_Brutalista', me)
col.objects.link(ob)
for p in me.polygons:
    p.use_smooth = False
print('BUILT', len(me.polygons), 'faces, size', tuple(round(d, 1) for d in ob.dimensions))
bpy.ops.wm.save_as_mainfile(filepath=OUT)
