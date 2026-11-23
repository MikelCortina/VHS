# Tunnel building (Serwers02_004 in Sewer.blend): re-does the UVs in the project's PSX style (world-space plane
# projection per face, so the texel size is the same everywhere and nothing is stretched or streaked), and points
# its three textures at the right folder (Assets/Sewer (1)/Sewer/Textures) with Closest filtering.
#  concrete_base_03 (tube + portal) and concrete_base_02 (trims): 1 repeat / 3 m;  soil_mud (floor): 1 repeat / 2 m
# Works inside Blender or in batch:  blender -b Sewer.blend --python tunel_uvs.py -- [save]
import bpy, bmesh, sys, os
from mathutils import Vector
OBJ = 'Serwers02_004'
TILES = {'concrete_base_03': 3.0, 'concrete_base_02': 3.0, 'soil_mud': 2.0}
TEXDIR = r'C:\UnityProjects\VHS\Assets\Sewer (1)\Sewer\Textures'

ob = bpy.data.objects[OBJ]; me = ob.data
act = bpy.context.view_layer.objects.active
mode = act.mode if act else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

for m in me.materials:
    for n in m.node_tree.nodes:
        if n.type == 'TEX_IMAGE' and n.image:
            f = os.path.join(TEXDIR, os.path.basename(n.image.filepath.replace(chr(92), '/')))
            if os.path.exists(f):
                n.image.filepath = bpy.path.relpath(f) if bpy.data.filepath else f
                n.image.reload()
            n.interpolation = 'Closest'

tiles = [TILES.get(m.name, 3.0) if m else 3.0 for m in me.materials]
mw = ob.matrix_world; rot = mw.to_3x3().normalized()
bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
for f in bm.faces:
    n = (rot @ f.normal).normalized(); tile = tiles[f.material_index]
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
print('TUNEL UVS', len(me.polygons), 'faces, tiles', dict(zip([m.name for m in me.materials], tiles)))
if '--' in sys.argv and 'save' in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.save_mainfile(); print('SAVED', bpy.data.filepath)
