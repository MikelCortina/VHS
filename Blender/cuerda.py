# Rope for the hanged body in Cementerio.blend: noose on the neck, straight rope that follows
# the neck when the body swings, and coils wrapped round the street-lamp arm.
#   Cuerda_Lazo    noose + hangman's knot, bone-parented to mixamorig:Neck
#   Cuerda_Ancla   empty on top of the knot, bone-parented to mixamorig:Neck
#   Cuerda_Tramo   straight rope, origin at the lamp; "Stretch To" Cuerda_Ancla (Unity: RopeStretch.cs)
#   Cuerda_Vueltas coils + loose end on the lamp arm, parented to the lamp (Cube)
# Usage: blender -b Cementerio.blend --python cuerda.py -- <out.blend> <T_Cuerda.png>
import bpy, bmesh, math, sys, os
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index('--') + 1:]
out_blend, tex_path = argv[0], argv[1]
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pngsave import save_png
import numpy as np

ARM = bpy.data.objects['Armature']
LAMP = bpy.data.objects['Cube']
BONE = 'mixamorig:Neck'
R = 0.03          # rope radius
SIDES = 6
UV_REPEAT = 0.25  # one texture repeat every 0.25 units along the rope

# ---------- collection ----------
cname = 'Cuerda'
col = bpy.data.collections.get(cname)
if col:
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
else:
    col = bpy.data.collections.new(cname); bpy.context.scene.collection.children.link(col)

# ---------- texture + material ----------
C = 32
img = np.zeros((C, C, 3))
for y in range(C):
    for x in range(C):
        d = (x + y) % 8                      # diagonal twisted strands
        base = np.array((120, 92, 58.0)) if d < 4 else np.array((92, 68, 40.0))
        if d in (0, 4):
            base = base * 0.65               # groove between strands
        img[y, x] = base + ((x * 7 + y * 13) % 5 - 2) * 3
save_png(img, tex_path)

mat = bpy.data.materials.get('M_Cuerda') or bpy.data.materials.new('M_Cuerda')
mat.diffuse_color = (0.33, 0.24, 0.14, 1)
mat.use_nodes = True
nt = mat.node_tree
bsdf = nt.nodes.get('Principled BSDF')
for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
    nt.nodes.remove(n)
tex = nt.nodes.new('ShaderNodeTexImage')
tex.image = bpy.data.images.load(tex_path, check_existing=True); tex.image.reload()
tex.interpolation = 'Closest'
nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 1.0
nt.nodes.active = tex


# ---------- tube builder (parallel-transport frames) ----------
def tube(bm, pts, radius=R, closed=False, uvl=None):
    pts = [Vector(p) for p in pts]
    n = len(pts)
    tans = []
    for i in range(n):
        a = pts[i - 1] if (i > 0 or closed) else pts[i]
        b = pts[(i + 1) % n] if (i < n - 1 or closed) else pts[i]
        tans.append((b - a).normalized())
    ref = Vector((0, 0, 1)) if abs(tans[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = (ref - tans[0] * ref.dot(tans[0])).normalized()
    rings, lengths, L = [], [], 0.0
    for i in range(n):
        if i > 0:
            nrm = (nrm - tans[i] * nrm.dot(tans[i])).normalized()
            L += (pts[i] - pts[i - 1]).length
        bin_ = tans[i].cross(nrm)
        ring = []
        for s in range(SIDES):
            ang = 2 * math.pi * s / SIDES
            ring.append(bm.verts.new(pts[i] + (nrm * math.cos(ang) + bin_ * math.sin(ang)) * radius))
        rings.append(ring); lengths.append(L)
    segs = n if closed else n - 1
    if closed:
        lengths.append(L + (pts[0] - pts[-1]).length)
    for i in range(segs):
        r0, r1 = rings[i], rings[(i + 1) % n]
        for s in range(SIDES):
            f = bm.faces.new([r0[s], r0[(s + 1) % SIDES], r1[(s + 1) % SIDES], r1[s]])
            v0, v1 = lengths[i] / UV_REPEAT, lengths[i + 1] / UV_REPEAT
            u0, u1 = s / SIDES, (s + 1) / SIDES
            for l, uv in zip(f.loops, [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]):
                l[uvl].uv = uv
    if not closed:      # caps
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])


