# Edificio_Brutalista: the "beams" (vertical slots of the towers with their top caps, the left tower's side slit,
# and the colonnade pillars) get one new material, M_Viga, appended as the LAST slot so existing slots (and any
# per-slot assignments in Unity scenes) stay the same. UVs: world-space plane projection, 1 repeat every 2 m.
# Works in batch or inside Blender. Usage (batch): blender -b Edificio_Brutalista.blend --python separa_vigas.py -- <T_Viga.png>
import bpy, bmesh, sys
from mathutils import Vector
TEX = sys.argv[sys.argv.index('--') + 1:][0] if '--' in sys.argv else r'C:\Users\mkmik\Desktop\VHS\Texturas Edificio\T_Viga.png'
TILE = 2.0

ob = bpy.data.objects['Edificio_Brutalista']; me = ob.data
mode = bpy.context.object.mode if bpy.context.object else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

m = bpy.data.materials.get('M_Viga') or bpy.data.materials.new('M_Viga')
m.use_nodes = True; nt = m.node_tree
bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
    nt.nodes.remove(n)
t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX, check_existing=True)
if bpy.data.filepath:
    t.image.filepath = bpy.path.relpath(TEX)
t.interpolation = 'Closest'; t.location = (-350, 250)
nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 0.7
m.diffuse_color = (0.23, 0.24, 0.26, 1)
if 'M_Viga' not in [x.name for x in me.materials if x]:
    me.materials.append(m)
VI = [x.name if x else '' for x in me.materials].index('M_Viga')

bm = bmesh.new(); bm.from_mesh(me); bm.normal_update(); uvl = bm.loops.layers.uv.active
todo = set(bm.faces); isl = []
while todo:
    s = todo.pop(); i = [s]; st = [s]
    while st:
        f = st.pop()
        for v in f.verts:
            for g in v.link_faces:
                if g in todo: todo.remove(g); i.append(g); st.append(g)
    isl.append(i)


def is_beam(lo, hi):
    sx, sy, sz = (hi[k] - lo[k] for k in range(3))
    slot = sx < 2.0 and sz > 40 and sy > 10                         # tower slots (4.4-5.6, 34-35.8): tall, narrow, deep
    cap = 1.0 < sx < 2.0 and sz < 0.5 and sy > 10 and lo[2] > 50          # their caps at the top
    slit = sx < 1.3 and sy < 1.7 and sz > 40                        # left tower side slit and its plate
    pillar = lo[0] > 61 and abs(lo[2]) < 0.01 and sx < 1.5 and sy < 1.5 and sz > 3   # colonnade pillars
    return slot or cap or slit or pillar


n_isl = n_faces = 0
for i in isl:
    co = [v.co for f in i for v in f.verts]
    lo = [min(c[k] for c in co) for k in range(3)]; hi = [max(c[k] for c in co) for k in range(3)]
    if not is_beam(lo, hi):
        continue
    n_isl += 1
    for f in i:
        f.material_index = VI; n_faces += 1
        nrm = f.normal
        for l in f.loops:
            p = l.vert.co
            if abs(nrm.z) > 0.7: u, w = p.x, p.y
            else:
                tt = Vector((0, 0, 1)).cross(nrm).normalized(); b = nrm.cross(tt).normalized(); u, w = p.dot(tt), p.dot(b)
            l[uvl].uv = (u / TILE, w / TILE)
    print('BEAM', [round(x, 2) for x in lo], [round(x, 2) for x in hi])
bm.to_mesh(me); bm.free(); me.update()
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode=mode)
print('BEAMS', n_isl, 'pieces,', n_faces, 'faces -> M_Viga (slot', VI, ')')
