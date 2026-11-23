# HabitacionFinal Demo.blend: fixes z-fighting ("planos que clipean") and UVs of the room meshes.
#  1. Pillar ("Cylinder"): the ring's top/bottom caps were 16 quads joining opposite corners (they crossed the whole
#     pillar and overlapped each other) -> replaced by a proper octagonal annulus at each height.
#  2. Every pair of coplanar overlapping faces left (beam/ledge blocks flush with the ledge, back-to-back caps...):
#     same-facing -> a face fully covered by a bigger one is deleted, otherwise the smaller one is lifted EPS along its
#     normal; back-to-back -> each moved EPS/2 along its own normal. Face orientation is never changed.
#  3. UVs of Cylinder, Cylinder.001, Cylinder.002: world-space plane projection per face (PSX style, no stretching),
#     one repeat every TILE Blender units (5 u = 2 m in Unity at the x0.4 export scale).
# Usage: blender -b "HabitacionFinal Demo.blend" --python habitacion_limpieza.py -- [save]
import bpy, bmesh, math, sys
from collections import defaultdict
from mathutils import Vector, kdtree
from mathutils.geometry import intersect_point_tri

EPS = 0.02
TILE = 5.0
if bpy.context.view_layer.objects.active and bpy.context.view_layer.objects.active.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
O = bpy.data.objects


# ---------- 1. pillar ring caps
def fix_ring(ob):
    me = ob.data; mw = ob.matrix_world; mwi = mw.inverted()
    bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    W = lambda v: mw @ v.co
    bad = []
    for f in bm.faces:
        n = (mw.to_3x3() @ f.normal).normalized()
        c = sum((W(v) for v in f.verts), Vector()) / len(f.verts)
        if abs(n.z) > 0.99 and len(f.verts) == 4 and math.hypot(c.x, c.y) < 5.0:     # quads only (a full octagon cap is fine)
            r = [math.hypot(W(v).x, W(v).y) for v in f.verts]
            if max(r) > 9.0 and min(r) > 6.0:            # crosses the pillar, from corner to opposite corner
                bad.append(f)
    if not bad:
        bm.free(); return 0
    slot = bad[0].material_index
    caps = defaultdict(list)
    for f in bad:
        caps[round(W(f.verts[0]).z, 3)].append((mw.to_3x3() @ f.normal).z)
    r_in = min(math.hypot(W(v).x, W(v).y) for f in bad for v in f.verts)
    r_out = max(math.hypot(W(v).x, W(v).y) for f in bad for v in f.verts)
    ang0 = math.atan2(W(bad[0].verts[0]).y, W(bad[0].verts[0]).x)
    angs = sorted({round(math.degrees(math.atan2(W(v).y, W(v).x)) % 360, 1) for f in bad for v in f.verts})
    bmesh.ops.delete(bm, geom=bad, context='FACES')
    kd = kdtree.KDTree(len(bm.verts))
    for i, v in enumerate(bm.verts): kd.insert(W(v), i)
    kd.balance(); bm.verts.ensure_lookup_table()

    def vert(p):
        co, i, d = kd.find(p)
        return bm.verts[i] if d < 1e-3 else bm.verts.new(mwi @ p)
    made = 0
    for z, ns in caps.items():
        up = sum(ns) > 0
        for k in range(len(angs)):
            a0, a1 = math.radians(angs[k]), math.radians(angs[(k + 1) % len(angs)])
            q = [vert(Vector((r * math.cos(a), r * math.sin(a), z))) for r, a in ((r_in, a0), (r_out, a0), (r_out, a1), (r_in, a1))]
            if not up: q.reverse()
            try:
                f = bm.faces.new(q); f.material_index = slot; made += 1
            except ValueError:
                pass
    bm.normal_update()
    bm.to_mesh(me); bm.free(); me.update()
    print('RING', ob.name, 'removed', len(bad), 'caps', made, 'r', round(r_in, 2), round(r_out, 2), 'angles', angs)
    return made


# ---------- 2. coplanar overlaps
def canon(n):
    for c in n:
        if abs(c) > 1e-3: return n if c > 0 else -n
    return n


def inside(p, f):
    vs = [v.co for v in f.verts]
    for i in range(1, len(vs) - 1):
        if intersect_point_tri(p, vs[0], vs[i], vs[i + 1]) is not None:
            return True
    return False


def samples(f):
    c = f.calc_center_median()
    return [c] + [c + (v.co - c) * t for v in f.verts for t in (0.3, 0.7, 0.95)]


