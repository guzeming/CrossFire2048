"""Reopen Blender and FBX deliverables and verify real skinned mesh motion."""
import bpy
import json
import math
from pathlib import Path

folder = Path(__file__).resolve().parent.parent / 'CT_SAS'
output = folder / 'Animations'
manifest = json.loads((output / 'manifest.json').read_text(encoding='utf-8-sig'))

def mesh_positions(body):
    bpy.context.view_layer.update()
    evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    positions = [evaluated.matrix_world @ v.co for v in mesh.vertices]
    evaluated.to_mesh_clear()
    assert all(math.isfinite(c) for v in positions for c in v)
    return positions

def check_action(rig, body, action, static):
    rig.animation_data_create()
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]
    start, end = action.frame_range
    bpy.context.scene.frame_set(round(start))
    first = mesh_positions(body)
    max_movement = 0.0
    changed = 0
    for fraction in [0.25, 0.5, 0.75, 1.0]:
        bpy.context.scene.frame_set(round(start + (end-start)*fraction))
        current = mesh_positions(body)
        moves = [(a-b).length for a,b in zip(first,current)]
        max_movement = max(max_movement, max(moves))
        changed = max(changed, sum(m > 0.00001 for m in moves))
        spans = [max(p[i] for p in current)-min(p[i] for p in current) for i in range(3)]
        assert max(spans) < 5, (action.name, 'Exploded mesh', spans)
    if not static:
        assert changed > 10 and max_movement > 0.0001, (action.name, 'No mesh animation', changed, max_movement)
    return {'action':action.name, 'range':[start,end], 'max_vertex_motion_m':max_movement, 'changed_body_vertices':changed}

bpy.ops.wm.open_mainfile(filepath=str(folder / 'ctm_sas_animated.blend'))
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.context.scene.objects if o.name.endswith('thirdperson_body'))
assert len(rig.data.bones) == 94
assert len(bpy.data.actions) == len(manifest) == 53
assert all(i.packed_file for i in bpy.data.images if i.source == 'FILE')
results = [check_action(rig, body, bpy.data.actions[c['name']], c['is_pose']) for c in manifest]
blend_report = {'bones':len(rig.data.bones), 'actions':len(results), 'checks':results}
print('BLEND_VALIDATED '+str(len(results))+' actions',flush=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(output / 'ctm_sas_animated.fbx'))
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
body = next(o for o in bpy.context.scene.objects if o.name.endswith('thirdperson_body'))
assert len(bpy.data.actions) == 53, len(bpy.data.actions)
assert len(rig.data.bones) == 94
assert len([o for o in bpy.context.scene.objects if o.type == 'MESH']) == 3
fbx_checks = []
for name in ['run_n_rifle','crouch_n_rifle','reload_m4a4','shoot_m4a4','death_chest_a']:
    action = next(a for a in bpy.data.actions if a.name.endswith(name))
    fbx_checks.append(check_action(rig, body, action, False))
report = {'blend':blend_report, 'fbx':{'actions':len(bpy.data.actions), 'bones':len(rig.data.bones), 'checks':fbx_checks},
          'static_pose_count':sum(c['is_pose'] for c in manifest), 'unity_editor_import_tested':False}
(output / 'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('FBX_VALIDATED '+str(len(bpy.data.actions))+' actions',flush=True)
