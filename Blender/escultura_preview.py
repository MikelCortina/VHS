# Preview render of Escultura_Monumento from the front, foggy, low camera (like the reference photo).
# Usage: blender -b Escultura_Monumento.blend --python escultura_preview.py -- <out.jpg> [cam_x cam_y cam_z]
import bpy, sys, math
from mathutils import Vector
a = sys.argv[sys.argv.index('--') + 1:]
OUT = a[0]; cl = Vector(tuple(float(x) for x in a[1:4])) if len(a) >= 4 else Vector((0, -38, 1.7))
sc = bpy.context.scene
cd = bpy.data.cameras.new('C'); cd.lens = 24; cam = bpy.data.objects.new('C', cd); sc.collection.objects.link(cam)
cam.location = cl; t = Vector((0, 0, 14))
cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler(); sc.camera = cam
sd = bpy.data.lights.new('S', 'SUN'); sd.energy = 2.0; s = bpy.data.objects.new('S', sd); sc.collection.objects.link(s)
s.rotation_euler = (math.radians(35), 0, math.radians(-40))
w = bpy.data.worlds.new('W'); w.use_nodes = True; bg = w.node_tree.nodes['Background']
bg.inputs['Color'].default_value = (0.82, 0.83, 0.84, 1); bg.inputs['Strength'].default_value = 1.0; sc.world = w
sc.render.engine = 'BLENDER_EEVEE'; sc.render.resolution_x, sc.render.resolution_y = 900, 600
sc.render.image_settings.file_format = 'JPEG'; sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
