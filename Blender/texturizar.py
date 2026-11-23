# Textures Cementerio.blend:
#  - ground faces (all floor tiles + the floor part of tomb objects) -> M_Suelo_Cementerio,
#    world-space UVs, one repeat every 12 units so tiles join seamlessly
#  - headstones / grave slabs / their broken pieces -> M_Tumba ("Textura tumbas.png"),
#    projected onto the clean concrete panel of one of the 4 atlas variants (random per tomb)
#  - lamps: relink the missing Lamp.png to the local folder and add Lamp_emission
# Usage: blender -b Cementerio.blend --python texturizar.py -- <out.blend>
import bpy, bmesh, random, sys, os
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]
TEX_DIR = 'C:/Users/mkmik/Desktop/VHS/Texturas Cementerio'
GROUND_REPEAT = 12.0
rnd = random.Random(5)


def image(path):
    img = bpy.data.images.load(path, check_existing=True); img.reload(); return img


def psx_material(name, img_path):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
        nt.nodes.remove(n)
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = image(img_path); t.interpolation = 'Closest'
    t.location = (-350, 250)
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 1.0
    nt.nodes.active = t
    return m


M_SUELO = psx_material('M_Suelo_Cementerio', TEX_DIR + '/T_Suelo_Cementerio.png')
M_TUMBA = psx_material('M_Tumba', TEX_DIR + '/Textura tumbas.png')

# atlas: 4 quadrants of 128 px; clean concrete panel inside each (pixels, top-left origin)
PANEL = (6, 4, 122, 36)          # x0, y0, x1, y1 inside a quadrant
VARIANTS = [(0, 0), (1, 0), (0, 1), (1, 1)]    # (1,1) is the dirty one


def panel_uv(q, a, b):
    qx, qy = q
    x0, y0, x1, y1 = PANEL
    px = qx * 128 + x0 + (x1 - x0) * a
    py = qy * 128 + y0 + (y1 - y0) * (1 - b)
    return (px / 256.0, 1 - py / 256.0)


targets = [o for o in bpy.data.objects if o.type == 'MESH' and len(o.material_slots) == 0
           and not o.name.startswith(('Cuerda', 'Character', 'Escombros'))]
n_ground = n_tomb = 0
for o in targets:
    me = o.data
    me.materials.clear(); me.materials.append(M_SUELO); me.materials.append(M_TUMBA)
    bm = bmesh.new(); bm.from_mesh(me)
    uv = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    mw = o.matrix_world
    wco = {v: mw @ v.co for v in bm.verts}
    rot = mw.to_3x3()
    ground, tomb = [], []
    for f in bm.faces:
        wn = (rot @ f.normal).normalized()
        if all(abs(wco[v].z) < 0.01 for v in f.verts) and wn.z > 0.9:
            ground.append(f)
        else:
            tomb.append(f)
    for f in ground:
        f.material_index = 0
        for l in f.loops:
            p = wco[l.vert]; l[uv].uv = (p.x / GROUND_REPEAT, p.y / GROUND_REPEAT)
    if tomb:
        pts = [wco[v] for f in tomb for v in f.verts]
        mn = Vector([min(p[i] for p in pts) for i in range(3)]); mx = Vector([max(p[i] for p in pts) for i in range(3)])
        ext = Vector([max(mx[i] - mn[i], 1e-4) for i in range(3)])
        q = rnd.choices(VARIANTS, weights=[3, 3, 3, 2])[0]
        for f in tomb:
            f.material_index = 1
            wn = (rot @ f.normal)
            ax = max(range(3), key=lambda i: abs(wn[i]))
            for l in f.loops:
                p = wco[l.vert]
                if ax == 2:
                    a, b = (p.x - mn.x) / ext.x, (p.y - mn.y) / ext.y
                elif ax == 0:
                    a, b = (p.y - mn.y) / ext.y, (p.z - mn.z) / ext.z
                else:
                    a, b = (p.x - mn.x) / ext.x, (p.z - mn.z) / ext.z
                l[uv].uv = panel_uv(q, min(max(a, 0), 1), min(max(b, 0), 1))
        n_tomb += 1
    if ground:
        n_ground += 1
    bm.to_mesh(me); bm.free()

# ---- lamps: relink texture, add emission map
lamp_img = image(TEX_DIR + '/Lamp.png')
emi_img = image(TEX_DIR + '/Lamp_emission.png')
lamp_mats = set()
for m in bpy.data.materials:
    if not m.node_tree:
        continue
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('Lamp.png'):
            n.image = lamp_img; n.interpolation = 'Closest'; lamp_mats.add(m)
for m in lamp_mats:
    nt = m.node_tree
    bsdf = next((n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None or any(n.type == 'TEX_IMAGE' and n.image == emi_img for n in nt.nodes):
        continue
    e = nt.nodes.new('ShaderNodeTexImage'); e.image = emi_img; e.interpolation = 'Closest'; e.location = (-350, -250)
    nt.links.new(e.outputs['Color'], bsdf.inputs['Emission Color'])
    bsdf.inputs['Emission Strength'].default_value = 2.0
for img in [i for i in bpy.data.images if i.name.startswith('Lamp.png') and i.users == 0]:
    bpy.data.images.remove(img)

# ---- rope texture moved into the textures folder: relink it
rope_img = image(TEX_DIR + '/T_Cuerda.png')
for m in bpy.data.materials:
    if m.node_tree:
        for n in m.node_tree.nodes:
            if n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith('T_Cuerda'):
                n.image = rope_img
for img in [i for i in bpy.data.images if i.name.startswith('T_Cuerda.png.') and i.users == 0]:
    bpy.data.images.remove(img)

print('TEX ground objects', n_ground, 'tomb objects', n_tomb, 'lamp materials', len(lamp_mats))
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
