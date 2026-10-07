import argparse,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from batch_common import *

parser=argparse.ArgumentParser()
parser.add_argument('--id',required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
plan=json.loads((BATCH/'plan.json').read_text())
entry=next(e for e in plan['models'] if e['id']==args.id)
folder=BATCH/entry['relative_dir']
folder.mkdir(parents=True,exist_ok=True)
character=entry['category']=='Characters'
original=ROOT/entry['existing_blend'] if entry['existing_blend'] else None
if original:
    bpy.ops.wm.open_mainfile(filepath=str(original))
else:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps=30
    bpy.ops.import_scene.gltf(filepath=str(BATCH/entry['gltf']))
scene=bpy.context.scene
scene.render.fps=30
scene.unit_settings.system='METRIC'
rigs=[o for o in scene.objects if o.type=='ARMATURE']
rig=max(rigs,key=lambda o:len(o.data.bones)) if rigs else None
shapes={b.custom_shape for r in rigs for b in r.pose.bones if b.custom_shape}
meshes=[o for o in scene.objects if o.type=='MESH' and o not in shapes]
hidden=bpy.data.collections.new('Alternatives_Hidden')
scene.collection.children.link(hidden)
has_hd=any('body_hd' in o.name for o in meshes)
hidden_names=[]
for obj in meshes:
    if ('firstperson_' in obj.name if character else has_hd and 'body_legacy' in obj.name):
        for c in list(obj.users_collection): c.objects.unlink(obj)
        hidden.objects.link(obj)
        hidden_names.append(obj.name)
hidden.hide_viewport=True
hidden.hide_render=True
visible=[o for o in meshes if o.name not in hidden_names and not any(c.hide_render for c in o.users_collection)]
if rig:
    rig.name=entry['id']+'_Rig'
    rig.show_in_front=True
    rig.data.display_type='STICK'
    for bone in rig.pose.bones: bone.rotation_mode='QUATERNION'
actions=[]
skipped=[]
if character and not original:
    before=set(bpy.data.objects)
    old_actions=set(bpy.data.actions)
    with bpy.data.libraries.load(str(ROOT/'CT_SAS/ctm_sas_animated.blend'),link=False) as (src,dst):
        dst.objects=['CT_SAS_Rig']
        dst.actions=list(src.actions)
    source=dst.objects[0]
    scene.collection.objects.link(source)
    source_actions=set(bpy.data.actions)-old_actions
    for action in sorted(source_actions,key=lambda a:a.name):
        assign(source,action)
        baked=bake_action(rig,source,'CT_'+action.name,*action.frame_range)
        actions.append({'name':baked.name,'source_action':action.name,'frames':int(baked.frame_end),'mapped_bones':baked['mapped_bones']})
    for obj in set(bpy.data.objects)-before: bpy.data.objects.remove(obj,do_unlink=True)
    for action in source_actions: bpy.data.actions.remove(action)
elif not character and not original and entry['category']=='Weapons' and rig:
    bits=entry['source'].split('/')
    key=bits[3] if bits[2] in ['knife','grenade'] else bits[2]
    aliases={'ak47':['ak'],'glock18':['glock','glock18'],'scar20':['scar','scar20'],'usp_silencer':['usp'],'knife_default_ct':['default_ct'],'knife_default_t':['default_t','knife_default_t']}.get(key,[key])
    related=[]
    for clip in plan['animations']:
        group=clip['group']
        tail=group.split('/')[-1]
        if not group.startswith(('world/','viewmodel/')): continue
        if any(tail in [a,'rifle_'+a,'pistol_'+a,'grenade_'+a] for a in aliases): related.append(clip)
        elif key=='knife_default_ct' and group=='viewmodel/knife/_default_knife': related.append(clip)
        elif bits[2]=='grenade' and group=='world/grenade/_default_grenade': related.append(clip)
    for clip in related:
        before=set(bpy.data.objects)
        old_actions=set(bpy.data.actions)
        bpy.ops.import_scene.gltf(filepath=str(BATCH/clip['gltf']))
        imported=set(bpy.data.objects)-before
        candidates=[o for o in imported if o.type=='ARMATURE']
        source=max(candidates,key=lambda o:len(set(o.data.bones.keys())&set(rig.data.bones.keys()))) if candidates else None
        common=set(source.data.bones.keys())&set(rig.data.bones.keys()) if source else set()
        new_actions=set(bpy.data.actions)-old_actions
        compatible=len(common)>=max(1,min(3,len(rig.data.bones))) or {'weapon','weapon_offset'}<=common
        if source and compatible and source.animation_data and source.animation_data.action:
            source_action=source.animation_data.action
            name=('VM_' if clip['group'].startswith('viewmodel') else 'WM_')+clip['name'].replace('.vnmclip+non_additive','_full_pose')
            baked=bake_action(rig,source,name,*source_action.frame_range)
            actions.append({'name':baked.name,'source':clip['source'],'frames':int(baked.frame_end),'mapped_bones':len(common)})
        else: skipped.append({'source':clip['source'],'reason':'No compatible weapon rig; character motion remains in shared library'})
        for obj in imported: bpy.data.objects.remove(obj,do_unlink=True)
        for action in new_actions: bpy.data.actions.remove(action)
elif original:
    actions=[{'name':a.name,'frames':int(a.frame_range[1]),'reused':True} for a in bpy.data.actions]

weights=[]
for obj in meshes:
    arm=[m.object for m in obj.modifiers if m.type=='ARMATURE' and m.object]
    if arm:
        names={b.name for a in arm for b in a.data.bones}
        groups={g.index for g in obj.vertex_groups if g.name in names}
        unweighted=sum(not any(g.group in groups and g.weight>0 for g in v.groups) for v in obj.data.vertices)
        if unweighted: raise ValueError('Unweighted vertices '+obj.name)
    else: unweighted=None
    weights.append({'mesh':obj.name,'vertices':len(obj.data.vertices),'triangles':sum(len(p.vertices)-2 for p in obj.data.polygons),'unweighted':unweighted})
motion=None
if character:
    run=next(a for a in bpy.data.actions if 'run_n_rifle' in a.name)
    body=max(visible,key=lambda o:len(o.data.vertices))
    motion=check_motion(rig,body,run)
    if motion['changed_vertices']<50: raise ValueError('Character failed skinning motion check')
    assign(rig,run)
    scene.frame_start,scene.frame_end=1,int(run.frame_range[1])
    scene.frame_set(6)
elif rig:
    if actions:
        test=next((a for a in bpy.data.actions if 'reload' in a.name),bpy.data.actions[0])
        motion=check_motion(rig,max(visible,key=lambda o:len(o.data.vertices)),test)
    assign(rig,None)
    scene.frame_set(1)
if not original: dimensions=studio(visible,character)
else: dimensions=None
images=[{'name':i.name,'size':list(i.size)} for i in bpy.data.images if i.source=='FILE']
if any(i['size'][0]==0 for i in images): raise ValueError('Missing image pixels')
bpy.ops.file.pack_all()
bpy.context.preferences.filepaths.save_version=0
bpy.ops.object.select_all(action='DESELECT')
selected=rig if rig else visible[0]
selected.select_set(True)
bpy.context.view_layer.objects.active=selected
scene['source_resource']=entry['source']
scene['shared_animation_library']='Batch/Animations and Batch/RawAnimations. See Batch/README.txt.'
blend=folder/(entry['id']+'.blend')
fbx=folder/(entry['id']+'.fbx')
bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
export_fbx(fbx,rigs+visible,animations=bool(actions))
report=dict(entry,blend=str(blend),fbx=str(fbx),meshes=weights,hidden=hidden_names,bones=sum(len(r.data.bones) for r in rigs),
    images=images,actions=actions,skipped_weapon_clips=skipped,motion_check=motion,dimensions=dimensions,all_images_packed=all(i.packed_file for i in bpy.data.images if i.source=='FILE'),status='pending_validation')
(folder/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
if character or entry['id'] in ['weapon_rif_ak47','weapon_snip_awp','weapon_pist_glock18','weapon_c4']:
    scene.render.filepath=str(folder/'preview.png')
    bpy.ops.render.render(write_still=True)
expected_meshes=len(visible)
expected_bones=report['bones']
expected_actions=len(actions)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(fbx))
roundtrip_meshes=[o for o in bpy.context.scene.objects if o.type=='MESH']
roundtrip_rigs=[o for o in bpy.context.scene.objects if o.type=='ARMATURE']
assert len(roundtrip_meshes)==expected_meshes,(entry['id'],'FBX mesh count')
assert sum(len(o.data.bones) for o in roundtrip_rigs)==expected_bones,(entry['id'],'FBX bone count')
assert len(skeletal_actions())==expected_actions,(entry['id'],'FBX skeletal action count',len(skeletal_actions()),expected_actions)
report['fbx_validation']={'meshes':len(roundtrip_meshes),'bones':expected_bones,'actions':len(skeletal_actions()),'additional_shape_or_object_actions':len(bpy.data.actions)-len(skeletal_actions()),'passed':True}
report['status']='complete'
(folder/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('MODEL_COMPLETE '+entry['id'],flush=True)
