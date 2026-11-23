import bpy, bmesh, math, sys
from mathutils import Vector, Matrix
argv = sys.argv[sys.argv.index('--') + 1:]
outpath = argv[0]
cname = 'Skyscraper_Details'
if cname in bpy.data.collections:
    col = bpy.data.collections[cname]
    for o in list(col.objects):
        bpy.data.objects.remove(o, do_unlink=True)
else:
    col = bpy.data.collections.new(cname)
    bpy.context.scene.collection.children.link(col)
for me in list(bpy.data.meshes):
    if me.users == 0 and me.name.startswith('SK_'):
        bpy.data.meshes.remove(me)
P = bpy.data.objects.new('Skyscraper', None)   # root empty: move/rotate the whole building with this
P.empty_display_type = 'PLAIN_AXES'; P.empty_display_size = 20
col.objects.link(P)


def mat(name, rgb, rough=0.9, metal=0.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.diffuse_color = (*rgb, 1)
    m.use_nodes = True
    b = m.node_tree.nodes.get('Principled BSDF')
    if b:
        b.inputs['Base Color'].default_value = (*rgb, 1)
        b.inputs['Roughness'].default_value = rough
        b.inputs['Metallic'].default_value = metal
    return m


M_CON = mat('SK_Concrete', (0.62, 0.61, 0.59))
M_CON2 = mat('SK_ConcreteDark', (0.40, 0.39, 0.37))
M_GLS = mat('SK_Window', (0.03, 0.035, 0.04), 0.25)
M_MET = mat('SK_Steel', (0.12, 0.12, 0.12), 0.5, 0.8)
MATS = [M_CON, M_CON2, M_GLS, M_MET]
FACES = [(0, 3, 2, 1), (4, 5, 6, 7), (0, 1, 5, 4), (1, 2, 6, 5), (2, 3, 7, 6), (3, 0, 4, 7)]


class Builder:
    def __init__(s, name):
        s.name = name
        s.bm = bmesh.new()

    def box(s, mn, mx, mi=0):
        (x0, y0, z0), (x1, y1, z1) = mn, mx
        x0, x1 = min(x0, x1), max(x0, x1)
        y0, y1 = min(y0, y1), max(y0, y1)
        vs = [s.bm.verts.new(v) for v in [(x0, y0, z0), (x1, y0, z0), (x1, y1, z0), (x0, y1, z0),
                                         (x0, y0, z1), (x1, y0, z1), (x1, y1, z1), (x0, y1, z1)]]
        for f in FACES:
            s.bm.faces.new([vs[i] for i in f]).material_index = mi

    def beam(s, a, b, t=0.6, mi=3):
        a = Vector(a); b = Vector(b); d = b - a; L = d.length
        R = d.to_track_quat('Z', 'Y').to_matrix()
        h = t / 2
        loc = [Vector(p) for p in [(-h, -h, 0), (h, -h, 0), (h, h, 0), (-h, h, 0),
                                   (-h, -h, L), (h, -h, L), (h, h, L), (-h, h, L)]]
        vs = [s.bm.verts.new(a + (R @ p)) for p in loc]
        for f in FACES:
            s.bm.faces.new([vs[i] for i in f]).material_index = mi

    def prism(s, pts, faces, mi):
        vs = [s.bm.verts.new(p) for p in pts]
        for f in faces:
            s.bm.faces.new([vs[i] for i in f]).material_index = mi

    def done(s):
        me = bpy.data.meshes.new(s.name)
        bmesh.ops.recalc_face_normals(s.bm, faces=s.bm.faces)
        s.bm.to_mesh(me); s.bm.free()
        for m in MATS:
            me.materials.append(m)
        o = bpy.data.objects.new(s.name, me)
        col.objects.link(o)
        o.parent = P
        o.matrix_parent_inverse = Matrix.Identity(4)
        return o


WEDGE = [(0, 2, 1), (3, 4, 5), (0, 1, 4, 3), (1, 2, 5, 4), (2, 0, 3, 5)]
FL = 8.18; Z0 = 30.9; TOP = 309.0
floors = [Z0 + i * FL for i in range(34) if Z0 + i * FL + FL <= TOP + 0.1]

# ---------- 1. Crown arm to the left (-X), the "hammer" of the photo ----------
b = Builder('SK_Crown_ArmLeft')
ARM_Z0 = TOP - 4 * FL
XO = -100.0
b.box((XO, -42, ARM_Z0), (-50, 42, TOP + 2), 0)
b.box((XO - 1, -43, ARM_Z0 - 2.2), (-50, 43, ARM_Z0), 1)
for i in range(4):
    z = ARM_Z0 + i * FL
    b.box((XO - 0.4, -38, z + 2.2), (XO + 0.1, 38, z + 6.6), 2)      # glazing band
    b.box((XO - 4, -42, z), (XO, 42, z + 0.9), 0)                     # balcony slab
    b.box((XO - 4, -42, z + 0.9), (XO - 3.2, 42, z + 3.4), 1)         # parapet
    for yy in (-42, -14.4, 13.6, 41.2):
        b.box((XO - 4, yy, z), (XO, yy + 0.8, z + FL), 0)             # fins
    for sd in (-1, 1):
        y = 42 * sd
        b.box((XO + 4, y, z + 2.6), (-54, y + 0.4 * sd, z + 6.2), 2)  # side windows
b.done()

# ---------- 2. Right crown block (+X) with raking brackets ----------
b = Builder('SK_Crown_Right')
RZ0 = TOP - 3 * FL
b.box((50, -45, RZ0), (66, 45, TOP + 2), 0)
b.box((50, -46, RZ0 - 2), (67, 46, RZ0), 1)
for y in (-40, -14, 14, 40):
    b.prism([(50, y - 1, RZ0 - 2), (66, y - 1, RZ0 - 2), (50, y - 1, RZ0 - 26),
             (50, y + 1, RZ0 - 2), (66, y + 1, RZ0 - 2), (50, y + 1, RZ0 - 26)], WEDGE, 1)
for i in range(3):
    z = RZ0 + i * FL
    b.box((66, -40, z + 2.4), (66.4, 40, z + 6.4), 2)
    for sd in (-1, 1):
        y = 45 * sd
        b.box((53, y, z + 2.4), (63, y + 0.4 * sd, z + 6.4), 2)
b.done()

# ---------- 3. Roof: plant room, parapets and steel lattice tower ----------
b = Builder('SK_Roof_PlantRoom')
PR = TOP + FL * 1.4
b.box((-30, -35, TOP), (45, 35, PR), 0)
b.box((-31, -36, PR), (46, 36, PR + 1), 1)
b.box((-50, -51, TOP), (50, -50, TOP + 4), 0)
b.box((-50, 49, TOP), (50, 50, TOP + 4), 0)
b.box((XO, -42, TOP + 2), (XO + 1, 42, TOP + 5), 0)
b.box((XO, -42, TOP + 2), (-50, -41, TOP + 5), 0)
b.box((XO, 41, TOP + 2), (-50, 42, TOP + 5), 0)
b.done()

b = Builder('SK_Roof_Lattice')
base = PR + 1
H = 72
cx, cy = 8, 0
levels = [base + H * k / 5 for k in range(6)]


def half(z):
    t = (z - base) / H
    return 32 - 12 * t, 28 - 10 * t


rings = []
for z in levels:
    hx, hy = half(z)
    rings.append([(cx - hx, cy - hy, z), (cx + hx, cy - hy, z), (cx + hx, cy + hy, z), (cx - hx, cy + hy, z)])
for k in range(5):
    lo, hi = rings[k], rings[k + 1]
    for j in range(4):
        b.beam(lo[j], hi[j], 1.2)
        b.beam(lo[j], hi[(j + 1) % 4], 0.45)
        b.beam(lo[(j + 1) % 4], hi[j], 0.45)
for r in rings:
    for j in range(4):
        b.beam(r[j], r[(j + 1) % 4], 0.7)
for k in (2, 4):
    z = levels[k]; hx, hy = half(z)
    b.box((cx - hx - 3, cy - hy - 3, z - 0.4), (cx + hx + 3, cy + hy + 3, z), 3)
    for yy in (cy - hy - 3, cy + hy + 3):
        b.beam((cx - hx - 3, yy, z + 2.5), (cx + hx + 3, yy, z + 2.5), 0.3)
top = levels[-1]; hx, hy = half(top)
for (x, y, _) in rings[-1]:
    b.beam((x, y, top), (x, y, top + 10), 0.5)
b.beam((cx, cy, top), (cx, cy, top + 30), 0.9)
for z, sx, sy in [(levels[3] + 3, 1, 1), (levels[4] + 4, -1, 1), (levels[2] + 5, 1, -1)]:
    hx, hy = half(z)
    x = cx + sx * (hx + 1.5); y = cy + sy * hy * 0.4
    b.box((x - 0.8, y - 3, z - 3), (x + 0.8, y + 3, z + 3), 1)
b.done()


# ---------- 4. Window grids on the two blank faces ----------
def window_grid(name, face, us, w, h=4.2, sill=2.0, maxz=TOP):
    b = Builder(name)
    for zf in floors:
        if zf + FL > maxz + 0.1:
            break
        for u in us:
            z0 = zf + sill; z1 = z0 + h
            if face == '+Y':
                b.box((u - w / 2, 50, z0), (u + w / 2, 50.25, z1), 2)
                b.box((u - w / 2 - 0.6, 50, z0 - 0.7), (u + w / 2 + 0.6, 50.9, z0), 0)
            elif face == '-Y':
                b.box((u - w / 2, -50.25, z0), (u + w / 2, -50, z1), 2)
                b.box((u - w / 2 - 0.6, -50.9, z0 - 0.7), (u + w / 2 + 0.6, -50, z0), 0)
            elif face == '+X':
                b.box((50, u - w / 2, z0), (50.25, u + w / 2, z1), 2)
                b.box((50, u - w / 2 - 0.6, z0 - 0.7), (50.9, u + w / 2 + 0.6, z0), 0)
            else:
                b.box((-50.25, u - w / 2, z0), (-50, u + w / 2, z1), 2)
                b.box((-50.9, u - w / 2 - 0.6, z0 - 0.7), (-50, u + w / 2 + 0.6, z0), 0)
    return b.done()


window_grid('SK_Windows_Back', '+Y', [-40, -28, -16, -4, 8], 7.0)

b = Builder('SK_Balconies_Back')   # loggia stack, like the right side of the photo
for zf in floors:
    b.box((16, 50, zf), (48, 57, zf + 0.9), 0)
    b.box((16, 56.2, zf + 0.9), (48, 57, zf + 3.8), 1)
    b.box((17, 50, zf + 1.5), (47, 50.25, zf + 7.2), 2)
for x in (16, 32.4, 48):
    b.box((x - 0.8, 50, Z0), (x, 57, floors[-1] + FL), 0)
b.done()

window_grid('SK_Windows_Left', '-X', [-38, -24, -10, 10, 24, 38], 6.5, maxz=ARM_Z0 - 2.2)
b = Builder('SK_Fins_Left')
for y in (-31, 0, 31):
    b.box((-53, y - 0.8, Z0), (-50, y + 0.8, ARM_Z0 - 2.2), 0)
b.done()

# ---------- 5. Raking legs at the base ----------
b = Builder('SK_Base_Legs')
t = 2.0
for y in (-46, -16, 16, 46):
    b.prism([(-50, y - t, 0), (-64, y - t, 0), (-50, y - t, Z0), (-50, y + t, 0), (-64, y + t, 0), (-50, y + t, Z0)], WEDGE, 1)
for x in (-46, -16, 16):
    b.prism([(x - t, 50, 0), (x - t, 64, 0), (x - t, 50, Z0), (x + t, 50, 0), (x + t, 64, 0), (x + t, 50, Z0)], WEDGE, 1)
for x in (-46, -16, 16, 46):
    b.prism([(x - t, -50, 0), (x - t, -64, 0), (x - t, -50, Z0), (x + t, -50, 0), (x + t, -64, 0), (x + t, -50, Z0)], WEDGE, 1)
for y in (-46, -16, 16, 46):
    b.prism([(50, y - t, 0), (64, y - t, 0), (50, y - t, Z0), (50, y + t, 0), (64, y + t, 0), (50, y + t, Z0)], WEDGE, 1)
b.done()

# ---------- 6. Tower body (replaces the old Plane) ----------
b = Builder('SK_Tower_Body')
b.box((-50, -50, Z0), (50, 50, TOP), 0)                 # main slab
b.box((-51, -51, TOP - 1.2), (51, 51, TOP + 0.6), 1)    # roof cornice
b.box((-51, -51, Z0 - 1.6), (51, 51, Z0), 1)            # transfer slab over the lobby
b.box((-45, -45, 0), (45, 45, Z0 - 1.6), 1)             # recessed lobby core
for zf in floors[1:]:                                   # panel seams every floor
    b.box((-50.3, -50.3, zf - 0.2), (50.3, 50.3, zf + 0.2), 1)
b.done()

b = Builder('SK_Lobby')
for (a0, a1) in [(-40, -20), (-14, 14), (20, 40)]:      # glazing on the 4 lobby faces
    b.box((a0, -45.3, 3), (a1, -45, 24), 2)
    b.box((a0, 45, 3), (a1, 45.3, 24), 2)
    b.box((-45.3, a0, 3), (-45, a1, 24), 2)
    b.box((45, a0, 3), (45.3, a1, 24), 2)
for x in (-48, -30, -10, 10, 30, 48):                   # pilotis
    for y in (-48, 48):
        b.box((x - 1.5, y - 1.5, 0), (x + 1.5, y + 1.5, Z0 - 1.6), 0)
        b.box((y - 1.5, x - 1.5, 0), (y + 1.5, x + 1.5, Z0 - 1.6), 0)
b.box((-24, -66, 21), (8, -50, 23), 1)                  # entrance canopy (front, -Y)
for x in (-22, 6):
    b.box((x - 0.6, -65, 0), (x + 0.6, -63.8, 21), 3)
b.box((-14, -45.4, 0), (0, -45, 18), 3)                 # entrance doors
b.done()

# ---------- 7. Front (-Y) and right (+X) facades ----------
window_grid('SK_Windows_Front', '-Y', [-40, -28, -16, -4, 8], 7.0)
b = Builder('SK_Balconies_Front')   # loggia stack on the front, like the photo
for zf in floors:
    b.box((16, -57, zf), (48, -50, zf + 0.9), 0)
    b.box((16, -57, zf + 0.9), (48, -56.2, zf + 3.8), 1)
    b.box((17, -50.25, zf + 1.5), (47, -50, zf + 7.2), 2)
for x in (16, 32.4, 48):
    b.box((x - 0.8, -57, Z0), (x, -50, floors[-1] + FL), 0)
b.done()

window_grid('SK_Windows_Right', '+X', [-34, -22, -8, 8, 22, 34], 6.5, maxz=RZ0 - 2)
b = Builder('SK_Fins_Right')
for y in (-28, 0, 28):
    b.box((50, y - 0.8, Z0), (53, y + 0.8, RZ0 - 2), 0)
b.done()

tot = sum(len(o.data.polygons) for o in col.objects if o.type == "MESH")
print("ADDED", [o.name for o in col.objects], "faces", tot)
bpy.ops.wm.save_as_mainfile(filepath=outpath, copy=True)
