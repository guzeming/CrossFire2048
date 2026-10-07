"""Retarget exported CS2 world clips to the existing SAS rig, preserving its mesh bind pose."""
import argparse
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix

parser = argparse.ArgumentParser()
parser.add_argument('--test', action='store_true')
parser.add_argument('--render', action='store_true')
parser.add_argument('--fbx', action='store_true')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
folder = Path(__file__).resolve().parent.parent / 'CT_SAS'
animation_folder = folder / 'Animations'
manifest = json.loads((animation_folder / 'manifest.json').read_text(encoding='utf-8-sig'))
if args.test:
    manifest = [c for c in manifest if c['name'] in {'idle_rifle', 'run_n_rifle', 'crouch_n_rifle'}]
bpy.ops.wm.open_mainfile(filepath=str(folder / 'ctm_sas_material_preview.blend'))
scene = bpy.context.scene
scene.render.fps = 30
scene.render.fps_base = 1
target = next(o for o in scene.objects if o.type == 'ARMATURE')
target.name = 'CT_SAS_Rig'
target.animation_data_create()
target.animation_data_clear()
target.animation_data_create()
for bone in target.pose.bones:
    bone.rotation_mode = 'QUATERNION'
    bone.matrix_basis = Matrix.Identity(4)
target_rest = {b.name: b.matrix_local.copy() for b in target.data.bones}
target_world = target.matrix_world.copy()
target_world_inv = target_world.inverted()
records = []
actions = {}

for clip in manifest:
    old_objects = set(bpy.data.objects)
    old_actions = set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=str(animation_folder / clip['gltf']))
    imported = set(bpy.data.objects) - old_objects
    # Weapon clips may also contain a synchronized weapon rig. Choose the
    # character skeleton by shared bone names, never by import order.
    source = max((o for o in imported if o.type == 'ARMATURE'), key=lambda o: len(set(o.data.bones.keys()) & set(target.data.bones.keys())))
    source_actions = set(bpy.data.actions) - old_actions
    common = [b.name for b in target.data.bones if b.name in source.data.bones]
    assert len(common) == 64, (clip['name'], len(common))
    corrections = {n: (source.matrix_world @ source.data.bones[n].matrix_local).inverted() @ target_world @ target_rest[n] for n in common}
    differences = {n: ((source.matrix_world @ source.data.bones[n].matrix_local).translation - (target_world @ target_rest[n]).translation).length for n in common}
    assert max(d for n, d in differences.items() if n != 'wpn') < 0.001
    count = max(1, round(clip['duration_seconds'] * scene.render.fps) + 1)
    values = {n: {'location': [], 'rotation_quaternion': [], 'scale': []} for n in common}
    previous_rotations = {}
    root_positions = []
    for i in range(count):
        scene.frame_set(i)
        bpy.context.view_layer.update()
        desired = {}
        for bone in target.data.bones:
            name = bone.name
            if name in corrections:
                desired[name] = target_world_inv @ source.matrix_world @ source.pose.bones[name].matrix @ corrections[name]
            elif bone.parent:
                desired[name] = desired[bone.parent.name] @ target_rest[bone.parent.name].inverted() @ target_rest[name]
            else:
                desired[name] = target_rest[name]
            if name not in corrections:
                continue
            kwargs = {}
            if bone.parent:
                kwargs = {'parent_matrix': desired[bone.parent.name], 'parent_matrix_local': target_rest[bone.parent.name]}
            basis = bone.convert_local_to_pose(desired[name], target_rest[name], invert=True, **kwargs)
            location, rotation, scale = basis.decompose()
            if name in previous_rotations and previous_rotations[name].dot(rotation) < 0:
                rotation.negate()
            previous_rotations[name] = rotation.copy()
            for key, components in [('location', location), ('rotation_quaternion', rotation), ('scale', scale)]:
                assert all(math.isfinite(v) for v in components)
                values[name][key].append(tuple(components))
        root_positions.append(tuple(desired['root_motion'].translation))
    action = bpy.data.actions.new(clip['name'])
    slot = action.slots.new('OBJECT', target.name)
    layer = action.layers.new('Baked CS2 animation')
    strip = layer.strips.new(type='KEYFRAME')
    bag = strip.channelbag(slot, ensure=True)
    for name, channels in values.items():
        for path, samples in channels.items():
            for component in range(len(samples[0])):
                curve = bag.fcurves.new(data_path=f'pose.bones["{name}"].{path}', index=component)
                curve.keyframe_points.add(count)
                curve.keyframe_points.foreach_set('co', [v for i, sample in enumerate(samples) for v in (i + 1, sample[component])])
                for key in curve.keyframe_points:
                    key.interpolation = 'LINEAR'
                curve.update()
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start = 1
    action.frame_end = max(2, count)
    action['source_resource'] = clip['source']
    action['original_duration_seconds'] = clip['duration_seconds']
    action['is_static_pose'] = clip['is_pose']
    action.asset_mark()
    action.asset_data.description = f"CS2 SAS / {clip['name']} / {clip['duration_seconds']:.3f}s / 30 fps. Baked to the 94-bone SAS rig."
    action.asset_data.tags.new('CS2')
    action.asset_data.tags.new('CT_SAS')
    actions[action.name] = action
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    for source_action in source_actions:
        bpy.data.actions.remove(source_action)
    records.append(dict(clip, frames=count, baked_bones=len(common), action=action.name,
                        root_displacement=[root_positions[-1][i]-root_positions[0][i] for i in range(3)]))
    print(f"BAKED {len(records)}/{len(manifest)} {action.name}: {count} frames", flush=True)

