import bpy
import json
from pathlib import Path

root = Path(__file__).resolve().parent.parent
folder = root/'M4A1_S'

def positions(obj):
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(bpy.context.evaluated_depsgraph_get())
    mesh = evaluated.to_mesh()
    result = [evaluated.matrix_world @ v.co for v in mesh.vertices]
    evaluated.to_mesh_clear()
    return result

def motion(rig, mesh, action):
    rig.animation_data_create()
    rig.animation_data.action = action
    rig.animation_data.action_slot = action.slots[0]
    bpy.context.scene.frame_set(1)
    base = positions(mesh)
    maximum, moved = 0,0
    for fraction in [0.25,0.5,0.75,1]:
        bpy.context.scene.frame_set(round(1+(action.frame_range[1]-1)*fraction))
        current = positions(mesh)
        distances = [(a-b).length for a,b in zip(base,current)]
        maximum = max(maximum,max(distances))
        moved = max(moved,sum(d>0.00001 for d in distances))
    return {'action':action.name,'moved_vertices':moved,'max_displacement_m':maximum}

bpy.ops.wm.open_mainfile(filepath=str(folder/'m4a1_s.blend'))
rig = bpy.data.objects['M4A1S_Rig']
mesh = bpy.data.objects['M4A1S_HD']
assert len(rig.data.bones) == 7
assert len(mesh.data.vertices) == 37010
assert len(bpy.data.actions) == 5
assert all(i.packed_file for i in bpy.data.images if i.source == 'FILE')
assert bpy.data.collections['Legacy_Alternative'].hide_render
checks = [motion(rig,mesh,a) for a in list(bpy.data.actions)]
reload_check = next(c for c in checks if c['action']=='M4A1S_reload_m4a1s')
assert reload_check['moved_vertices']>100 and reload_check['max_displacement_m']>0.1
report = {'blend':{'bones':7,'hd_vertices':37010,'actions':5,'all_images_packed':True,'motion_checks':checks}}
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(folder/'m4a1_s.fbx'))
rig = next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
meshes = [o for o in bpy.context.scene.objects if o.type=='MESH']
assert len(meshes)==1
assert len(rig.data.bones)==7
assert len(bpy.data.actions)==5
action = next(a for a in bpy.data.actions if a.name.endswith('M4A1S_reload_m4a1s'))
fbx_motion = motion(rig,meshes[0],action)
assert fbx_motion['moved_vertices']>100 and fbx_motion['max_displacement_m']>0.1
report['fbx'] = {'mesh_count':1,'bones':7,'actions':5,'reload_check':fbx_motion,'unity_editor_import_tested':False}
bpy.ops.wm.open_mainfile(filepath=str(root/'CT_SAS/ctm_sas_m4a1_s.blend'))
scene = bpy.context.scene
weapon = next(o for o in scene.objects if o.type=='ARMATURE' and 'weapon_offset' in o.data.bones)
character = next(o for o in scene.objects if o.type=='ARMATURE' and 'pelvis' in o.data.bones)
assert character.animation_data.action.name=='reload_m4a1s'
assert weapon.animation_data.action.name=='M4A1S_reload_m4a1s'
assert weapon.parent.constraints[0].target==character
assert all(i.packed_file for i in bpy.data.images if i.source=='FILE')
scene.frame_set(35)
scene.render.resolution_x,scene.render.resolution_y=768,768
scene.render.resolution_percentage=100
scene.render.filepath=str(folder/'ct_m4a1_s_reload_check.png')
bpy.ops.render.render(write_still=True)
report['equipped_scene']={'synchronized_reload':True,'frames':[1,93],'attachment_target':character.name,'attachment_bone':'wpn'}
(folder/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('M4A1S_VALIDATED '+json.dumps(report),flush=True)
