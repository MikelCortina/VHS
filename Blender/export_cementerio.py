# Exports Cementerio.blend to Unity as one FBX at real scale (x0.4: the police are 1.88 m).
# The .blend is NOT saved: everything below happens on the loaded copy.
#  - static scenery is merged by category: Suelo, Tumbas, Muros, Puertas, Escombros, Farolas
#  - each hanged body is baked to a static mesh and grouped with its noose and straight rope
#    under an empty "Ahorcado_NN" whose origin is the top of the rope (pivot for swinging in Unity)
#  - bodies without a rope are baked in place as "Cuerpo_NN"
#  - materials are consolidated by base name (brick.012 -> brick ...) so Unity can remap them
# Usage: blender -b Cementerio.blend --python export_cementerio.py -- <out.fbx>
import bpy, bmesh, sys
from mathutils import Vector, Matrix

OUT = sys.argv[sys.argv.index('--') + 1:][0]
SCALE = 0.4
sc = bpy.context.scene
dg = bpy.context.evaluated_depsgraph_get()

LAMP_IMG = 'Lamp.png'


def canon(mat):
    if mat is None:
        return 'M_Tumba'
    if mat.node_tree and any(n.type == 'TEX_IMAGE' and n.image and n.image.name.startswith(LAMP_IMG) for n in mat.node_tree.nodes):
        return 'Farola'
    return mat.name.split('.')[0]


canon_mats = {}
def cmat(name, src):
    if name not in canon_mats:
        m = bpy.data.materials.get(name)
        if m is None:
            m = src.copy() if src else bpy.data.materials.new(name)
            m.name = name
        canon_mats[name] = m
    return canon_mats[name]


def baked_world_mesh(o):
    """Evaluated (modifiers/armature/constraints applied) mesh of o, in world space."""
    ev = o.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    me.transform(ev.matrix_world)
    return me, [canon(s.material) for s in o.material_slots], [s.material for s in o.material_slots]


def merge(name, objs, origin=Vector((0, 0, 0))):
    bm = bmesh.new(); names = []
    for o in objs:
        me, cn, srcs = baked_world_mesh(o)
        for c, s in zip(cn, srcs):
            if c not in names:
                names.append(c); cmat(c, s)
        remap = [names.index(c) for c in cn] or [0]
        tmp = bmesh.new(); tmp.from_mesh(me)
        for f in tmp.faces:
            f.material_index = remap[min(f.material_index, len(remap) - 1)]
        tmp.to_mesh(me); tmp.free()
        bm.from_mesh(me); bpy.data.meshes.remove(me)
    if not names:
        names = ['M_Tumba']; cmat('M_Tumba', None)
    bmesh.ops.translate(bm, verts=bm.verts, vec=-origin)
    me = bpy.data.meshes.new(name); bm.to_mesh(me); bm.free()
    for c in names:
        me.materials.append(canon_mats[c])
    ob = bpy.data.objects.new(name, me); ob.location = origin
    return ob


objs = [o for o in sc.objects if o.type == 'MESH']
arms = [o for o in sc.objects if o.type == 'ARMATURE']
tramos = {o.constraints[0].target: o for o in objs if o.name.startswith('Cuerda_Tramo') and o.constraints
          and o.constraints[0].type == 'STRETCH_TO' and o.constraints[0].target}
used = set()
new_objs = []

# ---- hanged bodies / bodies
hang_i = body_i = 0
for a in sorted(arms, key=lambda o: o.name):
    kids = [o for o in objs if o.parent == a]
    body = [o for o in kids if o.parent_type != 'BONE']
    noose = [o for o in kids if o.parent_type == 'BONE']
    anchor = next((e for e in sc.objects if e.type == 'EMPTY' and e.parent == a and e in tramos), None)
    tramo = tramos.get(anchor)
    used.update(kids); used.add(a)
    if tramo:
        used.add(tramo)
        hang_i += 1
        pivot = tramo.matrix_world.translation.copy()
        root = bpy.data.objects.new(f'Ahorcado_{hang_i:02d}', None); root.location = pivot
        new_objs.append(root)
        for nm, group in (('Cuerpo', body), ('Lazo', noose), ('Cuerda', [tramo])):
            if group:
                ob = merge(f'Ahorcado_{hang_i:02d}_{nm}', group, origin=pivot)
                ob.location = Vector((0, 0, 0)); ob.parent = root
                new_objs.append(ob)
    elif body:
        body_i += 1
        new_objs.append(merge(f'Cuerpo_{body_i:02d}', body + noose))

# ---- static scenery by category
cats = {'Suelo': [], 'Tumbas': [], 'Muros': [], 'Puertas': [], 'Escombros': [], 'Farolas': []}
for o in objs:
    if o in used or o.name.startswith(('Cuerda_Tramo', 'Cuerda_Lazo')):
        continue
    names = {canon(s.material) for s in o.material_slots}
    if o.name.startswith('Escombros'):
        cats['Escombros'].append(o)
    elif o.name.startswith('brick_wall_gate'):
        cats['Puertas'].append(o)
    elif o.name.startswith('brick'):
        cats['Muros'].append(o)
    elif 'Farola' in names or o.name.startswith('Cuerda_Vueltas'):
        cats['Farolas'].append(o)
    elif names <= {'M_Suelo_Cementerio', 'M_Tumba'} and all(p.material_index == 0 for p in o.data.polygons):
        cats['Suelo'].append(o)
    else:
        cats['Tumbas'].append(o)
for k, v in cats.items():
    if v:
        new_objs.append(merge(k, v))
    print('CAT', k, len(v))

# ---- swap scene content for the merged objects and export
exp = bpy.data.collections.new('EXPORT'); sc.collection.children.link(exp)
for o in new_objs:
    exp.objects.link(o)
bpy.context.view_layer.update()
for o in list(sc.objects):
    o.select_set(o.name in exp.objects)
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH', 'EMPTY'},
                         global_scale=SCALE, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         bake_space_transform=True, mesh_smooth_type='FACE', add_leaf_bones=False,
                         bake_anim=False, path_mode='STRIP')
print('EXPORTED', OUT, 'hanged', hang_i, 'bodies', body_i, 'materials', sorted(canon_mats))
for o in new_objs:
    if o.type == 'MESH':
        print('OBJ', o.name, len(o.data.polygons), [m.name for m in o.data.materials])
