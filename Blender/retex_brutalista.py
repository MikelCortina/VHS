# Re-textures Edificio_Brutalista in the project's PSX style (same as Skyscraper / Cementerio):
#  - small textures (32-128 px), point filtering, chunky texels (~32 px/m on the stone)
#  - every material gets an image: stone, dark slot concrete, tar roof, metal (project's T_Metal), lamp + emission
#  - world-space box-projected UVs per material (one tile = TILE metres), same mapping as the skyscraper,
#    recomputed on the CURRENT mesh so the user's own edits are kept
# Usage: blender -b Edificio_Brutalista.blend --python retex_brutalista.py -- <tex_dir>
import bpy, bmesh, sys, os, shutil
import numpy as np
from mathutils import Vector

TEX_DIR = sys.argv[sys.argv.index('--') + 1:][0]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
os.makedirs(TEX_DIR, exist_ok=True)
rng = np.random.default_rng(1979)


# ---------------------------------------------------------------- textures
def stone(N=128, rows=8):
    """Grey ashlar cladding: rows of rectangular slabs, subtle tone changes, darker joints, rain streaks."""
    base = np.array((150, 152, 156.0)); img = np.zeros((N, N, 3)); rh = N // rows
    for r in range(rows):
        cuts = sorted(set(int(c) for c in np.cumsum(rng.integers(12, 30, 10)) % N))
        for i, c0 in enumerate(cuts):
            w = (cuts[(i + 1) % len(cuts)] - c0) % N or N
            col = base + rng.normal(0, 8) + np.array((0, 0, 2.0))
            if rng.random() < 0.2:
                col *= 0.92
            xs = (c0 + np.arange(w)) % N
            img[r * rh:(r + 1) * rh][:, xs] = col + rng.normal(0, 6, (rh, w, 1))
            img[r * rh:(r + 1) * rh, c0] = base * 0.78
        img[r * rh] = base * 0.72
    for _ in range(18):
        x = int(rng.integers(0, N)); ys = (int(rng.integers(0, N)) + np.arange(int(rng.integers(10, 60)))) % N
        img[ys, x] *= 0.88
    return img


def noise_tex(N, col, amp, speck=0.0, speck_col=None):
    img = np.array(col, float) + rng.normal(0, amp, (N, N, 1))
    blobs = rng.normal(0, 1, (N // 8, N // 8)).repeat(8, 0).repeat(8, 1)[..., None]
    img += blobs * amp * 0.8
    if speck:
        m = rng.random((N, N)) < speck; img[m] = speck_col
    return img


def lamp(N=32):
    y, x = np.mgrid[0:N, 0:N]; d = np.hypot(x - N / 2 + 0.5, y - N / 2 + 0.5) / (N / 2)
    glow = np.clip(1.15 - d, 0, 1)[..., None]
    col = np.array((70, 55, 40.0)) * (1 - glow) + np.array((255, 196, 120.0)) * glow
    emi = np.array((255, 170, 90.0)) * np.clip(1.0 - d * 1.2, 0, 1)[..., None]
    return col, emi


files = {}
def save(name, arr):
    p = os.path.join(TEX_DIR, name); save_png(np.clip(arr, 0, 255), p); files[name] = p


save('T_Piedra_Brutalista.png', stone())
save('T_Ranura_Oscura.png', noise_tex(64, (24, 26, 29), 4))
save('T_Tejado_Oscuro.png', noise_tex(64, (52, 52, 54), 7, speck=0.04, speck_col=(84, 82, 78)))
lc, le = lamp()
save('T_Lampara.png', lc); save('T_Lampara_Emision.png', le)
METAL_SRC = 'C:/UnityProjects/VHS/Assets/Textures/T_Metal.png'      # the project's own metal
files['T_Metal.png'] = os.path.join(TEX_DIR, 'T_Metal.png'); shutil.copyfile(METAL_SRC, files['T_Metal.png'])


# ---------------------------------------------------------------- materials
def rel(p):
    return bpy.path.relpath(p) if bpy.data.filepath else p


def set_image(mat, name, emission=None, strength=0.0):
    mat.use_nodes = True; nt = mat.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
        nt.nodes.remove(n)
    img = bpy.data.images.load(files[name], check_existing=True); img.reload(); img.filepath = rel(files[name])
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = img; t.interpolation = 'Closest'; t.location = (-350, 250)
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1.0
    bsdf.inputs['Emission Strength'].default_value = 0.0
    if emission:
        ei = bpy.data.images.load(files[emission], check_existing=True); ei.reload(); ei.filepath = rel(files[emission])
        e = nt.nodes.new('ShaderNodeTexImage'); e.image = ei; e.interpolation = 'Closest'; e.location = (-350, -250)
        nt.links.new(e.outputs['Color'], bsdf.inputs['Emission Color'])
        bsdf.inputs['Emission Strength'].default_value = strength
    nt.nodes.active = t


# material name -> (texture, metres per tile, emission)
SPEC = {
    'M_Piedra_Brutalista': ('T_Piedra_Brutalista.png', 4.0, None),
    'M_Ranura_Oscura':     ('T_Ranura_Oscura.png', 3.0, None),
    'M_Tejado_Oscuro':     ('T_Tejado_Oscuro.png', 3.0, None),
    'M_Metal_Barandilla':  ('T_Metal.png', 2.0, None),
    'M_Lampara_Calida':    ('T_Lampara.png', None, 'T_Lampara_Emision.png'),   # None: whole image per face
}
for mname, (tex, _, emi) in SPEC.items():
    m = bpy.data.materials.get(mname)
    if m:
        set_image(m, tex, emi, 6.0 if emi else 0.0)

# ---------------------------------------------------------------- UVs (world-space box projection, like the skyscraper)
ob = bpy.data.objects['Edificio_Brutalista']
me = ob.data; mw = ob.matrix_world; rot = mw.to_3x3()
bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
uv = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
tile_of = [SPEC.get(m.name if m else '', (None, 4.0, None))[1] for m in me.materials] or [4.0]
fixed = 0
for f in bm.faces:
    T = tile_of[min(f.material_index, len(tile_of) - 1)]
    n = (rot @ f.normal).normalized()
    pts = [mw @ l.vert.co for l in f.loops]
    if n.length < 0.5 or abs(n.z) > 0.7:         # floors / roofs (and degenerate faces): plan view
        uvs = [(p.x, p.y) for p in pts]
    else:
        # walls, sloped or not: project onto the face's own plane, anchored to world space.
        # u runs horizontally along the wall, v up the wall (along the slope if it leans),
        # so nothing stretches; for straight walls this is the same as the box projection.
        t = Vector((0, 0, 1)).cross(n).normalized()
        b = n.cross(t).normalized()
        uvs = [(p.dot(t), p.dot(b)) for p in pts]
    if T is None:                       # lamps: the whole glow image on each face
        u0, v0 = min(a for a, _ in uvs), min(b for _, b in uvs)
        su = max(max(a for a, _ in uvs) - u0, 1e-4); sv = max(max(b for _, b in uvs) - v0, 1e-4)
        uvs = [((a - u0) / su, (b - v0) / sv) for a, b in uvs]
    else:
        uvs = [(a / T, b / T) for a, b in uvs]
    for l, c in zip(f.loops, uvs):
        l[uv].uv = c
    fixed += 1
bm.to_mesh(me); bm.free()
for m in [m for m in bpy.data.materials if m.users == 0]:
    bpy.data.materials.remove(m)
print('RETEX faces', fixed, 'textures', sorted(files))
bpy.ops.wm.save_mainfile()
