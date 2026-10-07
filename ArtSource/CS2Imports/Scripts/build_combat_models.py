import json
import sys
from pathlib import Path
import bpy

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'CombatFX'
sys.path.insert(0,str(ROOT/'Scripts'))
from batch_common import studio, export_fbx

for entry in json.loads((OUT/'model_plan.json').read_text()):
    if entry.get('reused') or entry.get('error'): continue
    folder=OUT/entry['folder'];name=entry['id']
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.unit_settings.system='METRIC'
    bpy.ops.import_scene.gltf(filepath=str(folder/(name+'.gltf')))
    rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
    shapes={b.custom_shape for r in rigs for b in r.pose.bones if b.custom_shape}
    meshes=[o for o in bpy.context.scene.objects if o.type=='MESH' and o not in shapes]
    report={**entry,'meshCount':len(meshes),'triangles':sum(len(p.vertices)-2 for o in meshes for p in o.data.polygons),
        'bones':sum(len(r.data.bones) for r in rigs)}
    if not meshes and not rigs:
        report['status']='source_has_no_mesh_or_rig'
        (folder/'report.json').write_text(json.dumps(report,indent=2));continue
    if meshes: studio(meshes)
    bpy.ops.file.pack_all()
    report['textures']=[{'name':i.name,'size':list(i.size)} for i in bpy.data.images if i.source=='FILE']
    assert all(i.size[0]>0 for i in bpy.data.images if i.source=='FILE'),name
    bpy.context.preferences.filepaths.save_version=0
    bpy.ops.wm.save_as_mainfile(filepath=str(folder/(name+'.blend')),compress=True)
    export_fbx(folder/(name+'.fbx'),meshes+rigs,animations=False)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(folder/(name+'.fbx')))
    actual_meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
    actual_bones=sum(len(o.data.bones) for o in bpy.context.scene.objects if o.type=='ARMATURE')
    assert len(actual_meshes)==report['meshCount'] and actual_bones==report['bones'],name
    report.update(status='complete',fbx_validation={'passed':True,'meshes':len(actual_meshes),'bones':actual_bones})
    (folder/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print('COMBAT_MODEL_VERIFIED '+name,flush=True)
