# Exports Edificio_Brutalista to Unity as one FBX in metres (1 Blender unit = 1 m), pivot at its base.
# The .blend is NOT saved. Prints "MAT <name> | <base image> | <emission image>" for every material used.
# Usage: blender -b Edificio_Brutalista.blend --python export_brutalista.py -- <out.fbx>
import bpy, sys

OUT = sys.argv[sys.argv.index('--') + 1:][0]
col = bpy.data.collections.get('Edificio_Brutalista')
objs = [o for o in (col.all_objects if col else bpy.context.scene.objects) if o.type == 'MESH']
for o in bpy.context.scene.objects:
    o.select_set(o in objs)
bpy.context.view_layer.objects.active = objs[0]
bpy.ops.export_scene.fbx(filepath=OUT, use_selection=True, object_types={'MESH'}, global_scale=1.0,
                         apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                         bake_space_transform=True, mesh_smooth_type='FACE', use_mesh_modifiers=True,
                         add_leaf_bones=False, bake_anim=False, path_mode='STRIP')
used = set()
for o in objs:
    print('OBJ', o.name, len(o.data.polygons), tuple(round(d, 2) for d in o.dimensions))
    used |= {m for m in o.data.materials if m}
for m in sorted(used, key=lambda m: m.name):
    base = emi = ''
    if m.node_tree:
        bsdf = next((n for n in m.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        for l in m.node_tree.links:
            if l.from_node.type == 'TEX_IMAGE' and l.to_node == bsdf and l.from_node.image:
                p = bpy.path.abspath(l.from_node.image.filepath)
                if l.to_socket.name == 'Base Color': base = p
                elif l.to_socket.name == 'Emission Color': emi = p
    print('MAT', m.name, '|', base, '|', emi)
print('EXPORTED', OUT)
