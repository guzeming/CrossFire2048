import bpy, json
from pathlib import Path
root = Path(__file__).resolve().parent.parent / 'CT_SAS'
bpy.ops.wm.open_mainfile(filepath=str(root / 'ctm_sas_material_preview.blend'))
target = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
old = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(root / 'Animations/Clips/idle_rifle.gltf'))
source = next(o for o in bpy.data.objects if o not in old and o.type == 'ARMATURE')
common = set(target.data.bones.keys()) & set(source.data.bones.keys())
result = {'target': target.name, 'source': source.name, 'common': len(common),
          'source_only': sorted(set(source.data.bones.keys())-common),
          'target_only': sorted(set(target.data.bones.keys())-common),
          'world_target': list(map(list,target.matrix_world)), 'world_source': list(map(list,source.matrix_world)), 'bones': {}}
for name in sorted(common):
    a,b=target.data.bones[name],source.data.bones[name]
    ma,mb=target.matrix_world @ a.matrix_local,source.matrix_world @ b.matrix_local
    result['bones'][name] = {'target_parent': a.parent.name if a.parent else None, 'source_parent': b.parent.name if b.parent else None,
                            'head_distance':(ma.translation-mb.translation).length,
                            'rotation_difference':ma.to_quaternion().rotation_difference(mb.to_quaternion()).angle,
                            'target_matrix':list(map(list,ma)), 'source_matrix':list(map(list,mb))}
(root/'Animations/rig_comparison.json').write_text(json.dumps(result,indent=2))
print(json.dumps({k:v for k,v in result.items() if k!='bones'}),flush=True)
print('MAX HEAD',max(b['head_distance'] for b in result['bones'].values()))
print('ACTION',[(a.name,list(a.frame_range)) for a in bpy.data.actions])
