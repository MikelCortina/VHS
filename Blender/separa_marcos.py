# Run INSIDE Blender (Scripting tab > Open > Run Script / Alt+P). Works in Object or Edit mode, unsaved changes are fine.
# Moves the window frames of Edificio_Brutalista (or the selected meshes) to their own material, M_Marco_Ventana.
# A window = small island of M_Ranura_Oscura faces (every side < MAX_SIZE m). In each island the biggest
# face(s) facing out stay dark glass; the thin faces around them (the frame) get M_Marco_Ventana.
# Frame UVs: world-space projection on each face's plane, 1 texture repeat every 2 m (same style as the rest).
import bpy, bmesh, os
from mathutils import Vector

GLASS_MAT = 'M_Ranura_Oscura'
FRAME_MAT = 'M_Marco_Ventana'
FRAME_TEX = r'C:\Users\mkmik\Desktop\VHS\Texturas Edificio\T_Marco_Ventana.png'
MAX_SIZE = 2.5
TILE = 2.0


def frame_material():
    m = bpy.data.materials.get(FRAME_MAT)
    if m:
        return m
    m = bpy.data.materials.new(FRAME_MAT); m.use_nodes = True
    nt = m.node_tree; bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
    img = bpy.data.images.load(FRAME_TEX, check_existing=True)
    if bpy.data.filepath:
        img.filepath = bpy.path.relpath(FRAME_TEX)
    t = nt.nodes.new('ShaderNodeTexImage'); t.image = img; t.interpolation = 'Closest'; t.location = (-350, 250)
    nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
    bsdf.inputs['Roughness'].default_value = 0.8
    m.diffuse_color = (0.33, 0.4, 0.4, 1)
    return m


def plane_uv(n, p):
    if n.length < 1e-6 or abs(n.normalized().z) > 0.7:
        return (p.x / TILE, p.y / TILE)
    n = n.normalized(); t = Vector((0, 0, 1)).cross(n).normalized(); b = n.cross(t).normalized()
    return (p.dot(t) / TILE, p.dot(b) / TILE)


objs = [o for o in bpy.context.selected_objects if o.type == 'MESH'] or [bpy.data.objects['Edificio_Brutalista']]
mode = bpy.context.object.mode if bpy.context.object else 'OBJECT'
if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
fmat = frame_material()

for ob in objs:
    me = ob.data
    names = [m.name if m else '' for m in me.materials]
    if GLASS_MAT not in names:
        continue
    gi = names.index(GLASS_MAT)
    if FRAME_MAT not in names:
        me.materials.append(fmat); names.append(FRAME_MAT)
    fi = names.index(FRAME_MAT)
    mw = ob.matrix_world; rot = mw.to_3x3()
    bm = bmesh.new(); bm.from_mesh(me); bm.normal_update()
    uv = bm.loops.layers.uv.active or bm.loops.layers.uv.new('UVMap')

    # islands of dark faces (connected through shared vertices)
    todo = {f for f in bm.faces if f.material_index in (gi, fi)}
    windows = frames = 0
    while todo:
        seed = todo.pop(); island = [seed]; stack = [seed]
        while stack:
            f = stack.pop()
            for v in f.verts:
                for g in v.link_faces:
                    if g in todo:
                        todo.remove(g); island.append(g); stack.append(g)
        co = [mw @ v.co for f in island for v in f.verts]
        size = [max(c[i] for c in co) - min(c[i] for c in co) for i in range(3)]
        if max(size) > MAX_SIZE or len(island) < 3:
            continue                                     # big slots, doors, panels: leave them alone
        big = max(f.calc_area() for f in island)
        front = max((f for f in island if f.calc_area() > big * 0.6),
                    key=lambda f: (mw @ f.calc_center_median() - sum(co, Vector()) / len(co)).dot(rot @ f.normal))
        fn = (rot @ front.normal).normalized()
        for f in island:
            n = (rot @ f.normal)
            is_glass = f.calc_area() > big * 0.6 and n.normalized().dot(fn) > 0.95
            f.material_index = gi if is_glass else fi
            if not is_glass:
                frames += 1
                for l in f.loops:
                    l[uv].uv = plane_uv(n, mw @ l.vert.co)
        windows += 1
    bm.to_mesh(me); bm.free(); me.update()
    print(f'{ob.name}: {windows} windows, {frames} frame faces -> {FRAME_MAT}')

if mode != 'OBJECT':
    bpy.ops.object.mode_set(mode=mode)
