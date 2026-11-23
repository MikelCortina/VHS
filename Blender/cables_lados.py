# HabitacionFinal Demo.blend: the user drew cables on one side (BezierCurve = long cable from a socket on the central
# pillar to a hanged person; BezierCurve.001-.003 = the 3 TV cables going into the pillar) and a small socket box on
# the pillar (8 extra verts in "Cylinder"). This repeats all of it on the other 7 sides (rotations of 45 deg about
# the room centre), so every side and every hanged person has its cables. Curves are linked copies (same data).
# Re-running is safe: old copies (*_L<k>) are removed and the socket is only added where it is missing.
# Usage: blender -b "HabitacionFinal Demo.blend" --python cables_lados.py -- [save]
import bpy, bmesh, math, sys
from mathutils import Matrix, kdtree

if bpy.context.view_layer.objects.active and bpy.context.view_layer.objects.active.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
objs = bpy.data.objects
EXTRA = ['Cube']                 # the street-lamp arm the user built on one side (its cable is BézierCurve.004)
for o in list(objs):
    if '_L' in o.name and (o.name.split('_L')[0].startswith('BézierCurve') or o.name.split('_L')[0] in EXTRA):
        objs.remove(o, do_unlink=True)
curves = [o for o in objs if (o.type == 'CURVE' and o.name.startswith('BézierCurve')) or o.name in EXTRA]

n_c = 0
for k in range(1, 8):
    Rz = Matrix.Rotation(math.radians(45 * k), 4, 'Z')
    for c in curves:
        d = c.copy(); d.name = f'{c.name}_L{k}'
        for cl in c.users_collection:
            cl.objects.link(d)
        d.matrix_world = Rz @ c.matrix_world; n_c += 1

# socket on the pillar: faces made only of verts that have no 45-deg-rotated twin -> copy them round
ob = objs['Cylinder']; me = ob.data; mw = ob.matrix_world; mwi = mw.inverted()
bm = bmesh.new(); bm.from_mesh(me)
def build_kd():
    kd = kdtree.KDTree(len(bm.verts))
    for i, v in enumerate(bm.verts): kd.insert(mw @ v.co, i)
    kd.balance(); return kd
kd = build_kd(); R45 = Matrix.Rotation(math.radians(45), 3, 'Z')
lone = {v for v in bm.verts if kd.find(R45 @ (mw @ v.co))[2] > 0.05 or kd.find(R45.inverted() @ (mw @ v.co))[2] > 0.05}
src = [f for f in bm.faces if all(v in lone for v in f.verts)]
print('SOCKET faces', len(src), 'verts', len(lone))
added = 0
for k in range(1, 8):
    R = Matrix.Rotation(math.radians(45 * k), 3, 'Z')
    probe = R @ (mw @ next(iter(src[0].verts)).co) if src else None
    if not src or kd.find(probe)[2] < 0.01:
        continue                                            # already there
    vmap = {}
    for f in src:
        for v in f.verts:
            if v not in vmap:
                vmap[v] = bm.verts.new(mwi @ (R @ (mw @ v.co)))
    for f in src:
        nf = bm.faces.new([vmap[v] for v in f.verts]); nf.material_index = f.material_index; nf.smooth = f.smooth
    added += 1
bm.to_mesh(me); bm.free(); me.update()
print('DONE curve copies', n_c, '| sockets added', added)
if '--' in sys.argv and 'save' in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.save_mainfile(); print('SAVED', bpy.data.filepath)
