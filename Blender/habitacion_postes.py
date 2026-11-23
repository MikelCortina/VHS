# HabitacionFinal Demo.blend:
#  1. Hangs the upside-down person (Armature + Character_31) from the post above it (one of the 8 sloped posts of
#     Cylinder.002, the one at -135 deg), like the hanged figure on the street lamp: several rope coils wrapped round the
#     post with a loose tail, one rope dropping straight down, and a double loop round both ankles -> Cuerda_Poste.
#     The post geometry and the person's pose/height are kept; the person only slides sideways (horizontally) so the
#     rope hangs vertically from the coils.
#  2. Puts a copy of the hanged person (linked mesh/armature data) on each of the other 7 posts.
#  3. Copies the 3 TVs (TV4, TV4.001, TV4.002) onto each of the 8 faces of the central pillar (rotations of 45 deg).
# Re-running is safe: previous copies (names ending in _P<k> / _C<k>) and the rope are rebuilt.
# Works in batch:  blender -b "HabitacionFinal Demo.blend" --python habitacion_postes.py -- [save]
import bpy, bmesh, math, sys
from mathutils import Vector, Matrix

ROPE_TEX = r'C:\UnityProjects\VHS\Assets\Textures\Habitacion\T_Cuerda.png'
POST_ANG = -135.0                        # sector of the original person
# post geometry in its own sector frame (post along +X, world origin = room centre)
TIP_BOT, WALL_BOT = Vector((34.15, 30.88)), Vector((44.28, 26.88))     # (r, z) of the underside line
TIP_TOP, WALL_TOP = Vector((35.36, 32.07)), Vector((45.85, 28.43))     # (r, z) of the top line
HALF_W = 0.27                            # half width of the post
COIL_R0, COIL_R1, TURNS = 35.75, 36.45, 5  # coils wrapped round the post, between these radii
HANG_R = 36.1                            # where the hanging rope leaves the coils
ROPE_R = 0.05

if bpy.context.view_layer.objects.active and bpy.context.view_layer.objects.active.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
objs = bpy.data.objects
arm, body = objs['Armature'], objs['Character_31']
tvs = [objs['TV4'], objs['TV4.001'], objs['TV4.002']]
coll = arm.users_collection[0]

# ---- clean previous runs
for o in list(objs):
    if o.name.startswith(('Cuerda_Poste', 'Armature_P', 'Character_31_P', 'TV4_C', 'TV4.001_C', 'TV4.002_C')):
        objs.remove(o, do_unlink=True)


def zb(r): return TIP_BOT.y + (WALL_BOT.y - TIP_BOT.y) * (r - TIP_BOT.x) / (WALL_BOT.x - TIP_BOT.x)
def zt(r): return TIP_TOP.y + (WALL_TOP.y - TIP_TOP.y) * (r - TIP_TOP.x) / (WALL_TOP.x - TIP_TOP.x)
def sector(deg): return Matrix.Rotation(math.radians(deg), 4, 'Z')


S0 = sector(POST_ANG); S0i = S0.inverted()

# ---- 1. slide the person sideways (same height, same pose) so the ankles are right under the coils
bpy.context.view_layer.update()
def feet():
    lf = arm.matrix_world @ arm.pose.bones['mixamorig:LeftFoot'].head
    rf = arm.matrix_world @ arm.pose.bones['mixamorig:RightFoot'].head
    return (lf + rf) / 2
F = S0i @ feet()
arm.matrix_world = Matrix.Translation(S0.to_3x3() @ Vector((HANG_R - F.x, -F.y, 0))) @ arm.matrix_world
bpy.context.view_layer.update()
F = S0i @ feet()

dg = bpy.context.evaluated_depsgraph_get()
ev = body.evaluated_get(dg); me = ev.to_mesh()
pts = [S0i @ (body.matrix_world @ v.co) for v in me.vertices]
me.calc_loop_triangles()
tris = [tuple(t.vertices) for t in me.loop_triangles]
ev.to_mesh_clear()
top_body = max(p.z for p in pts)
band = [p for p in pts if F.z - 0.9 < p.z < F.z - 0.05]
lo = Vector((min(p.x for p in band), min(p.y for p in band))); hi = Vector((max(p.x for p in band), max(p.y for p in band)))
print('ANKLES sector', [round(x, 2) for x in F], 'feet top', round(top_body, 2), 'post underside', round(zb(HANG_R), 2))

