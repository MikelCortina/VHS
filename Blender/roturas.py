# Damage pass for Cementerio.blend: broken / leaning / chipped headstones, cracked grave slabs,
# notched and holed brick walls, and one breach the player can walk through, plus rubble.
# Usage: blender -b Cementerio.blend --python roturas.py -- <out.blend>
import bpy, bmesh, math, random, sys
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index('--') + 1:]
OUT = argv[0]
rnd = random.Random(1987)
CENTER = Vector((48, 48, 0))
LOG = []

col = bpy.data.collections.get('Roturas')
if col is None:
    col = bpy.data.collections.new('Roturas'); bpy.context.scene.collection.children.link(col)


# ------------------------------------------------------------------ helpers
def cut(bm, co, no, keep_below=True, jag=0.12):
    """Bisect bm with a plane, keep one side, cap the hole with a jagged (poked) face."""
    geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
    res = bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=no,
                                 clear_outer=keep_below, clear_inner=not keep_below)
    edges = [e for e in res['geom_cut'] if isinstance(e, bmesh.types.BMEdge) and e.is_valid and e.is_boundary]
    if not edges:
        return
    filled = bmesh.ops.holes_fill(bm, edges=edges, sides=0)['faces']
    sgn = 1 if keep_below else -1
    for f in filled:
        r = bmesh.ops.poke(bm, faces=[f])
        for v in r['verts']:
            v.co += Vector(no) * sgn * rnd.uniform(0.3, 1.0) * jag
        for v in [v for e in edges if e.is_valid for v in e.verts]:
            v.co += Vector(no) * sgn * rnd.uniform(-0.4, 0.4) * jag


def split_faces(bm, pred):
    """Return a copy of bm containing only faces where pred(face) is true; delete them from bm."""
    part = bm.copy()
    bmesh.ops.delete(part, geom=[f for f in part.faces if not pred(f)], context='FACES')
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if pred(f)], context='FACES')
    return part


def append(dst, src):
    me = bpy.data.meshes.new('_tmp'); src.to_mesh(me); dst.from_mesh(me); bpy.data.meshes.remove(me)


def bbox(bm):
    xs = [v.co for v in bm.verts]
    return Vector([min(p[i] for p in xs) for i in range(3)]), Vector([max(p[i] for p in xs) for i in range(3)])


def transform(bm, m):
    bmesh.ops.transform(bm, matrix=m, verts=bm.verts)


def rest_on_ground(bm, z=0.0):
    mn, _ = bbox(bm); bmesh.ops.translate(bm, verts=bm.verts, vec=Vector((0, 0, z - mn.z)))


def rubble_chunk(bm, center, size, mat_index=0, uv=None, flat=False):
    sx, sy, sz = size
    if flat:
        sz *= 0.35
    pts = []
    for dx in (-1, 1):
        for dy in (-1, 1):
            for dz in (0, 1):
                j = 0.35
                pts.append(Vector((dx * sx / 2 * rnd.uniform(1 - j, 1 + j), dy * sy / 2 * rnd.uniform(1 - j, 1 + j),
                                   dz * sz * rnd.uniform(0.6, 1.2))))
    rot = Matrix.Rotation(rnd.uniform(0, 6.28), 3, 'Z') @ Matrix.Rotation(rnd.uniform(-0.3, 0.3), 3, 'X')
    vs = [bm.verts.new(center + rot @ p) for p in pts]
    for f in [(0, 2, 6, 4), (1, 5, 7, 3), (0, 4, 5, 1), (2, 3, 7, 6), (0, 1, 3, 2), (4, 6, 7, 5)]:
        face = bm.faces.new([vs[i] for i in f]); face.material_index = mat_index
        if uv is not None:
            n = face.normal
            for l in face.loops:
                c = l.vert.co
                l[uv].uv = ((c.x + c.y) / 3.0, c.z / 3.0) if abs(n.z) < 0.7 else (c.x / 3.0, c.y / 3.0)


def finish(bm, me):
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free(); me.update()


# ------------------------------------------------------------------ headstones
stones = sorted([o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.polygons) == 117
                 and abs(o.dimensions.z - 3.0) < 0.1], key=lambda o: o.name)
slabs = sorted([o for o in bpy.data.objects if o.type == 'MESH' and len(o.data.polygons) == 198
                and abs(o.dimensions.z - 1.0) < 0.1], key=lambda o: o.name)
picked = rnd.sample(stones, 16)
kinds = ['snap'] * 7 + ['lean'] * 5 + ['chip'] * 4

