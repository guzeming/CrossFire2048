import bpy,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from batch_common import BATCH,skeletal_actions
samples=['ui_anims__buy_menu__ct__01','viewmodel__rifle__rifle_ak__01','world__pistol__pistol_deagle__01']
results=[]
for name in samples:
    folder=BATCH/'Animations'/name
    report=json.loads((folder/'report.json').read_text())
    for item in report['fbx']:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=str(folder/item['file']))
        rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
        assert len(rigs)==1
        assert len(rigs[0].data.bones)==report['rigs'][item['rig']]['bones']
        assert len(skeletal_actions())==item['actions'],(name,item['rig'],len(skeletal_actions()),item['actions'])
        results.append({'library':name,'rig':item['rig'],'bones':len(rigs[0].data.bones),'actions':len(skeletal_actions()),'passed':True})
(BATCH/'animation_fbx_spot_checks.json').write_text(json.dumps(results,indent=2))
print('ANIMATION_LIBRARY_CHECKS '+json.dumps(results),flush=True)
