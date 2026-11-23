# Window frames: the stone box around each recessed window (front ring + outer sides) -> M_Marco_Ventana,
# so the whole frame (outside and inside the recess) is one material. The glass and the wall are untouched.
# Window = small island (< 2.5 m) containing M_Ranura_Oscura glass + M_Marco_Ventana walls. Works in batch or in Blender.
import bpy, bmesh
from mathutils import Vector
TILE = 2.0
ob = bpy.data.objects['Edificio_Brutalista']; me = ob.data
mode = bpy.context.object.mode if bpy.context.object else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
names = [m.name if m else '' for m in me.materials]
STONE, DARK, FRAME = (names.index(n) for n in ('M_Piedra_Brutalista', 'M_Ranura_Oscura', 'M_Marco_Ventana'))
bm = bmesh.new(); bm.from_mesh(me); bm.normal_update(); uvl = bm.loops.layers.uv.active
todo = set(bm.faces); wins = faces = 0
while todo:
    s = todo.pop(); isl = [s]; st = [s]
    while st:
        f = st.pop()
        for v in f.verts:
            for g in v.link_faces:
                if g in todo: todo.remove(g); isl.append(g); st.append(g)
    co = [v.co for f in isl for v in f.verts]
    size = max(max(c[k] for c in co) - min(c[k] for c in co) for k in range(3))
    mats = {f.material_index for f in isl}
    if size < 2.5 and DARK in mats and FRAME in mats and STONE in mats:
        for f in isl:
            if f.material_index == STONE:
                f.material_index = FRAME; faces += 1
                n = f.normal
                for l in f.loops:
                    p = l.vert.co
                    if abs(n.z) > 0.7: u, w = p.x, p.y
                    else:
                        t = Vector((0, 0, 1)).cross(n).normalized(); b = n.cross(t).normalized(); u, w = p.dot(t), p.dot(b)
                    l[uvl].uv = (u / TILE, w / TILE)
        wins += 1
bm.to_mesh(me); bm.free(); me.update()
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode=mode)
print('FRAMES outer', wins, 'windows,', faces, 'faces -> M_Marco_Ventana')