for o, kind in zip(picked, kinds):
    bm = bmesh.new(); bm.from_mesh(o.data)
    stone = split_faces(bm, lambda f: max(v.co.z for v in f.verts) > 0.01)
    mn, mx = bbox(stone)
    h = mx.z - mn.z
    c = (mn + mx) / 2
    ang = rnd.uniform(0, 6.28)
    side = Vector((math.cos(ang), math.sin(ang), 0))
    if kind == 'snap':
        z = mn.z + h * rnd.uniform(0.45, 0.7)
        no = (Vector((0, 0, 1)) + side * rnd.uniform(0.35, 0.7)).normalized()
        top = stone.copy()
        cut(stone, (c.x, c.y, z), no, keep_below=True, jag=0.15)
        cut(top, (c.x, c.y, z), no, keep_below=False, jag=0.15)
        tmn, tmx = bbox(top); tc = (tmn + tmx) / 2
        axis = Vector((-side.y, side.x, 0))
        m = (Matrix.Translation(Vector((c.x, c.y, 0)) + side * rnd.uniform(2.3, 2.9))
             @ Matrix.Rotation(rnd.uniform(-0.6, 0.6), 4, 'Z')
             @ Matrix.Rotation(math.radians(rnd.uniform(80, 100)), 4, axis)
             @ Matrix.Translation(-tc))
        transform(top, m); rest_on_ground(top, -0.05)
        append(stone, top); top.free()
        for _ in range(rnd.randint(2, 4)):
            rubble_chunk(stone, Vector((c.x, c.y, 0)) + side * rnd.uniform(1.4, 2.4)
                         + Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), 0)),
                         (rnd.uniform(0.15, 0.35),) * 3)
    elif kind == 'lean':
        pivot = Vector((c.x, c.y, 0)) + side * (mx.x - mn.x) * 0.45
        axis = Vector((-side.y, side.x, 0))
        m = Matrix.Translation(pivot) @ Matrix.Rotation(math.radians(rnd.uniform(14, 26)), 4, axis) @ Matrix.Translation(-pivot)
        transform(stone, m)
        bmesh.ops.translate(stone, verts=stone.verts, vec=Vector((0, 0, -rnd.uniform(0.1, 0.3))))   # sunk a bit
    else:  # chip a top corner off
        z = mn.z + h * rnd.uniform(0.7, 0.85)
        corner = Vector((c.x, c.y, z)) + side * (mx.x - mn.x) * 0.2
        no = (Vector((0, 0, 1)) + side * 1.2).normalized()
        piece = stone.copy()
        cut(stone, corner, no, keep_below=True, jag=0.08)
        cut(piece, corner, no, keep_below=False, jag=0.08)
        pmn, pmx = bbox(piece); pc = (pmn + pmx) / 2
        m = (Matrix.Translation(Vector((c.x, c.y, 0)) + side * rnd.uniform(1.9, 2.4))
             @ Matrix.Rotation(rnd.uniform(0, 6.28), 4, 'Z') @ Matrix.Rotation(1.3, 4, 'X') @ Matrix.Translation(-pc))
        transform(piece, m); rest_on_ground(piece, -0.03)
        append(stone, piece); piece.free()
    append(bm, stone); stone.free()
    finish(bm, o.data)
    LOG.append(f'lapida {kind}: {o.name}')

# ------------------------------------------------------------------ grave slabs: cracked, one half tilted
for o in rnd.sample(slabs, 5):
    bm = bmesh.new(); bm.from_mesh(o.data)
    slab = split_faces(bm, lambda f: max(v.co.z for v in f.verts) > 0.01)
    mn, mx = bbox(slab); c = (mn + mx) / 2
    long_x = (mx.x - mn.x) > (mx.y - mn.y)
    along = Vector((1, 0, 0)) if long_x else Vector((0, 1, 0))
    no = (along + Vector((-along.y, along.x, 0)) * rnd.uniform(-0.5, 0.5)).normalized()
    co = c + along * rnd.uniform(-0.15, 0.15) * ((mx - mn).dot(along))
    other = slab.copy()
    cut(slab, co, no, keep_below=True, jag=0.12)
    cut(other, co, -no, keep_below=True, jag=0.12)
    omn, omx = bbox(other)
    # hinge at the crack: the far end of this half sinks, the crack edge lifts
    hinge = Vector((co.x, co.y, mn.z))
    axis = Vector((-no.y, no.x, 0))
    m = Matrix.Translation(hinge) @ Matrix.Rotation(math.radians(rnd.uniform(6, 12)), 4, axis) @ Matrix.Translation(-hinge)
    transform(other, m)
    bmesh.ops.translate(other, verts=other.verts, vec=no * 0.25 + Vector((0, 0, -0.25)))
    append(slab, other); other.free()
    for _ in range(rnd.randint(3, 5)):
        rubble_chunk(slab, Vector((co.x, co.y, 0)) + Vector((rnd.uniform(-1.2, 1.2), rnd.uniform(-1.2, 1.2), 0)),
                     (rnd.uniform(0.2, 0.4),) * 3, flat=True)
    append(bm, slab); slab.free()
    finish(bm, o.data)
    LOG.append(f'losa rota: {o.name}')


