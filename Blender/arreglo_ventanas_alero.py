# Two fixes on Edificio_Brutalista. Runs in batch (-b file --python this) or inside Blender (Run Script).
#  1. Windows: the dark "glass" box was stuck 2-3 cm in front of the stone frame -> z-fighting in Unity.
#     Each window becomes a real recess: the frame's front face is inset to the glass size and pushed RECESS m
#     inwards; the inner walls are M_Marco_Ventana, the bottom is the dark glass. The old glass box is deleted.
#  2. Low wing (colonnade, x > WING_X): everything above COL_TOP goes up DZ; columns and the dark back stretch.
# Affected faces get world-space plane UVs (same style as the rest). Safe to run twice (windows already fixed are skipped).
import bpy, bmesh
from mathutils import Vector

RECESS = 0.30
WING_X = 61.5          # pieces whose min x is beyond this are the low wing
COL_TOP = 3.4          # current colonnade height (m)
NEW_COL_TOP = 6.0      # wanted height
DZ = NEW_COL_TOP - COL_TOP
TILE = {'M_Piedra_Brutalista': 4.0, 'M_Ranura_Oscura': 3.0, 'M_Tejado_Oscuro': 3.0,
        'M_Metal_Barandilla': 2.0, 'M_Marco_Ventana': 2.0}

ob = bpy.data.objects['Edificio_Brutalista']
mode = bpy.context.object.mode if bpy.context.object else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
me = ob.data; names = [m.name if m else '' for m in me.materials]
STONE, DARK, FRAME = (names.index(n) for n in ('M_Piedra_Brutalista', 'M_Ranura_Oscura', 'M_Marco_Ventana'))
bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
uvl = bm.loops.layers.uv.active


def islands(faces):
    todo = set(faces); out = []
    while todo:
        s = todo.pop(); isl = [s]; st = [s]
        while st:
            f = st.pop()
            for v in f.verts:
                for g in v.link_faces:
                    if g in todo:
                        todo.remove(g); isl.append(g); st.append(g)
        out.append(isl)
    return out


def bbox(fs):
    co = [v.co for f in fs for v in f.verts]
    return Vector([min(c[i] for c in co) for i in range(3)]), Vector([max(c[i] for c in co) for i in range(3)])


touched = set()

# ---------------------------------------------------------------- 1. windows -> recesses
stone_fronts = [f for f in bm.faces if f.material_index == STONE and len(f.verts) == 4 and 1.0 < f.calc_area() < 6.0]
glass_islands = [i for i in islands([f for f in bm.faces if f.material_index in (DARK, FRAME)])
                 if max((b - a)[k] for a, b in [bbox(i)] for k in range(3)) < 2.5]
to_delete = []; fixed = 0
for isl in glass_islands:
    glass = max((f for f in isl if f.material_index == DARK), key=lambda f: f.calc_area(), default=None)
    if glass is None:
        continue
    n = glass.normal.copy(); c = glass.calc_center_median()
    host = None
    for s in stone_fronts:
        if s.normal.dot(n) > 0.99 and abs((c - s.calc_center_median()).dot(n)) < 0.1:
            lo, hi = bbox([s]); pad = Vector((0.06, 0.06, 0.06))
            if all(lo[k] - pad[k] <= c[k] <= hi[k] + pad[k] for k in range(3)) and (c - s.calc_center_median()).length < 0.6:
                host = s; break
    if host is None:
        continue
    # inset the frame front to the glass size, then push the inner face back
    gw = max((bbox([glass])[1] - bbox([glass])[0])[k] for k in range(3))
    hw = max((bbox([host])[1] - bbox([host])[0])[k] for k in range(3))
    res = bmesh.ops.inset_individual(bm, faces=[host], thickness=max((hw - gw) / 2, 0.05), depth=0.0)
    for f in res['faces']:
        f.material_index = STONE; touched.add(f)
    ext = bmesh.ops.extrude_face_region(bm, geom=[host])
    inner = next(g for g in ext['geom'] if isinstance(g, bmesh.types.BMFace))
    bmesh.ops.translate(bm, verts=list(inner.verts), vec=-n * RECESS)
    bmesh.ops.delete(bm, geom=[host], context='FACES_ONLY')      # opens the hole at the front
    inner.material_index = DARK; touched.add(inner)
    if inner.normal.dot(n) < 0:
        inner.normal_flip()
    ic = inner.calc_center_median() + n * (RECESS / 2)
    for e in inner.edges:
        for f in e.link_faces:
            if f is not inner:
                f.material_index = FRAME; touched.add(f)
                f.normal_update()
                if f.normal.dot(ic - f.calc_center_median()) < 0:   # walls must face into the recess
                    f.normal_flip()
    stone_fronts.remove(host)
    to_delete += isl; fixed += 1
bmesh.ops.delete(bm, geom=list(set(to_delete)), context='FACES')
loose = [v for v in bm.verts if not v.link_faces]
bmesh.ops.delete(bm, geom=loose, context='VERTS')
print('WINDOWS recessed', fixed)

# ---------------------------------------------------------------- 2. taller colonnade on the low wing
moved = 0
if DZ > 0:
    wing = [i for i in islands(list(bm.faces)) if bbox(i)[0].x >= WING_X and bbox(i)[1].z <= 16.0]   # wing pieces only (not the windows/railings of the tall block)
    vs = {v for i in wing for f in i for v in f.verts}
    already = any(abs(v.co.z - NEW_COL_TOP) < 1e-3 for v in vs) and not any(abs(v.co.z - COL_TOP) < 1e-3 for v in vs)
    if not already:
        for v in vs:
            if v.co.z > COL_TOP - 0.01:
                v.co.z += DZ; moved += 1
        for i in wing:
            touched.update(i)
print('WING verts raised', moved, 'by', round(DZ, 2))

# ---------------------------------------------------------------- UVs on everything touched
bm.normal_update()
for f in touched:
    if not f.is_valid:
        continue
    T = TILE.get(names[f.material_index], 4.0); nrm = f.normal
    for l in f.loops:
        p = l.vert.co
        if abs(nrm.z) > 0.7:
            u, v = p.x, p.y
        else:
            t = Vector((0, 0, 1)).cross(nrm).normalized(); b = nrm.cross(t).normalized()
            u, v = p.dot(t), p.dot(b)
        l[uvl].uv = (u / T, v / T)
bm.to_mesh(me); bm.free(); me.update()
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode=mode)
print('DONE faces', len(me.polygons))
