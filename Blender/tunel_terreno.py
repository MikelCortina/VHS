# Tunnel scene (Sewer.blend): textures the mountain and the earth of the object "Plane" (NOT the tunnel building).
#  - Plane has two parts: the ground (rolling terrain) and the vertical cliff wall behind the tunnel.
#  - Faces steeper than ROCK_SLOPE (and the whole cliff) -> M_Roca_Montana; the rest -> M_Tierra_Camino.
#  - UVs: world-space plane projection per face like the rest of the project (floors use world X/Y),
#    rock 1 repeat / 4 m, earth 1 repeat / 3 m. Uses the object's world matrix (the Plane is rotated).
# Works inside Blender (Scripting tab -> Open -> Run Script) or in batch:
#   blender -b Sewer.blend --python tunel_terreno.py -- [save]
import bpy, bmesh, sys
from mathutils import Vector
TEXDIR = r'C:\UnityProjects\VHS\Assets\Textures\Tunel'
OBJ = 'Plane'
ROCK_SLOPE = 0.55          # |normal.z| below this = steep = rock
TILE_ROCK, TILE_EARTH = 4.0, 3.0

ob = bpy.data.objects[OBJ]; me = ob.data
ctx_ob = bpy.context.view_layer.objects.active
mode = ctx_ob.mode if ctx_ob else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')


def make_mat(name, tex, base):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True; nt = m.node_tree
    bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
        nt.nodes.remove(n)
    t = nt.nodes.new('ShaderNodeTexImage'); t.location = (-350, 250)
    t.image = bpy.data.images.load(tex, check_existing=True); t.interpolation = 'Closest'
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.95
    m.diffuse_color = base
    return m


mr = make_mat('M_Roca_Montana', TEXDIR + r'\T_Roca_Montana.png', (0.33, 0.32, 0.30, 1))
mt = make_mat('M_Tierra_Camino', TEXDIR + r'\T_Tierra_Camino.png', (0.27, 0.22, 0.17, 1))
names = [x.name if x else '' for x in me.materials]
for m in (mt, mr):
    if m.name not in names:
        me.materials.append(m); names.append(m.name)
IR, IT = names.index('M_Roca_Montana'), names.index('M_Tierra_Camino')

mw = ob.matrix_world; rot = mw.to_3x3().normalized()
bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')

# islands: the cliff wall is the one whose world height span is the largest
todo = set(bm.faces); isl = []
while todo:
    s = todo.pop(); i = [s]; st = [s]
    while st:
        f = st.pop()
        for v in f.verts:
            for g in v.link_faces:
                if g in todo: todo.remove(g); i.append(g); st.append(g)
    isl.append(i)
def zspan(i):
    z = [(mw @ v.co).z for f in i for v in f.verts]; return max(z) - min(z)
cliff = set(max(isl, key=zspan)) if len(isl) > 1 else set()

nr = ne = 0
for f in bm.faces:
    n = (rot @ f.normal).normalized()
    rock = f in cliff or abs(n.z) < ROCK_SLOPE
    f.material_index = IR if rock else IT
    tile = TILE_ROCK if rock else TILE_EARTH
    nr += rock; ne += not rock
    if abs(n.z) > 0.7:
        tt, bb = Vector((1, 0, 0)), Vector((0, 1, 0))
    else:
        tt = Vector((0, 0, 1)).cross(n).normalized(); bb = n.cross(tt).normalized()
    for l in f.loops:
        p = mw @ l.vert.co
        l[uvl].uv = (p.dot(tt) / tile, p.dot(bb) / tile)
bm.to_mesh(me); bm.free(); me.update()
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode=mode)
print('TERRENO rock faces', nr, '| earth faces', ne, '| cliff faces', len(cliff))
if '--' in sys.argv and 'save' in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.save_mainfile(); print('SAVED', bpy.data.filepath)