# All actions target the original rig. Additional twist/accessory bones retain
# their bind offsets and inherit parent motion; game procedural jiggle/IK is absent.
target.animation_data.action = actions['run_n_rifle']
target.animation_data.action_slot = actions['run_n_rifle'].slots[0]
scene.frame_start = 1
scene.frame_end = int(actions['run_n_rifle'].frame_end)
scene.frame_set(6)
bpy.ops.object.select_all(action='DESELECT')
target.hide_set(False)
target.select_set(True)
bpy.context.view_layer.objects.active = target
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type == 'DOPESHEET_EDITOR' and area.spaces.active.mode == 'TIMELINE':
            area.spaces.active.mode = 'ACTION'
        if area.type == 'VIEW_3D':
            area.spaces.active.overlay.show_overlays = False
scene['animation_count'] = len(actions)
scene['animation_help'] = 'Space: play run_n_rifle. Action Editor dropdown: choose another clip. Set timeline range to its frame range. Assets also listed in the Asset Browser.'
scene['animation_limitations'] = 'Original animation clips retargeted by rest-pose world-space delta; no Source 2 runtime IK, jiggle, aim blending, weapon mesh, or gameplay graph.'
bpy.context.preferences.filepaths.save_version = 0
output = folder / ('ctm_sas_animation_test.blend' if args.test else 'ctm_sas_animated.blend')
bpy.ops.wm.save_as_mainfile(filepath=str(output), compress=True)
report = {'blend':str(output), 'fps':30, 'actions':records, 'bones':len(target.data.bones),
          'retarget_method':'source_pose_world * inverse(source_rest_world) * target_rest_world',
          'unanimated_helper_bones': [b.name for b in target.data.bones if b.name not in common],
          'animation_graph_exported':False}
(animation_folder / ('test_report.json' if args.test else 'animation_report.json')).write_text(json.dumps(report,indent=2),encoding='utf-8')

if args.fbx:
    bpy.ops.object.select_all(action='DESELECT')
    target.select_set(True)
    for obj in scene.objects:
        if obj.type == 'MESH' and any(m.type == 'ARMATURE' and m.object == target for m in obj.modifiers) and not any(c.name == 'FirstPerson_Alternatives' for c in obj.users_collection):
            obj.hide_set(False)
            obj.select_set(True)
    bpy.context.view_layer.objects.active = target
    bpy.ops.export_scene.fbx(filepath=str(animation_folder / 'ctm_sas_animated.fbx'), use_selection=True,
        object_types={'ARMATURE','MESH'}, add_leaf_bones=False, use_armature_deform_only=False,
        bake_anim=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0,
        path_mode='COPY', embed_textures=True, axis_forward='-Z', axis_up='Y')
    print('FBX_EXPORTED', flush=True)
if args.render:
    scene.render.resolution_x = 768
    scene.render.resolution_y = 768
    scene.render.resolution_percentage = 100
    scene.render.filepath = str(animation_folder / 'run_preview.png')
    bpy.ops.render.render(write_still=True)
print('ANIMATED_BLEND_SAVED '+str(output),flush=True)
