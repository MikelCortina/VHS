# HabitacionFinal Demo.blend: the pose the user set by hand on "Armature" is not keyed, so the shared mixamo action
# overwrites it every time the scene is evaluated (and it never reaches the copies or Unity).
# This reads that hand-made pose straight from the file (before the action is evaluated), bakes it into a new
# one-frame action "Pose_Colgado", assigns it to every hanged person (Armature + Armature_P*), and then re-runs
# habitacion_postes.py so the copies and ropes are rebuilt and the ankle rope is fitted to the new legs.
# Usage: blender -b --python pose_colgado.py -- "<file.blend>"
import bpy, sys, os
F = sys.argv[sys.argv.index('--') + 1:][0]
HERE = os.path.dirname(os.path.abspath(__file__))
RAW = {}


@bpy.app.handlers.persistent
def grab(*_):
    if RAW: return
    for pb in bpy.data.objects['Armature'].pose.bones:
        RAW[pb.name] = (pb.location.copy(), pb.rotation_quaternion.copy(), pb.rotation_euler.copy(), pb.scale.copy())


bpy.app.handlers.load_post.append(grab)
bpy.ops.wm.open_mainfile(filepath=F, load_ui=False)
bpy.app.handlers.load_post.remove(grab)

arm = bpy.data.objects['Armature']
old = bpy.data.actions.get('Pose_Colgado')
if old: bpy.data.actions.remove(old)
arm.animation_data_create(); arm.animation_data.action = None
for pb in arm.pose.bones:
    loc, q, e, s = RAW[pb.name]
    pb.location, pb.rotation_quaternion, pb.rotation_euler, pb.scale = loc, q, e, s
    for path in ('location', 'rotation_quaternion' if pb.rotation_mode == 'QUATERNION' else 'rotation_euler', 'scale'):
        pb.keyframe_insert(path, frame=1)
act = arm.animation_data.action; act.name = 'Pose_Colgado'; act.use_fake_user = True
print('BAKED', len(RAW), 'bones into', act.name)

sys.argv = [sys.argv[0], '--', 'save']
exec(compile(open(os.path.join(HERE, 'habitacion_postes.py')).read(), 'habitacion_postes.py', 'exec'))