def overlaps(bm):
    groups = defaultdict(list)
    for f in bm.faces:
        if f.calc_area() < 1e-4:
            continue
        n = canon(f.normal.copy())
        groups[(round(n.x, 2), round(n.y, 2), round(n.z, 2), round(n.dot(f.verts[0].co), 1))].append(f)
    out = []
    for fs in groups.values():
        for i, a in enumerate(fs):
            for b in fs[i + 1:]:
                if abs(canon(a.normal.copy()).dot(a.verts[0].co) - canon(b.normal.copy()).dot(b.verts[0].co)) > 0.02:
                    continue
                hb = sum(1 for p in samples(b) if inside(p, a)); ha = sum(1 for p in samples(a) if inside(p, b))
                if hb >= 2 or ha >= 2:
                    out.append((a, b))
    return out


def fix_overlaps(ob):
    me = ob.data; mw = ob.matrix_world; mwi = mw.inverted()
    bm = bmesh.new(); bm.from_mesh(me); bm.transform(mw); bm.normal_update()   # work in world space (normals after transform)
    pairs = overlaps(bm); moves = defaultdict(Vector)
    # a face lying completely inside a bigger coplanar face facing the same way is a duplicate: delete it
    dup = set()
    for a, b in pairs:
        if a.normal.dot(b.normal) > 0:
            s, l = (a, b) if a.calc_area() < b.calc_area() else (b, a)
            if l not in dup and all(inside(p, l) for p in samples(s)):
                dup.add(s)
    if dup:
        for s in dup: print('  DUP', [round(x, 2) for x in s.calc_center_median()], 'area', round(s.calc_area(), 2), 'n', [round(x, 2) for x in s.normal])
        bmesh.ops.delete(bm, geom=list(dup), context='FACES_ONLY')
        bm.normal_update(); pairs = overlaps(bm)
    print('DUPLICATES', ob.name, len(dup))
    for a, b in pairs:
        if a.normal.dot(b.normal) > 0:
            s = a if a.calc_area() < b.calc_area() else b
            for v in s.verts: moves[v] = moves[v] + s.normal * EPS if moves[v].length < EPS * 0.5 else moves[v]
        else:
            for s in (a, b):
                for v in s.verts: moves[v] = moves[v] + s.normal * EPS * 0.5 if moves[v].length < EPS * 0.25 else moves[v]   # back-to-back: each towards its own viewer
    for v, d in moves.items():
        v.co += d
    bm.normal_update(); left = overlaps(bm)
    for a, b in left: print("  LEFT", [round(x, 2) for x in a.calc_center_median()], [round(x, 2) for x in b.calc_center_median()], round(a.calc_area(), 2), round(b.calc_area(), 2), "same" if a.normal.dot(b.normal) > 0 else "opp")
    left = len(left)
    bm.transform(mwi); bm.to_mesh(me); bm.free(); me.update()
    print('OVERLAPS', ob.name, 'fixed', len(pairs), 'left', left)


def fix_pillar_normals(ob, r_min=6.0):
    """vertical faces on the pillar's outer surface (radius >= r_min) must face away from its axis; Unity culls
    back faces, so an inverted wall is invisible from the room"""
    me = ob.data; mw = ob.matrix_world; nm = mw.to_3x3().inverted().transposed()
    bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    flip = []
    for f in bm.faces:
        n = (nm @ f.normal).normalized(); c = mw @ f.calc_center_median(); rad = Vector((c.x, c.y, 0))
        if abs(n.z) < 0.3 and rad.length >= r_min and n.dot(rad.normalized()) < -0.5:
            flip.append(f)
    bmesh.ops.reverse_faces(bm, faces=flip)
    bm.to_mesh(me); bm.free(); me.update()
    print('FLIPPED', ob.name, len(flip))


# ---------- 3. world-space UVs
def world_uvs(ob):
    me = ob.data; mw = ob.matrix_world; rot = mw.to_3x3()
    bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    uvl = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')
    for f in bm.faces:
        n = (rot @ f.normal).normalized()
        if abs(n.z) > 0.7:
            t, b = Vector((1, 0, 0)), Vector((0, 1, 0))
        else:
            t = Vector((0, 0, 1)).cross(n).normalized(); b = n.cross(t).normalized()
        for l in f.loops:
            p = mw @ l.vert.co
            l[uvl].uv = (p.dot(t) / TILE, p.dot(b) / TILE)
    bm.to_mesh(me); bm.free(); me.update()
    print('UVS', ob.name, len(me.polygons))


fix_ring(O['Cylinder'])
# fix_pillar_normals(O['Cylinder'])   # NOT used: the user's face orientation is intentional (the tube is seen from inside)
for n in [x for x in ('Cylinder', 'Cylinder.002', 'Cylinder.001') if x in O]:
    fix_overlaps(O[n])
for n in [x for x in ('Cylinder', 'Cylinder.002', 'Cylinder.001') if x in O]:
    world_uvs(O[n])
if '--' in sys.argv and 'save' in sys.argv[sys.argv.index('--') + 1:]:
    bpy.ops.wm.save_mainfile(); print('SAVED', bpy.data.filepath)
