# Skyscraper: balcony barriers (parapets) get their own material, SK_Barandilla (appended as slot 5 so the
# existing slots 0-3, and any per-slot material assignments in Unity scenes, stay the same).
#  - SK_Balconies_Front / _Back: every SK_ConcreteDark face is a parapet
#  - SK_Crown_ArmLeft: the SK_ConcreteDark faces of the thin parapet boxes at the outer edge of the arm
# Usage: blender -b "Skyscrapper 1.blend" --python separa_barandillas.py -- <T_Barandilla.png>
import bpy, sys
TEX = sys.argv[sys.argv.index('--') + 1:][0]

m = bpy.data.materials.get('SK_Barandilla') or bpy.data.materials.new('SK_Barandilla')
m.use_nodes = True; nt = m.node_tree
bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
for n in [n for n in nt.nodes if n.type == 'TEX_IMAGE']:
    nt.nodes.remove(n)
t = nt.nodes.new('ShaderNodeTexImage'); t.image = bpy.data.images.load(TEX, check_existing=True)
t.image.filepath = bpy.path.relpath(TEX); t.interpolation = 'Closest'; t.location = (-350, 250)
nt.links.new(t.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 0.8
m.diffuse_color = (0.36, 0.42, 0.45, 1)

col = bpy.data.collections['Skyscraper_Details']
total = 0
for o in col.objects:
    if o.type != 'MESH':
        continue
    me = o.data
    if 'SK_Barandilla' not in [x.name for x in me.materials if x]:
        me.materials.append(m)                       # every SK_ mesh gets the same 5 slots (export merges them)
    bi = [x.name for x in me.materials].index('SK_Barandilla')
    n = 0
    for p in me.polygons:
        if p.material_index != 1:
            continue
        c = o.matrix_world @ p.center
        if o.name.startswith('SK_Balconies_'):
            p.material_index = bi; n += 1
        elif o.name == 'SK_Crown_ArmLeft':
            xs = [(o.matrix_world @ me.vertices[v].co).x for v in p.vertices]
            if min(xs) >= -104.01 and max(xs) <= -103.19:      # parapet box: x from XO-4 to XO-3.2 (XO = -100)
                p.material_index = bi; n += 1
    if n:
        print('BARRIER', o.name, n)
    total += n
print('BARRIER total', total)
bpy.ops.wm.save_mainfile()
print('SAVED_OK')
