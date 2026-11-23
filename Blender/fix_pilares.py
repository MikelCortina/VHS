# Raises the colonnade columns that stayed at the old height (3.4 m) to the new one (6 m) and redoes their UVs.
# Column = loose stone piece on the low wing (x > 61), standing on the ground, narrower than 2 m, top at OLD.
import bpy, bmesh
from mathutils import Vector
OLD, NEW, TILE = 3.4, 6.0, 4.0
ob = bpy.data.objects['Edificio_Brutalista']; me = ob.data
mode = bpy.context.object.mode if bpy.context.object else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
bm = bmesh.new(); bm.from_mesh(me); uvl = bm.loops.layers.uv.active
todo = set(bm.faces); fixed = 0
while todo:
    s = todo.pop(); isl = [s]; st = [s]
    while st:
        f = st.pop()
        for v in f.verts:
            for g in v.link_faces:
                if g in todo: todo.remove(g); isl.append(g); st.append(g)
    vs = {v for f in isl for v in f.verts}
    lo = [min(v.co[k] for v in vs) for k in range(3)]; hi = [max(v.co[k] for v in vs) for k in range(3)]
    if lo[0] > 61 and abs(lo[2]) < 0.01 and abs(hi[2] - OLD) < 0.01 and hi[0] - lo[0] < 2 and hi[1] - lo[1] < 2:
        for v in vs:
            if v.co.z > OLD - 0.01: v.co.z = NEW
        bm.normal_update()
        for f in isl:
            n = f.normal
            for l in f.loops:
                p = l.vert.co
                if abs(n.z) > 0.7: u, w = p.x, p.y
                else:
                    t = Vector((0, 0, 1)).cross(n).normalized(); b = n.cross(t).normalized(); u, w = p.dot(t), p.dot(b)
                l[uvl].uv = (u / TILE, w / TILE)
        fixed += 1
bm.to_mesh(me); bm.free(); me.update()
print('COLUMNS raised', fixed)
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode=mode)
