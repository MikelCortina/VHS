# Star-shaped concrete memorial sculpture (Yugoslav "spomenik" style), PSX low poly, ~31 m tall:
#  - central slender column on a round plinth
#  - 5 rays around it (72 deg apart): each ray = a leaning leg split by a dark groove, rising to a knot
#    from which three blades shoot out: a tall near-vertical spike, a long diagonal arm and a short low arm
#  - optional round cobbled plaza (separate object "Plaza_Monumento", delete it if not needed)
# Textures: board-formed concrete (vertical plank marks) and wet cobbles, 128 px, point filtered.
# Usage: blender -b --python escultura.py -- <out.blend> <tex_dir>
import bpy, bmesh, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

OUT, TEX_DIR = sys.argv[sys.argv.index('--') + 1:][:2]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
os.makedirs(TEX_DIR, exist_ok=True)
rng = np.random.default_rng(1971)

# ---------------------------------------------------------------- textures
def concrete(N=128):
    """Board-formed concrete: vertical planks (8 px) with slightly different tones, form-tie holes, rain streaks."""
    base = np.array((150, 150, 146.0))
    img = base + rng.normal(0, 5, (N, N, 1))
    img += rng.normal(0, 1, (N // 16, N // 16)).repeat(16, 0).repeat(16, 1)[..., None] * 5
    for x0 in range(0, N, 8):                                   # planks
        img[:, x0:x0 + 8] *= rng.uniform(0.93, 1.05)
        img[:, x0] *= 0.86
    for _ in range(14):                                         # wood grain strokes
        x = int(rng.integers(0, N)); y0 = int(rng.integers(0, N)); ln = int(rng.integers(8, 40))
        img[(y0 + np.arange(ln)) % N, x] *= 0.92
    for y in (20, 84):                                          # form-tie holes
        for x in range(4, N, 32):
            img[y:y + 2, x:x + 2] = base * 0.45
    for _ in range(30):                                         # dark rain streaks running down
        x = int(rng.integers(0, N)); y0 = int(rng.integers(0, N)); ln = int(rng.integers(15, 70)); a = rng.uniform(0.75, 0.9)
        img[(y0 + np.arange(ln)) % N, x] *= a
    for _ in range(8):                                          # moss / damp patches
        cx, cy = rng.integers(0, N, 2); r = int(rng.integers(3, 8))
        yy, xx = np.ogrid[:N, :N]; m = ((xx - cx) % N) ** 2 + ((yy - cy) % N) ** 2 < r * r
        img[m] = img[m] * 0.7 + np.array((80, 88, 60.0)) * 0.3
    return img


def cobbles(N=128):
    """Wet cobblestones: irregular grey setts with dark mossy joints."""
    img = np.zeros((N, N, 3))
    pts = rng.integers(0, N, (60, 2)).astype(float)
    tones = rng.normal(118, 14, 60)
    yy, xx = np.mgrid[0:N, 0:N]
    d1 = np.full((N, N), 1e9); d2 = np.full((N, N), 1e9); idx = np.zeros((N, N), int)
    for i, (px, py) in enumerate(pts):
        dx = np.minimum(abs(xx - px), N - abs(xx - px)); dy = np.minimum(abs(yy - py), N - abs(yy - py))
        d = np.hypot(dx, dy)
        closer = d < d1
        d2 = np.where(closer, d1, np.minimum(d2, d)); idx = np.where(closer, i, idx); d1 = np.where(closer, d, d1)
    stone = tones[idx][..., None] * np.array((1.0, 1.0, 0.98)) + rng.normal(0, 6, (N, N, 1))
    edge = np.clip((d2 - d1) / 3.0, 0, 1)[..., None]
    joint = np.array((52, 58, 40.0)) + rng.normal(0, 5, (N, N, 1))
    img = stone * edge + joint * (1 - edge)
    img += np.clip(1 - (d1 / 6), 0, 1)[..., None] * 10        # wet highlight on top of each stone
    return img


TEX = {}
for name, arr in (('T_Hormigon_Monumento.png', concrete()), ('T_Adoquin_Plaza.png', cobbles())):
    p = os.path.join(TEX_DIR, name); save_png(np.clip(arr, 0, 255), p); TEX[name] = p


def material(name, tex=None, color=(0.5, 0.5, 0.5)):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    bsdf.inputs['Roughness'].default_value = 0.95
    if tex:
        t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX[tex], check_existing=True)
        t.interpolation = 'Closest'; t.location = (-350, 250)
        nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    else:
        bsdf.inputs['Base Color'].default_value = (*color, 1)
    m.diffuse_color = (*color, 1)
    return m


M_CON = material('M_Hormigon_Monumento', 'T_Hormigon_Monumento.png', (0.58, 0.58, 0.56))
M_DARK = material('M_Ranura_Monumento', None, (0.05, 0.05, 0.05))
M_PLAZA = material('M_Adoquin_Plaza', 'T_Adoquin_Plaza.png', (0.45, 0.45, 0.42))
CON, DARK = 0, 1

# ---------------------------------------------------------------- geometry helpers
bm = bmesh.new()


def hull8(p, mat=CON):
    """Closed box from 8 corners: bottom quad (ccw from outside-below), then the matching top quad."""
    v = [bm.verts.new(c) for c in p]
    faces = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]
    for f in faces:
        bm.faces.new([v[i] for i in f]).material_index = mat