# ------------------------------------------------------------------ walls (boolean cutters)
def wall_uv_scale(w):
    me = w.data; uvl = me.uv_layers.active.data
    xs, us = [], []
    for p in me.polygons:
        if p.material_index == 0 and abs(p.normal.y) > 0.9:
            for li in p.loop_indices:
                xs.append(me.vertices[me.loops[li].vertex_index].co.x); us.append(uvl[li].uv.x)
    if len(xs) < 2 or max(xs) - min(xs) < 1e-4:
        return 1.0
    return (max(us) - min(us)) / (max(xs) - min(xs))


def cutter_from_profile(w, profile, name):
    """profile: list of (x, z) in wall-local coords; extruded through the wall thickness."""
    k = wall_uv_scale(w)
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
    front = [bm.verts.new((x, -0.5, z)) for x, z in profile]
    back = [bm.verts.new((x, 0.5, z)) for x, z in profile]
    n = len(profile)
    faces = [bm.faces.new(front), bm.faces.new(list(reversed(back)))]
    for i in range(n):
        j = (i + 1) % n
        faces.append(bm.faces.new([front[i], front[j], back[j], back[i]]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    for f in faces:
        f.material_index = 0
        for l in f.loops:
            c = l.vert.co
            l[uv].uv = ((c.x + c.y * 0.6) * k, (c.z + c.y * 0.6) * k)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    me.materials.append(w.material_slots[0].material)
    cu = bpy.data.objects.new(name, me); bpy.context.scene.collection.objects.link(cu)
    cu.matrix_world = w.matrix_world.copy()
    return cu


def apply_cut(w, cutter):
    mod = w.modifiers.new('rotura', 'BOOLEAN')
    mod.operation = 'DIFFERENCE'; mod.solver = 'EXACT'; mod.object = cutter
    mod.material_mode = 'TRANSFER'
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    new = bpy.data.meshes.new_from_object(w.evaluated_get(dg), preserve_all_data_layers=True, depsgraph=dg)
    old = w.data; w.modifiers.remove(mod); w.data = new; new.name = old.name
    if old.users == 0:
        bpy.data.meshes.remove(old)
    bpy.data.objects.remove(cutter, do_unlink=True)


def jagged(points, amp=0.08):
    return [(x + rnd.uniform(-amp, amp), z + rnd.uniform(-amp, amp)) for x, z in points]


def notch_profile(x0, width, depth, top=2.08):
    xs = [x0 + width * t for t in (0, 0.15, 0.3, 0.45, 0.6, 0.75, 0.9, 1)]
    bottom = [(x, top - depth * math.sin(math.pi * (x - x0) / width) * rnd.uniform(0.75, 1.1)) for x in xs]
    pts = [(x0 - 0.05, top + 0.5)] + jagged(bottom[1:-1], 0.07) + [(x0 + width + 0.05, top + 0.5)]
    return pts[:1] + [(x0, top + 0.02)] + pts[1:-1] + [(x0 + width, top + 0.02)] + pts[-1:]


def hole_profile(cx, cz, rx, rz, n=9):
    return [(cx + rx * math.cos(a) * rnd.uniform(0.7, 1.15), cz + rz * math.sin(a) * rnd.uniform(0.7, 1.15))
            for a in [2 * math.pi * i / n for i in range(n)]]


def breach_profile(x0, x1, top=2.08):
    # full-height opening: narrower at the floor, jagged sides, wider at the top
    left = jagged([(x0, -0.2), (x0 + 0.05, 0.3), (x0 - 0.12, 0.7), (x0 - 0.05, 1.1), (x0 - 0.3, 1.5), (x0 - 0.45, 1.85)], 0.06)
    right = jagged([(x1 + 0.45, 1.85), (x1 + 0.3, 1.45), (x1 + 0.05, 1.05), (x1 + 0.12, 0.65), (x1 - 0.05, 0.3), (x1, -0.2)], 0.06)
    return left + [(x0 - 0.5, top + 0.5), (x1 + 0.5, top + 0.5)] + right


def outward(w):
    p = w.matrix_world.translation
    yw = (w.matrix_world.to_3x3() @ Vector((0, 1, 0))).normalized()
    return 1 if yw.dot(p - CENTER) > 0 else -1


def wall_rubble(w, spots, n, max_size, keep_clear=None, flat_in_clear=0):
    """spots: list of local x positions; rubble on both sides of the wall."""
    bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
    mw = w.matrix_world; s = mw.to_scale().x
    for _ in range(n):
        x = rnd.choice(spots) + rnd.uniform(-0.25, 0.25)
        if keep_clear and keep_clear[0] < x < keep_clear[1]:
            continue
        y = rnd.choice((-1, 1)) * rnd.uniform(0.25, 0.9)
        p = mw @ Vector((x, y, 0)); p.z = 0
        sz = rnd.uniform(0.25, max_size)
        rubble_chunk(bm, p, (sz, sz * rnd.uniform(0.6, 1.2), sz * rnd.uniform(0.4, 0.9)), rnd.choice((0, 0, 1)), uv)
    if keep_clear:
        for _ in range(flat_in_clear):        # low chips inside the opening, walkable
            x = rnd.uniform(*keep_clear); y = rnd.uniform(-0.8, 0.8)
            p = mw @ Vector((x, y, 0)); p.z = 0
            rubble_chunk(bm, p, (rnd.uniform(0.25, 0.5),) * 3, 0, uv, flat=True)
    me = bpy.data.meshes.new('Escombros_' + w.name)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces); bm.to_mesh(me); bm.free()
    for slot in w.material_slots[:2]:
        me.materials.append(slot.material)
    ob = bpy.data.objects.new('Escombros_' + w.name, me); col.objects.link(ob)
    return ob


W = bpy.data.objects
# notches from the top
for name, x0, wd, dp in [('brick_wall.003', 1.2, 1.3, 0.9), ('brick_wall.010', 2.0, 1.1, 0.7),
                         ('brick_wall.028', 0.6, 1.5, 1.1), ('brick_wall.022', 2.2, 1.0, 0.6),
                         ('brick_wall.031', 1.6, 1.2, 0.8)]:
    w = W[name]
    apply_cut(w, cutter_from_profile(w, notch_profile(x0, wd, dp), 'cut_' + name))
    wall_rubble(w, [x0 + wd * 0.3, x0 + wd * 0.5, x0 + wd * 0.7], 16, 1.1)
    LOG.append(f'muro mellado: {name}')

# holes through the wall (see-through, not passable)
for name, cx, cz, rx, rz in [('brick_wall.014', 2.2, 1.05, 0.45, 0.32), ('brick_wall.005', 1.5, 0.75, 0.35, 0.28)]:
    w = W[name]
    apply_cut(w, cutter_from_profile(w, hole_profile(cx, cz, rx, rz), 'cut_' + name))
    wall_rubble(w, [cx - 0.2, cx, cx + 0.2], 10, 0.8)
    LOG.append(f'muro agujereado: {name}')

# BREACH the player can walk through (right side wall, middle)
BREACH = 'brick_wall.019'
w = W[BREACH]
bx0, bx1 = 1.15, 2.85                      # 1.7 local = 5.1 m wide at the floor (wall is scaled x3)
apply_cut(w, cutter_from_profile(w, breach_profile(bx0, bx1), 'cut_' + BREACH))
rub = wall_rubble(w, [bx0 - 0.35, bx0 - 0.15, bx1 + 0.15, bx1 + 0.35], 22, 1.1, keep_clear=(bx0 + 0.15, bx1 - 0.15), flat_in_clear=6)
# a fallen piece of wall lying outside, off to one side of the path
bm = bmesh.new(); uv = bm.loops.layers.uv.new('UVMap')
s = outward(w)
mw = w.matrix_world
base = mw @ Vector((bx1 + 0.95, s * 1.25, 0)); base.z = 0
frag_w, frag_h, frag_t = 3.2, 0.9, 1.9      # world units, lying down
yaxis = (mw.to_3x3() @ Vector((1, 0, 0))).normalized()
rubble_chunk(bm, base, (frag_w, frag_h * 2.0, frag_t * 0.45), 0, uv)
append_me = bpy.data.meshes.new('_frag'); bm.to_mesh(append_me); bm.free()
bm2 = bmesh.new(); bm2.from_mesh(rub.data); bm2.from_mesh(append_me); bm2.to_mesh(rub.data); bm2.free()
bpy.data.meshes.remove(append_me)
pa, pb = mw @ Vector((bx0, 0, 0)), mw @ Vector((bx1, 0, 0))
LOG.append(f'BRECHA transitable: {BREACH} entre ({pa.x:.1f}, {pa.y:.1f}) y ({pb.x:.1f}, {pb.y:.1f})')

bpy.context.view_layer.update()
for l in LOG:
    print('ROTURA', l)
bpy.ops.wm.save_as_mainfile(filepath=OUT, copy=True)
