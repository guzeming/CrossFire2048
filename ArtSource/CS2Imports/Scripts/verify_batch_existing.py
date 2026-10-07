"""Backfill FBX round-trip checks for files created before batch verification was enabled."""
import bpy,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from batch_common import skeletal_actions
root=Path(__file__).resolve().parent.parent/'Batch'
for path in sorted((root/'Characters').glob('*/report.json'))+sorted((root/'Weapons').glob('*/report.json'))+sorted((root/'WeaponParts').glob('*/report.json')):
    report=json.loads(path.read_text())
    if report.get('fbx_validation',{}).get('passed'): continue
    if report['status']!='complete': continue
    bpy.ops.wm.open_mainfile(filepath=report['blend'])
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and any(m.type=='ARMATURE' for m in o.modifiers) and not any(c.hide_render for c in o.users_collection)]
    # Some equipment and detached magazines are static meshes.
    if not meshes:
        shapes={b.custom_shape for o in bpy.context.scene.objects if o.type=='ARMATURE' for b in o.pose.bones if b.custom_shape}
        meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o not in shapes and not any(c.hide_render for c in o.users_collection)]
    expected_meshes=len(meshes)
    assert all(i.packed_file for i in bpy.data.images if i.source=='FILE')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=report['fbx'])
    actual_meshes=sum(o.type=='MESH' for o in bpy.context.scene.objects)
    actual_bones=sum(len(o.data.bones) for o in bpy.context.scene.objects if o.type=='ARMATURE')
    assert actual_meshes==expected_meshes,(report['id'],'meshes',actual_meshes,expected_meshes)
    assert actual_bones==report['bones'],(report['id'],'bones',actual_bones,report['bones'])
    assert len(skeletal_actions())==len(report['actions']),(report['id'],'actions',len(skeletal_actions()),len(report['actions']))
    report['fbx_validation']={'meshes':actual_meshes,'bones':actual_bones,'actions':len(skeletal_actions()),'additional_shape_or_object_actions':len(bpy.data.actions)-len(skeletal_actions()),'passed':True}
    report['blend_reopen_verified']=True
    path.write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('VERIFIED '+report['id'],flush=True)