def blade(root, tip, side, width, thick, mat=CON):
    """Tapered ridged blade from a diamond cross-section at `root` to a point at `tip`.
    side = direction of the blade's width (flat face), thick = ridge depth."""
    ax = (tip - root).normalized()
    s = (side - ax * side.dot(ax)).normalized()
    n = ax.cross(s).normalized()
    ring = [root + s * width / 2, root + n * thick / 2, root - s * width / 2, root - n * thick / 2]
    rv = [bm.verts.new(c) for c in ring]
    tv = bm.verts.new(tip)
    for i in range(4):
        bm.faces.new([rv[i], rv[(i + 1) % 4], tv]).material_index = mat
    bm.faces.new(rv[::-1]).material_index = mat


def cyl(c, r, z0, z1, seg=10, r1=None, mat=CON):
    r1 = r if r1 is None else r1
    lo = [bm.verts.new((c.x + r * math.cos(2 * math.pi * i / seg), c.y + r * math.sin(2 * math.pi * i / seg), z0)) for i in range(seg)]
    hi = [bm.verts.new((c.x + r1 * math.cos(2 * math.pi * i / seg), c.y + r1 * math.sin(2 * math.pi * i / seg), z1)) for i in range(seg)]
    for i in range(seg):
        j = (i + 1) % seg
        bm.faces.new([lo[i], lo[j], hi[j], hi[i]]).material_index = mat
    bm.faces.new(hi).material_index = mat
    bm.faces.new(lo[::-1]).material_index = mat