# ---- rope mesh in the sector frame
bm = bmesh.new(); uvl = bm.loops.layers.uv.new('UVMap')


def tube(pts, closed=False, sides=6, tile=0.35, r=ROPE_R):
    n = len(pts); rings = []
    prev_a = None
    for i in range(n):
        p0 = pts[i - 1] if (closed or i > 0) else pts[i]
        p2 = pts[(i + 1) % n] if (closed or i < n - 1) else pts[i]
        t = (p2 - p0).normalized()
        a = (prev_a - t * prev_a.dot(t)).normalized() if prev_a else t.orthogonal().normalized()   # no twisting
        prev_a = a; b = t.cross(a)
        rings.append([bm.verts.new(pts[i] + (a * math.cos(2 * math.pi * k / sides) + b * math.sin(2 * math.pi * k / sides)) * r) for k in range(sides)])
    dist = [0.0]
    for i in range(1, n + 1):
        dist.append(dist[-1] + (pts[i % n] - pts[i - 1]).length)
    for i in range(n if closed else n - 1):
        j = (i + 1) % n
        for k in range(sides):
            f = bm.faces.new((rings[i][k], rings[i][(k + 1) % sides], rings[j][(k + 1) % sides], rings[j][k]))
            for l, (u, v) in zip(f.loops, ((dist[i], k), (dist[i], k + 1), (dist[i + 1], k + 1), (dist[i + 1], k))):
                l[uvl].uv = (u / tile, v / sides)
    if not closed:
        for ring in (rings[0], rings[-1]):
            bm.faces.new(ring if ring is rings[-1] else list(reversed(ring)))


def around_post(u, r):
    """point on a rounded rectangle hugging the post's cross-section at radius r; u in [0,1) goes once round
    (0 = middle of the underside, going towards +y, up the side, over the top, down the other side)"""
    y0, y1 = -HALF_W - ROPE_R, HALF_W + ROPE_R
    z0, z1 = zb(r) - ROPE_R, zt(r) + ROPE_R
    w, h = y1 - y0, z1 - z0; per = 2 * (w + h); s = (u % 1.0) * per
    if s < w / 2: return Vector((r, s, z0))
    s -= w / 2
    if s < h: return Vector((r, y1, z0 + s))
    s -= h
    if s < w: return Vector((r, y1 - s, z1))
    s -= w
    if s < h: return Vector((r, y0, z1 - s))
    s -= h
    return Vector((r, y0 + s, z0))


# coils: one continuous helix round the post, then a loose tail hanging from the wall-side end
STEPS = 24
coil = [around_post(i / STEPS, COIL_R0 + (COIL_R1 - COIL_R0) * i / (STEPS * TURNS)) for i in range(STEPS * TURNS + 1)]
end = coil[-1]
tail = [end + Vector((0.04 * k, 0.03 * math.sin(k), -0.16 * k)) for k in range(1, 6)]
tube(coil + tail)

# hanging rope: from under the coils straight down to the ankles, then a double loop round both ankles
from mathutils.geometry import convex_hull_2d, intersect_line_line_2d
from mathutils import Vector as V2


LEG_R = 0.032                            # ankle lashing is thinner than the hanging rope


def leg_outline(z):
    """convex outline (xy, sector frame) of both legs together: the real cross-section of the skin at height z
    (triangles cut by the plane), pushed out so the rope sits pressed onto the skin"""
    xy = []
    for tri in tris:
        for a, b in ((tri[0], tri[1]), (tri[1], tri[2]), (tri[2], tri[0])):
            pa, pb = pts[a], pts[b]
            if (pa.z - z) * (pb.z - z) < 0:
                f = (z - pa.z) / (pb.z - pa.z); q = pa + (pb - pa) * f
                xy.append(V2((q.x, q.y)))
    hull = [xy[i] for i in convex_hull_2d(xy)]
    c = sum(hull, V2((0, 0))) / len(hull)
    return c, [h + (h - c).normalized() * LEG_R * 0.75 for h in hull]


def ray_hit(c, poly, ang):
    far = c + V2((math.cos(ang), math.sin(ang))) * 5
    for i in range(len(poly)):
        hit = intersect_line_line_2d(c, far, poly[i - 1], poly[i])
        if hit: return hit
    return c


