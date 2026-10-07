"""Bake exported CS grenade actions onto the shared character rig, without changing the sources.
Blender --background --python this.py -- --output Assets/Art/Throwables/Animations
"""
import argparse
import json
import sys
from pathlib import Path
import bpy
from mathutils import Matrix

sys.path.insert(0, str(Path(__file__).resolve().parent))
from batch_common import bake_action

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
source_root = Path(__file__).resolve().parents[1]
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(str(source_root / 'CT_SAS/ctm_sas_animated.blend'), link=False) as (src, dst):
    dst.objects = ['CT_SAS_Rig']
rig = dst.objects[0]
bpy.context.scene.collection.objects.link(rig)
rig.name = 'LobbyRig'
rig.animation_data_clear()
rig.matrix_world = Matrix.Identity(4)
for bone in rig.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
    bone.rotation_mode = 'QUATERNION'

actions = []
for name, folder, filename in [
    ('draw_grenade', '_default_grenade', 'draw_grenade'),
    ('idle_grenade', '_default_grenade', 'idle_grenade.vnmclip+non_additive'),
    ('prepare_grenade', '_default_grenade', 'pullpin_grenade'),
    ('throw_grenade', '_default_grenade', 'throw_overhand_grenade'),
    ('draw_molotov', 'grenade_molotov', 'draw_molotov'),
    ('idle_molotov', 'grenade_molotov', 'idle_molotov.vnmclip+non_additive'),
    ('prepare_molotov', 'grenade_molotov', 'pullpin_molotov'),
]:
    path = source_root / 'Batch/RawAnimations/animation/anims/world/grenade' / folder / (filename + '.gltf')
    previous = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    imported = set(bpy.data.objects) - previous
    source = max((o for o in imported if o.type == 'ARMATURE'),
                 key=lambda o: len(set(o.data.bones.keys()) & set(rig.data.bones.keys())))
    assert len(set(source.data.bones.keys()) & set(rig.data.bones.keys())) == 64
    action = bake_action(rig, source, name, *source.animation_data.action.frame_range)
    actions.append((action, path.relative_to(source_root).as_posix()))
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)

root = bpy.data.objects.new('LoadoutModel', None)
bpy.context.scene.collection.objects.link(root)
rig.parent = root
sequence = bpy.data.actions.new('TrainingThrowableActions')
slot = sequence.slots.new('OBJECT', rig.name)
bag = sequence.layers.new('Throwable actions').strips.new(type='KEYFRAME').channelbag(slot, ensure=True)
curves, clips = {}, []
start = 1
for action, path in actions:
    count = int(action.frame_end - action.frame_start + 1)
    for layer in action.layers:
        for strip in layer.strips:
            for source_bag in strip.channelbags:
                for curve in source_bag.fcurves:
                    key = (curve.data_path, curve.array_index)
                    if key not in curves:
                        curves[key] = bag.fcurves.new(data_path=key[0], index=key[1])
                    for point in curve.keyframe_points:
                        target = curves[key].keyframe_points.insert(point.co.x - action.frame_start + start,
                                                                    point.co.y, options={'FAST'})
                        target.interpolation = 'LINEAR'
    clips.append({'name': action.name, 'first': start, 'last': start + count - 1, 'source': path})
    start += count
for curve in curves.values():
    curve.update()
rig.animation_data_create()
rig.animation_data.action = sequence
rig.animation_data.action_slot = slot
scene = bpy.context.scene
scene.render.fps, scene.render.fps_base = 30, 1
scene.frame_start, scene.frame_end = 1, start - 1
scene.frame_set(1)
bpy.context.view_layer.update()
bpy.ops.object.select_all(action='DESELECT')
root.select_set(True)
rig.hide_set(False)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(output / 'ThrowableActions.fbx'), use_selection=True,
    object_types={'ARMATURE', 'EMPTY'}, add_leaf_bones=False, use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
    bake_anim_simplify_factor=0.0, path_mode='STRIP', axis_forward='-Z', axis_up='Y')
(output / 'throwable_actions_manifest.json').write_text(json.dumps({'clips': clips}, indent=2), encoding='utf-8')
print('THROWABLE_ACTION_EXPORT_PASS', clips, flush=True)
