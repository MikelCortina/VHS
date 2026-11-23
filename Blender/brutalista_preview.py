# Renders a preview of Edificio_Brutalista from a low street-level angle, foggy grey sky.
# Usage: blender -b Edificio_Brutalista.blend --python brutalista_preview.py -- <out.png>
import bpy, sys, math
from mathutils import Vector

OUT = sys.argv[sys.argv.index('--') + 1:][0]
sc = bpy.context.scene
cam_d = bpy.data.cameras.new('PrevCam'); cam_d.lens = 18
cam = bpy.data.objects.new('PrevCam', cam_d); sc.collection.objects.link(cam)
cam.location = Vector((-4, -78, 1.7))
target = Vector((34, 0, 44))
cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
sc.camera = cam
sun_d = bpy.data.lights.new('PrevSun', 'SUN'); sun_d.energy = 3.0
sun = bpy.data.objects.new('PrevSun', sun_d); sc.collection.objects.link(sun)
sun.rotation_euler = (math.radians(55), 0, math.radians(-75))
w = bpy.data.worlds.new('PrevWorld'); w.use_nodes = True
bg = w.node_tree.nodes['Background']; bg.inputs['Color'].default_value = (0.55, 0.58, 0.62, 1); bg.inputs['Strength'].default_value = 0.9
sc.world = w
bpy.ops.mesh.primitive_plane_add(size=600, location=(30, 0, -0.02))
sc.render.engine = 'BLENDER_EEVEE'
sc.render.resolution_x, sc.render.resolution_y = 900, 1150
sc.render.image_settings.file_format = 'JPEG'
sc.render.filepath = OUT
bpy.ops.render.render(write_still=True)
