"""Build small animation-only libraries, preserving each source skeleton and slot."""
import argparse,array,hashlib,json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from batch_common import *

p=argparse.ArgumentParser()
p.add_argument('--job',required=True)
args=p.parse_args(sys.argv[sys.argv.index('--')+1:])
jobs=json.loads((BATCH/'animation_jobs.json').read_text())
job=next(j for j in jobs if j['id']==args.job)
folder=BATCH/'Animations'/job['id']
folder.mkdir(parents=True,exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene=bpy.context.scene
scene.render.fps=30
masters={}
records=[]
rig_records={}

for clip in job['clips']:
    before=set(bpy.data.objects)
    old_actions=set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=str(BATCH/clip['gltf']))
    imported=set(bpy.data.objects)-before
    original_actions=set(bpy.data.actions)-old_actions
    retained=set()
    for source in sorted([o for o in imported if o.type=='ARMATURE'],key=lambda o:o.name):
        signature=hashlib.sha256(json.dumps([(b.name,b.parent.name if b.parent else None,[[round(v,5) for v in row] for row in b.matrix_local]) for b in source.data.bones]).encode()).hexdigest()
        original_name=source.name
        if signature not in masters:
            masters[signature]=source
            source.name=Path(original_name.replace('\\','/')).name.split('.')[0]+'_Rig'
            source.data.display_type='STICK'
            source.show_in_front=True
            for b in source.pose.bones: b.custom_shape=None
            retained.add(source)
            rig_records[source.name]={'source_skeleton':original_name,'bones':len(source.data.bones),'actions':[]}
        master=masters[signature]
        if not source.animation_data or not source.animation_data.action:
            continue
        original=source.animation_data.action
        source_slot=source.animation_data.action_slot
        label=clip['name'].replace('.vnmclip+non_additive','_full_pose')+'@'+master.name.removesuffix('_Rig')
        action=bpy.data.actions.new(label)
        slot=action.slots.new('OBJECT',master.name)
        bag=action.layers.new('Source clip').strips.new(type='KEYFRAME').channelbag(slot,ensure=True)
        source_curves=[]
        for layer in original.layers:
            for strip in layer.strips:
                source_bag=strip.channelbag(source_slot)
                if source_bag: source_curves.extend(source_bag.fcurves)
        assert source_curves,(clip['source'],original_name)
        for fc in source_curves:
            copied=bag.fcurves.new(data_path=fc.data_path,index=fc.array_index)
            count=len(fc.keyframe_points)
            copied.keyframe_points.add(count)
            coords=array.array('f',[0])*(count*2)
            fc.keyframe_points.foreach_get('co',coords)
            for i in range(0,len(coords),2): coords[i]+=1
            copied.keyframe_points.foreach_set('co',coords)
            for key in copied.keyframe_points: key.interpolation='LINEAR'
            copied.update()
        action.use_fake_user=True
        action.use_frame_range=True
        action.frame_start=1
        action.frame_end=max(2,original.frame_range[1]+1)
        action['source_resource']=clip['source']
        action.asset_mark()
        assign(master,action)
        rig_records[master.name]['actions'].append(action.name)
        records.append({'source':clip['source'],'rig':master.name,'action':action.name,'frames':int(action.frame_end),'curves':len(source_curves)})
    for obj in imported-retained: bpy.data.objects.remove(obj,do_unlink=True)
    for action in original_actions: bpy.data.actions.remove(action)

scene.frame_start=1
scene.frame_set(1)
selected=max(masters.values(),key=lambda o:len(o.data.bones))
bpy.ops.object.select_all(action='DESELECT')
selected.select_set(True)
bpy.context.view_layer.objects.active=selected
scene.frame_end=int(selected.animation_data.action.frame_end)
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='DOPESHEET_EDITOR': area.spaces.active.mode='ACTION'
        elif area.type=='VIEW_3D':
            area.spaces.active.region_3d.view_location=(0,0,1)
            area.spaces.active.region_3d.view_distance=3
scene['library_note']='Animation-only library using original Source 2 skeletons. Retarget to a character/weapon with Scripts/apply_shared_animation.py. Weapon components and character slots are kept separately.'
blend=folder/'animations.blend'
bpy.context.preferences.filepaths.save_version=0
bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
fbxs=[]
for rig_name,record in rig_records.items():
    bpy.ops.wm.open_mainfile(filepath=str(blend))
    for action in list(bpy.data.actions):
        if action.name not in record['actions']: bpy.data.actions.remove(action)
    rig=bpy.data.objects[rig_name]
    export_fbx(folder/(rig_name+'.fbx'),[rig])
    fbxs.append({'rig':rig_name,'file':rig_name+'.fbx','actions':len(record['actions'])})
(folder/'report.json').write_text(json.dumps({'id':job['id'],'status':'complete','source_clips':len(job['clips']),'action_slots':len(records),'rigs':rig_records,'clips':records,'fbx':fbxs},indent=2),encoding='utf-8')
print('LIBRARY_COMPLETE '+job['id'],flush=True)
