"""Bake existing third-person primary weapon clips to the shared training rig.
Blender --background --python this.py -- --output <Assets/Art/Training>
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
root = Path(__file__).resolve().parents[1]
raw = root / 'Batch/RawAnimations/animation/anims/world'
output = Path(args.output).resolve()
output.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(str(root / 'CT_SAS/ctm_sas_animated.blend'), link=False) as (src, dst):
    dst.objects = ['CT_SAS_Rig']
rig = dst.objects[0]
bpy.context.scene.collection.objects.link(rig)
rig.name = 'LobbyRig'
rig.animation_data_clear()
rig.matrix_world = Matrix.Identity(4)
for bone in rig.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
    bone.rotation_mode = 'QUATERNION'

jobs = [('idle_primary_reference', raw / 'rifle/_default_rifle/idle_rifle.gltf', True, False)]
for profile in ('aug', 'famas', 'galilar', 'sg556', 'bizon', 'mac10', 'mp5sd', 'mp7', 'mp9', 'p90', 'ump45', 'mag7', 'nova', 'sawedoff', 'xm1014', 'm249', 'negev'):
    directory = raw / 'rifle' / f'rifle_{profile}'
    for kind, filename in [('idle', f'idle_{profile}.vnmclip+non_additive'),
                           ('fire', f'shoot_{profile}.vnmclip+non_additive'), ('reload', f'reload_{profile}')]:
        jobs.append((f'{kind}_{profile}', directory / f'{filename}.gltf', kind == 'idle', kind != 'idle'))

actions = []
for name, path, loop, additive in jobs:
    assert path.exists(), path
    previous = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(path))
    imported = set(bpy.data.objects) - previous
    source = max((o for o in imported if o.type == 'ARMATURE'),
                 key=lambda o: len(set(o.data.bones.keys()) & set(rig.data.bones.keys())))
    assert len(set(source.data.bones.keys()) & set(rig.data.bones.keys())) == 64
    action = bake_action(rig, source, name, *source.animation_data.action.frame_range)
    actions.append((action, path.relative_to(root).as_posix(), loop, additive))
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    print('BAKED', name, flush=True)

parent = bpy.data.objects.new('LoadoutModel', None)
bpy.context.scene.collection.objects.link(parent)
rig.parent = parent
sequence = bpy.data.actions.new('TrainingPrimaryActions')
slot = sequence.slots.new('OBJECT', rig.name)
bag = sequence.layers.new('Primary actions').strips.new(type='KEYFRAME').channelbag(slot, ensure=True)
curves, clips = {}, []
start = 1
for action, path, loop, additive in actions:
    count = int(action.frame_end - action.frame_start + 1)
    for layer in action.layers:
        for strip in layer.strips:
            for source_bag in strip.channelbags:
                for curve in source_bag.fcurves:
                    key = (curve.data_path, curve.array_index)
                    if key not in curves:
                        curves[key] = bag.fcurves.new(data_path=key[0], index=key[1])
                    for point in curve.keyframe_points:
                        target = curves[key].keyframe_points.insert(point.co.x - action.frame_start + start, point.co.y, options={'FAST'})
                        target.interpolation = 'LINEAR'
    clips.append({'name': action.name, 'first': start, 'last': start + count - 1,
                  'loop': loop, 'additive': additive, 'source': path})
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
parent.select_set(True)
rig.hide_set(False)
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(output / 'PrimaryWeaponActions.fbx'), use_selection=True,
    object_types={'ARMATURE', 'EMPTY'}, add_leaf_bones=False, use_armature_deform_only=False,
    bake_anim=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
    bake_anim_simplify_factor=0.0, path_mode='STRIP', axis_forward='-Z', axis_up='Y')
(output / 'primary_actions_manifest.json').write_text(json.dumps({'clips': clips}, indent=2), encoding='utf-8')
print('PRIMARY_ACTION_EXPORT_PASS', len(clips), flush=True)
