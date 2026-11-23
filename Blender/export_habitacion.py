# Exports "HabitacionFinal Demo.blend" to Unity as one FBX at real scale (x0.4, same as the cemetery: the hanged
# Characters_psx bodies are ~4.5 units -> 1.8 m). The .blend is NOT saved: everything happens on the loaded copy.
#  - each hanged person is baked (pose applied) to a static mesh and grouped with its hanging rope + ankle lashing
#    under an empty "Colgado_NN" whose origin is where the rope leaves the beam (pivot for swinging in Unity);
#    the coils wrapped round the beams stay static in "Amarres"
#  - static scenery merged by part: Sala (walls/floor/beams), Pilar, Televisores, Farolas, Cables (curves -> mesh)
#  - materials get stable names so Unity can remap them to the .mat files in Assets/Materials/Habitacion
# Usage: blender -b "HabitacionFinal Demo.blend" --python export_habitacion.py -- <out.fbx>
import bpy, bmesh, sys, math
from mathutils import Vector

OUT = sys.argv[sys.argv.index('--') + 1:][0]
SCALE = 0.4
HANG = Vector((36.1, 0.0, 30.06))        # rope drop start, in the rope's own (sector) frame -> see habitacion_postes.py
sc = bpy.context.scene
if bpy.context.view_layer.objects.active and bpy.context.view_layer.objects.active.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
dg = bpy.context.evaluated_depsgraph_get()
RENAME = {'Character_31': 'M_Character_31', 'Material.001': 'M_TV_Caja', 'Material.003': 'M_TV_Pantalla',
          'Material.002': 'M_Farola', 'M_Cuerda': 'M_Cuerda',
          'Material': 'M_Habitacion_Pilar', 'Material.004': 'M_Habitacion_Pilar_Anillo',      # user's slots (pillar shaft / ring+top)
          'Material.005': 'M_Habitacion_Sala', 'Material.006': 'M_Habitacion_Vigas'}         # (walls+floor+ceiling / beams+ledge)
mats = {}


def mat(name):
    if name not in mats:
        old = bpy.data.materials.get(name)
        if old: old.name = '__src_' + name          # keep the exported name exact (no .001)
        m = bpy.data.materials.new(name); mats[name] = m
    return mats[name]


def baked(o, default):
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    me.transform(ev.matrix_world)
    names = [RENAME.get(s.material.name.removeprefix('__src_'), s.material.name.removeprefix('__src_')) if s.material else default for s in o.material_slots] or [default]
    return me, names


def build(name, parts, origin=Vector((0, 0, 0)), keep=None):
    """parts: list of (object, default material). keep(face_center)->bool filters faces (by connected island)."""
    bm = bmesh.new(); names = []
    for o, default in parts:
        me, cn = baked(o, default)
        for c in cn:
            if c not in names: names.append(c)
        remap = [names.index(c) for c in cn]
        tmp = bmesh.new(); tmp.from_mesh(me)
        for f in tmp.faces:
            f.material_index = remap[min(f.material_index, len(remap) - 1)]
        if keep:
            todo = set(tmp.faces); drop = []
            while todo:
                s = todo.pop(); isl = [s]; st = [s]
                while st:
                    f = st.pop()
                    for v in f.verts:
                        for g in v.link_faces:
                            if g in todo: todo.remove(g); isl.append(g); st.append(g)
                if not keep(isl): drop += isl
            bmesh.ops.delete(tmp, geom=drop, context='FACES')
        tmp.to_mesh(me); tmp.free()
        bm.from_mesh(me); bpy.data.meshes.remove(me)
    bmesh.ops.translate(bm, verts=bm.verts, vec=-origin)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for c in names:
        me.materials.append(mat(c))
    ob = bpy.data.objects.new(name, me); ob.location = origin
    return ob


O = bpy.data.objects
new = []
coils = []
for k in range(8):
    suf = '' if k == 0 else f'_P{k}'
    body, rope = O['Character_31' + suf], O['Cuerda_Poste' + suf]
    pivot = rope.matrix_world @ HANG
    top = lambda isl, z=pivot.z: max(v.co.z for f in isl for v in f.verts) > z + 0.05     # coils round the beam
    root = bpy.data.objects.new(f'Colgado_{k + 1:02d}', None); root.location = pivot; new.append(root)
    for nm, parts, keep in (('Cuerpo', [(body, 'M_Character_31')], None),
                            ('Cuerda', [(rope, 'M_Cuerda')], lambda isl, t=top: not t(isl))):
        ob = build(f'Colgado_{k + 1:02d}_{nm}', parts, origin=pivot, keep=keep)
        ob.location = Vector((0, 0, 0)); ob.parent = root; new.append(ob)
    coils.append((rope, top))
# static coils (one object for the 8 beams)
am = bpy.data.meshes.new('Amarres'); bm = bmesh.new()
for rope, top in coils:
    ob = build('tmp', [(rope, 'M_Cuerda')], keep=top); bm.from_mesh(ob.data)
    bpy.data.meshes.remove(ob.data)
bm.to_mesh(am); bm.free(); am.materials.append(mat('M_Cuerda')); new.append(bpy.data.objects.new('Amarres', am))

groups = {
    'Sala': ([O[n] for n in ('Cylinder.002', 'Cylinder.001') if n in O], 'M_Habitacion_Sala'),
    'Pilar': ([O['Cylinder']], 'M_Habitacion_Pilar'),
    'Televisores': ([o for o in O if o.name.startswith('TV4')], 'M_TV_Caja'),
    'Farolas': ([o for o in O if o.name == 'Cube' or o.name.startswith('Cube_L')], 'M_Farola'),
    'Cables': ([o for o in O if o.type == 'CURVE' and o.name.startswith('BézierCurve')], 'M_Habitacion_Cable'),
}
for name, (objs, default) in groups.items():
    new.append(build(name, [(o, default) for o in objs])); print('GROUP', name, len(objs))

exp = bpy.data.collections.new('EXPORT'); sc.collection.children.link(exp)
for o in new:
    exp.objects.link(o)
bpy.context.view_layer.update()
for o in list(sc.objects):
    o.select_set(o.name in exp.objects)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH', 'EMPTY'},
                         global_scale=SCALE, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP')
print('EXPORTED', OUT, 'materials', sorted(mats))
for o in new:
    if o.type == 'MESH':
        print('OBJ', o.name, len(o.data.polygons), [m.name for m in o.data.materials])
