import bpy,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from batch_common import BATCH,check_motion
path=BATCH/'Checks/tm_phoenix_reload_ak.blend'
bpy.ops.wm.open_mainfile(filepath=str(path))
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
body=next(o for o in bpy.context.scene.objects if o.type=='MESH' and o.name.endswith('thirdperson_body'))
result=check_motion(rig,body,rig.animation_data.action)
assert result['changed_vertices']>50 and result['max_motion_m']>0.01
(BATCH/'Checks/shared_retarget_validation.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result),flush=True)