# ---------------------------------------------------------------- the sculpture (metres, Z up, centre at origin)
O = Vector((0, 0, 0))
cyl(O, 1.9, 0.0, 0.35, seg=16)                     # round plinth
cyl(O, 0.6, 0.35, 13.0, seg=4, r1=0.5)              # central square column
cyl(O, 1.5, 9.6, 13.2, seg=8, r1=1.1)               # slim core joining the three knots
KNOT_Z, KNOT_R = 10.0, 3.6
# 3 rays, 120 deg apart: two at the front opening to the sides (+-60 deg from the viewer at -Y), one at the back
for k, deg in enumerate((210, 330, 90)):
    a = math.radians(deg)
    rad = Vector((math.cos(a), math.sin(a), 0)); tan = Vector((-math.sin(a), math.cos(a), 0))
    # leg: two thick halves with a dark groove between them; wide wedge at the ground, almost vertical
    for sgn in ((-1, 1) if deg != 90 else ()):          # the back ray has no leg (open sky behind the column)
        def P(r, z, w0, w1):
            return rad * r + tan * (sgn * w0) + Vector((0, 0, z)), rad * r + tan * (sgn * w1) + Vector((0, 0, z))
        b_in0, b_in1 = P(3.0, 0.0, 0.15, 1.0); b_out0, b_out1 = P(8.2, 0.0, 0.15, 1.0)
        t_in0, t_in1 = P(KNOT_R - 1.0, KNOT_Z, 0.13, 0.85); t_out0, t_out1 = P(KNOT_R + 1.2, KNOT_Z, 0.13, 0.85)
        quad_b = [b_out0, b_out1, b_in1, b_in0] if sgn > 0 else [b_out1, b_out0, b_in0, b_in1]
        quad_t = [t_out0, t_out1, t_in1, t_in0] if sgn > 0 else [t_out1, t_out0, t_in0, t_in1]
        hull8(quad_b + quad_t)
    g = lambda r, z, w: rad * r + tan * w + Vector((0, 0, z))
    if deg != 90: hull8([g(7.6, 0, -0.15), g(7.6, 0, 0.15), g(3.4, 0, 0.15), g(3.4, 0, -0.15),      # groove infill, set back
           g(KNOT_R + 0.7, KNOT_Z, -0.13), g(KNOT_R + 0.7, KNOT_Z, 0.13), g(KNOT_R - 0.7, KNOT_Z, 0.13), g(KNOT_R - 0.7, KNOT_Z, -0.13)], DARK)
    # knot: chunky block on top of the leg where the blades start
    kc = rad * KNOT_R + Vector((0, 0, KNOT_Z + 1.3))
    hull8([kc + rad * 1.3 + tan * 1.1 - Vector((0, 0, 1.4)), kc + rad * 1.3 - tan * 1.1 - Vector((0, 0, 1.4)),
           kc - rad * 1.8 - tan * 1.1 - Vector((0, 0, 1.4)), kc - rad * 1.8 + tan * 1.1 - Vector((0, 0, 1.4)),
           kc + rad * 1.0 + tan * 0.9 + Vector((0, 0, 1.7)), kc + rad * 1.0 - tan * 0.9 + Vector((0, 0, 1.7)),
           kc - rad * 1.6 - tan * 0.9 + Vector((0, 0, 1.7)), kc - rad * 1.6 + tan * 0.9 + Vector((0, 0, 1.7))])
    # blades in the ray's vertical plane: tall spike, medium spike, big diagonal arm, short low arm
    blade(kc - rad * 0.6 + Vector((0, 0, 1.0)), rad * 5.8 + Vector((0, 0, 31.0)), rad, 3.2, 1.3)
    blade(kc + rad * 0.6 + Vector((0, 0, 0.8)), rad * 8.0 + tan * 0.6 + Vector((0, 0, 23.5)), rad, 2.0, 1.0)
    blade(kc + rad * 0.8 + Vector((0, 0, 0.3)), rad * 17.5 + Vector((0, 0, 19.0)), Vector((0, 0, 1)), 3.0, 1.2)
    blade(kc + rad * 0.8 - Vector((0, 0, 0.9)), rad * 14.0 + Vector((0, 0, 10.8)), Vector((0, 0, 1)), 2.0, 0.9)

# ---------------------------------------------------------------- UVs (world projection on each face's plane, 4 m per repeat)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.normal_update()
uvl = bm.loops.layers.uv.new('UVMap')
TILE = 4.0
for f in bm.faces:
    n = f.normal
    for l in f.loops:
        p = l.vert.co
        if abs(n.z) > 0.7:
            u, w = p.x, p.y
        else:
            t = Vector((0, 0, 1)).cross(n).normalized(); b = n.cross(t).normalized(); u, w = p.dot(t), p.dot(b)
        l[uvl].uv = (u / TILE, w / TILE)

col = bpy.data.collections.new('Escultura_Monumento'); bpy.context.scene.collection.children.link(col)
me = bpy.data.meshes.new('Escultura_Monumento'); bm.to_mesh(me); bm.free()
me.materials.append(M_CON); me.materials.append(M_DARK)
ob = bpy.data.objects.new('Escultura_Monumento', me); col.objects.link(ob)

# ---------------------------------------------------------------- plaza (separate object)
bp = bmesh.new(); uvp = bp.loops.layers.uv.new('UVMap')
R, SEG = 16.0, 32
c = bp.verts.new((0, 0, 0)); ring = [bp.verts.new((R * math.cos(2 * math.pi * i / SEG), R * math.sin(2 * math.pi * i / SEG), 0)) for i in range(SEG)]
for i in range(SEG):
    f = bp.faces.new([c, ring[i], ring[(i + 1) % SEG]])
    for l in f.loops:
        l[uvp].uv = (l.vert.co.x / 3.0, l.vert.co.y / 3.0)   # one repeat every 3 m
mp = bpy.data.meshes.new('Plaza_Monumento'); bp.to_mesh(mp); bp.free(); mp.materials.append(M_PLAZA)
pl = bpy.data.objects.new('Plaza_Monumento', mp); col.objects.link(pl)

print('BUILT', len(me.polygons), 'faces, size', tuple(round(d, 1) for d in ob.dimensions))
bpy.ops.wm.save_as_mainfile(filepath=OUT)
