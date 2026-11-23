import bpy, bmesh, os, sys
from mathutils import Matrix
argv = sys.argv[sys.argv.index('--') + 1:]
out = argv[0]
SCALE = float(argv[1]) if len(argv) > 1 else 1.0
os.makedirs(os.path.dirname(out), exist_ok=True)

col = bpy.data.collections['Skyscraper_Details']
src = [o for o in col.objects if o.type == 'MESH']

# one merged copy in world space (the .blend itself is never saved)
bm = bmesh.new()
for o in src:
    me = o.data.copy(); me.transform(Matrix.Scale(SCALE, 4) @ o.matrix_world)
    bm.from_mesh(me); bpy.data.meshes.remove(me)
mats = list(src[0].data.materials)

# box-projected UVs: 1 texture tile every 8 units
uv = bm.loops.layers.uv.new('UVMap')
T = 3.0  # one texture tile per ~floor (3 m)
for f in bm.faces:
    n = f.normal; ax = max(range(3), key=lambda i: abs(n[i]))
    for l in f.loops:
        c = l.vert.co
        if ax == 0: l[uv].uv = (c.y / T * (1 if n.x > 0 else -1), c.z / T)
        elif ax == 1: l[uv].uv = (c.x / T * (-1 if n.y > 0 else 1), c.z / T)
        else: l[uv].uv = (c.x / T, c.y / T)

# ---- glass: each pane gets one cell of the 4x2 atlas (T_Ventana.png) ----
import random
from mathutils import Vector
rnd = random.Random(7)
UP = Vector((0, 0, 1))
bm.normal_update()


def face_axes(f):
    n = f.normal
    if abs(n.z) > 0.7:
        return Vector((1, 0, 0)), Vector((0, 1, 0))
    r = UP.cross(n); r.normalize()
    return r, UP


# split long glazing strips into roughly window-shaped pieces
long_edges = []
for f in [f for f in bm.faces if f.material_index == 2 and len(f.verts) == 4]:
    r, u = face_axes(f)
    us = [v.co.dot(r) for v in f.verts]; vs = [v.co.dot(u) for v in f.verts]
    w, h = max(us) - min(us), max(vs) - min(vs)
    if abs(f.normal.z) < 0.3 and h > 1.0 and w / h > 2.2:
        n = round(w / h / 1.6)
        es = [e for e in f.edges if abs((e.verts[0].co - e.verts[1].co).dot(r)) > 0.5 * w]
        long_edges.append((es, n))
for es, n in long_edges:
    if all(e.is_valid for e in es):
        bmesh.ops.subdivide_edges(bm, edges=es, cuts=n - 1, use_grid_fill=True)
bm.normal_update()

for f in bm.faces:
    if f.material_index != 2:
        continue
    r, u = face_axes(f)
    us = [l.vert.co.dot(r) for l in f.loops]; vs = [l.vert.co.dot(u) for l in f.loops]
    u0, u1, v0, v1 = min(us), max(us), min(vs), max(vs)
    # cells: top row = glass, clean / curtains / half blind / dark; bottom row = blinds lowered
    # (full, 3/4, full brown, mixed). No lit windows. ~38% of the windows get a lowered blind.
    cell = rnd.choices(range(8), weights=[24, 14, 14, 10, 14, 10, 7, 7])[0]
    col_i, row_i = cell % 4, cell // 4
    cu0 = col_i / 4; cv0 = 0.5 if row_i == 0 else 0.0   # top row of the image = upper half of UV space
    e = 0.002
    for l in f.loops:
        a = (l.vert.co.dot(r) - u0) / max(u1 - u0, 1e-6)
        b = (l.vert.co.dot(u) - v0) / max(v1 - v0, 1e-6)
        l[uv].uv = (cu0 + e + a * (0.25 - 2 * e), cv0 + e + b * (0.5 - 2 * e))

me = bpy.data.meshes.new('Skyscraper')
bm.to_mesh(me); bm.free()
for m in mats: me.materials.append(m)
for p in me.polygons: p.use_smooth = False
obj = bpy.data.objects.new('Skyscraper', me)
bpy.context.scene.collection.objects.link(obj)

bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True); bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.fbx(
    filepath=out, use_selection=True, object_types={'MESH'},
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
    bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False, path_mode='STRIP')
print('EXPORTED', out, 'verts', len(me.vertices), 'tris', sum(len(p.vertices) - 2 for p in me.polygons), 'mats', [m.name for m in mats])
