# Run INSIDE Blender (Scripting tab > Open this file > Run Script, or Alt+P).
# Redoes the UVs of the selected meshes (or Edificio_Brutalista if nothing is selected) in the project's PSX style:
# world-space projection onto each face's own plane, so walls, slopes and edited faces never stretch.
# Metres per texture repeat depend on the material. Works in Object or Edit mode.
import bpy, bmesh
from mathutils import Vector

TILE = {'M_Piedra_Brutalista': 4.0, 'M_Ranura_Oscura': 3.0, 'M_Tejado_Oscuro': 3.0,
        'M_Metal_Barandilla': 2.0, 'M_Lampara_Calida': None}      # None = whole image on each face
DEFAULT = 4.0

objs = [o for o in bpy.context.selected_objects if o.type == 'MESH'] or [bpy.data.objects['Edificio_Brutalista']]
mode = bpy.context.object.mode if bpy.context.object else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

for ob in objs:
    me = ob.data; mw = ob.matrix_world; rot = mw.to_3x3()
    tiles = [TILE.get(m.name if m else '', DEFAULT) for m in me.materials] or [DEFAULT]
    bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    uv = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    for f in bm.faces:
        T = tiles[min(f.material_index, len(tiles) - 1)]
        n = rot @ f.normal
        pts = [mw @ l.vert.co for l in f.loops]
        if n.length < 1e-6 or abs(n.normalized().z) > 0.7:
            uvs = [(p.x, p.y) for p in pts]                       # floors / roofs
        else:
            n = n.normalized()
            t = Vector((0, 0, 1)).cross(n).normalized()           # along the wall
            b = n.cross(t).normalized()                           # up the wall / slope
            uvs = [(p.dot(t), p.dot(b)) for p in pts]
        if T is None:
            u0 = min(a for a, _ in uvs); v0 = min(c for _, c in uvs)
            su = max(max(a for a, _ in uvs) - u0, 1e-4); sv = max(max(c for _, c in uvs) - v0, 1e-4)
            uvs = [((a - u0) / su, (c - v0) / sv) for a, c in uvs]
        else:
            uvs = [(a / T, c / T) for a, c in uvs]
        for l, c in zip(f.loops, uvs):
            l[uv].uv = c
    bm.to_mesh(me); bm.free(); me.update()
    print('UV fixed', ob.name, len(me.polygons), 'faces')

if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode=mode)