def make_obj(name, builder, origin=Vector((0, 0, 0))):
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap')
    builder(bm, uvl)
    bmesh.ops.translate(bm, verts=bm.verts, vec=-origin)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(mat)
    for p in me.polygons:
        p.use_smooth = True
    o = bpy.data.objects.new(name, me); col.objects.link(o)
    o.location = origin
    return o


def bone_parent(o):
    bpy.context.view_layer.update(); mw = o.matrix_world.copy()
    o.parent = ARM; o.parent_type = 'BONE'; o.parent_bone = BONE
    bpy.context.view_layer.update()
    o.matrix_world = mw


# ---------- geometry (world coordinates, measured from the scene) ----------
NECK_C = Vector((8.0, -6.12, 14.92))     # centre of the neck at the noose height
RX, RY = 0.19, 0.21                      # noose ellipse
TILT = math.radians(-25)                 # nape side higher than the throat
ROT = Matrix.Rotation(TILT, 3, 'X')
nape = NECK_C + ROT @ Vector((0, -RY, 0))
KNOT_BASE = nape + Vector((0, -0.02, 0.03))
KNOT_TOP = KNOT_BASE + Vector((0, 0, 0.24))

ARM_Y0 = -6.35                           # where the rope reaches the lamp arm
ARM_C_Z = 17.59                          # lamp arm axis (runs along Y)
COIL_R = 0.39                            # arm half-width 0.34 (octagon) + rope
TURNS, PITCH = 4, 0.13
TOP = Vector((8.0, ARM_Y0, ARM_C_Z - COIL_R))


def build_noose(bm, uvl):
    loop = [NECK_C + ROT @ Vector((RX * math.sin(a), -RY * math.cos(a), 0))
            for a in [2 * math.pi * k / 14 for k in range(14)]]
    tube(bm, loop, closed=True, uvl=uvl)
    for k in range(5):                    # hangman's knot: stacked turns at the nape
        c = KNOT_BASE + Vector((0, 0, 0.05 * k))
        ring = [c + Vector((0.065 * math.cos(a), 0.065 * math.sin(a), 0.01 * math.sin(a)))
                for a in [2 * math.pi * j / 8 for j in range(8)]]
        tube(bm, ring, radius=R * 0.9, closed=True, uvl=uvl)


def build_coils(bm, uvl):
    pts = []
    steps = 12
    for i in range(TURNS * steps + 1):
        phi = 2 * math.pi * i / steps
        y = ARM_Y0 - PITCH * i / steps
        pts.append(Vector((8.0 + COIL_R * math.sin(phi), y, ARM_C_Z - COIL_R * math.cos(phi))))
    last = pts[-1]
    pts += [last + Vector((0.04, -0.06, -0.18)), last + Vector((0.07, -0.09, -0.42)),
            last + Vector((0.05, -0.1, -0.62))]          # loose end hanging
    tube(bm, pts, uvl=uvl)


noose = make_obj('Cuerda_Lazo', build_noose, origin=NECK_C.copy())
bone_parent(noose)

anchor = bpy.data.objects.new('Cuerda_Ancla', None)
anchor.empty_display_type = 'SPHERE'; anchor.empty_display_size = 0.05
col.objects.link(anchor); anchor.location = KNOT_TOP
bone_parent(anchor)

length = (KNOT_TOP - TOP).length
def build_straight(bm, uvl):
    n = 8
    tube(bm, [Vector((0, length * i / n, 0)) for i in range(n + 1)], uvl=uvl)
straight = make_obj('Cuerda_Tramo', build_straight)
straight.location = TOP
straight.rotation_mode = 'QUATERNION'
straight.rotation_quaternion = (KNOT_TOP - TOP).to_track_quat('Y', 'Z')
con = straight.constraints.new('STRETCH_TO')
con.target = anchor
con.rest_length = length
con.volume = 'NO_VOLUME'
con.keep_axis = 'PLANE_X'

coils = make_obj('Cuerda_Vueltas', build_coils, origin=Vector((8.0, ARM_Y0, ARM_C_Z)))
bpy.context.view_layer.update(); mw = coils.matrix_world.copy(); coils.parent = LAMP
bpy.context.view_layer.update(); coils.matrix_world = mw

bpy.context.view_layer.update()
print('ROPE', [o.name for o in col.objects], 'length', round(length, 3),
      'tris', sum(sum(len(p.vertices) - 2 for p in o.data.polygons) for o in col.objects if o.type == 'MESH'))
bpy.ops.wm.save_as_mainfile(filepath=out_blend, copy=True)
