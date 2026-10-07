"""Export existing SAS locomotion actions to the shared lobby character hierarchy.

Blender --background --python export_training_locomotion.py -- --output <folder>
Only the rig and animation curves are exported; original source assets are preserved.
"""
import argparse
import json
import sys
from pathlib import Path
import bpy
from mathutils import Matrix

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
source = Path(__file__).resolve().parents[1] / 'CT_SAS/ctm_sas_animated.blend'
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
names = [f'{gait}_{direction}_rifle' for gait in ('walk', 'run')
         for direction in ('n', 'ne', 'e', 'se', 's', 'sw', 'w', 'nw')]
names += [f'jump_{direction}_rifle' for direction in ('stand', 'n', 'e', 's', 'w')]
names += ['idle_rifle']  # Combat aim pose, kept after the motion clips so existing ranges stay stable.
with bpy.data.libraries.load(str(source), link=False) as (src, dst):
    dst.objects = ['CT_SAS_Rig']
    dst.actions = list(names)
rig = dst.objects[0]
bpy.context.scene.collection.objects.link(rig)
rig.name = 'LobbyRig'
rig.animation_data_clear()
rig.matrix_world = Matrix.Identity(4)
for bone in rig.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
    bone.rotation_mode = 'QUATERNION'
root = bpy.data.objects.new('LoadoutModel', None)
bpy.context.scene.collection.objects.link(root)
rig.parent = root
sequence = bpy.data.actions.new('TrainingLocomotion')
slot = sequence.slots.new('OBJECT', rig.name)
bag = sequence.layers.new('Locomotion').strips.new(type='KEYFRAME').channelbag(slot, ensure=True)
curves, clips = {}, []
start = 1
for name, action in zip(names, dst.actions):
    assert action is not None, f'Missing source action: {name}'
    count = int(action.frame_end - action.frame_start + 1)
    for layer in action.layers:
        for strip in layer.strips:
            for source_bag in strip.channelbags:
                for curve in source_bag.fcurves:
                    key = (curve.data_path, curve.array_index)
                    if key not in curves:
                        curves[key] = bag.fcurves.new(data_path=key[0], index=key[1])
                    for point in curve.keyframe_points:
                        point = curves[key].keyframe_points.insert(
                            point.co.x - action.frame_start + start, point.co.y, options={'FAST'})
                        point.interpolation = 'LINEAR'
    clips.append({'name': name, 'first': start, 'last': start + count - 1,
                  'loop': not name.startswith('jump_')})
    start += count
for curve in curves.values():
    curve.update()
rig.animation_data_create()
rig.animation_data.action = sequence
rig.animation_data.action_slot = slot
scene = bpy.context.scene
scene.render.fps = 30
scene.render.fps_base = 1
scene.frame_start, scene.frame_end = 1, start - 1
scene.frame_set(1)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
rig.hide_set(False)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(output / 'RifleLocomotion.fbx'), use_selection=True,
    object_types={'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
    use_armature_deform_only=False, bake_anim=True,
    bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
    bake_anim_simplify_factor=0.0, path_mode='STRIP', axis_forward='-Z', axis_up='Y')
(output / 'locomotion_manifest.json').write_text(json.dumps({'clips': clips}, indent=2), encoding='utf-8')
print('TRAINING_LOCOMOTION_EXPORT_PASS', len(clips), 'clips', flush=True)