# three tight turns spiralling down round both legs, just below the ankles
TURN_Z = [F.z - 0.06 - 0.075 * k for k in range(5)]       # 4 tight turns just below the ankle bone
SEG = 24
loop = []
outl = [leg_outline(z) for z in TURN_Z]
A0 = math.pi                                               # turns start/end on the side facing the post (-x)
for t in range(len(TURN_Z) - 1):
    (c0, poly0), (c1, poly1) = outl[t], outl[t + 1]
    for i in range(SEG + (1 if t == len(TURN_Z) - 2 else 0)):
        f = i / SEG; ang = A0 + 2 * math.pi * f
        p0, p1 = ray_hit(c0, poly0, ang), ray_hit(c1, poly1, ang)
        p = p0 + (p1 - p0) * f
        loop.append(Vector((p.x, p.y, TURN_Z[t] + (TURN_Z[t + 1] - TURN_Z[t]) * f)))
first = loop[0]
start = Vector((HANG_R, 0.0, zb(HANG_R) - ROPE_R))
# the hanging rope comes down between the soles, then follows the skin of the feet into the first turn
cf, pf = leg_outline(F.z + 0.12)
entry = ray_hit(cf, pf, A0)
mid = Vector((entry.x, entry.y, F.z + 0.12))
drop = [start + (mid - start) * (k / 8) for k in range(9)] + [mid + (first - mid) * (k / 3) for k in range(1, 3)]
tube(drop, r=ROPE_R)
tube(loop, r=LEG_R, sides=8)
# knot where the rope meets the turns: two small wraps cinching rope and lashing together, plus a short loose end
kc = first + Vector((0, 0, 0.03)); kd = (mid - first).normalized()
ka = kd.orthogonal().normalized(); kb = kd.cross(ka)
for dz in (0.0, 0.035):
    ring = [kc + kd * dz + (ka * math.cos(2 * math.pi * s / 10) + kb * math.sin(2 * math.pi * s / 10)) * (ROPE_R + LEG_R * 0.6) for s in range(10)]
    tube(ring, closed=True, r=LEG_R * 0.9, sides=6)
last = loop[-1]
out = (Vector((last.x, last.y, 0)) - Vector((outl[-1][0].x, outl[-1][0].y, 0))).normalized()
tube([last + out * 0.02 * k + Vector((0, 0, -0.06 * k)) for k in range(4)], r=LEG_R, sides=8)

rope_me = bpy.data.meshes.new('Cuerda_Poste'); bm.normal_update(); bm.to_mesh(rope_me); bm.free()
mat = bpy.data.materials.get('M_Cuerda') or bpy.data.materials.new('M_Cuerda')
mat.use_nodes = True; nt = mat.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
    nt.nodes.remove(n)
t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(ROPE_TEX, check_existing=True); t.interpolation = 'Closest'
nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color']); bsdf.inputs['Roughness'].default_value = 1.0
mat.diffuse_color = (0.38, 0.30, 0.2, 1)
rope_me.materials.append(mat)
rope = objs.new('Cuerda_Poste', rope_me); coll.objects.link(rope); rope.matrix_world = S0.copy()

# ---- 2. copies of the hanged person on the other 7 posts (linked data)
for k in range(1, 8):
    Rz = sector(45 * k)
    a2 = arm.copy(); a2.name = f'Armature_P{k}'; coll.objects.link(a2); a2.matrix_world = Rz @ arm.matrix_world
    b2 = body.copy(); b2.name = f'Character_31_P{k}'; coll.objects.link(b2)
    b2.parent = a2; b2.matrix_parent_inverse = body.matrix_parent_inverse.copy()
    for m in b2.modifiers:
        if m.type == 'ARMATURE':
            m.object = a2
    r2 = rope.copy(); r2.name = f'Cuerda_Poste_P{k}'; coll.objects.link(r2); r2.matrix_world = Rz @ rope.matrix_world

# ---- 3. the 3 TVs on each of the 8 faces of the central pillar
for k in range(1, 8):
    Rz = sector(45 * k)
    for tv in tvs:
        c = tv.copy(); c.name = f'{tv.name}_C{k}'
        for cl in tv.users_collection:
            cl.objects.link(c)
        c.matrix_world = Rz @ tv.matrix_world
print('DONE rope faces', len(rope_me.polygons))
if '--' in sys.argv and 'save' in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.save_mainfile(); print('SAVED', bpy.data.filepath)
