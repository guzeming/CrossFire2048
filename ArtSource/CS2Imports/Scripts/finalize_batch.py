import csv,json
from collections import Counter,defaultdict
from pathlib import Path
root=Path(__file__).resolve().parent.parent
batch=root/'Batch'
plan=json.loads((batch/'plan.json').read_text())
models=[]
for entry in plan['models']:
    path=batch/entry['relative_dir']/'report.json'
    report=json.loads(path.read_text())
    assert report['status']=='complete',entry['id']
    assert report.get('fbx_validation',{}).get('passed'),(entry['id'],'missing FBX check')
    assert report['all_images_packed'],entry['id']
    assert Path(report['blend']).is_file() and Path(report['fbx']).is_file()
    models.append(report)
assert len(models)==125
libraries=[]
source_map=defaultdict(list)
for job in json.loads((batch/'animation_jobs.json').read_text()):
    folder=batch/'Animations'/job['id']
    report=json.loads((folder/'report.json').read_text())
    assert report['status']=='complete'
    assert (folder/'animations.blend').is_file()
    for fbx in report['fbx']: assert (folder/fbx['file']).is_file()
    libraries.append(report)
    for clip in report['clips']: source_map[clip['source']].append({'library':job['id'],'rig':clip['rig'],'action':clip['action'],'frames':clip['frames']})
expected={c['source'] for c in plan['animations']}
assert expected==set(source_map),(len(expected-set(source_map)),len(set(source_map)-expected))
assert len(expected)==2141
audio=json.loads((batch/'Audio/validation.json').read_text())
assert not audio['failures'] and audio['missing_event_resources']==0
assert audio['count']==1237
with (batch/'catalog.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.writer(f)
    writer.writerow(['类别','资源名称','骨骼数','骨骼动作数','原始网格数','三角面总数（含隐藏备选）','Blender文件','FBX文件','源资源','FBX回读验证'])
    for r in models:
        writer.writerow([r['category'],r['id'],r['bones'],len(r['actions']),len(r['meshes']),sum(m['triangles'] for m in r['meshes']),
                         Path(r['blend']).relative_to(batch).as_posix(),Path(r['fbx']).relative_to(batch).as_posix(),r['source'],'通过'])
with (batch/'animations.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.writer(f)
    writer.writerow(['源动画资源','分类','源glTF路径','共享动作库目录','骨架','动作名','帧数'])
    for clip in plan['animations']:
        assert (batch/clip['gltf']).is_file()
        for slot in source_map[clip['source']]:
            writer.writerow([clip['source'],clip['group'],clip['gltf'],'Animations/'+slot['library'],slot['rig'],slot['action'],slot['frames']])
summary={'characters':sum(m['category']=='Characters' for m in models),'ct_characters':sum(m['id'].startswith('ctm_') for m in models),
         't_characters':sum(m['id'].startswith('tm_') for m in models),'weapons_equipment':sum(m['category']=='Weapons' for m in models),
         'parts':sum(m['category']=='WeaponParts' for m in models),'models':len(models),'model_fbx_roundtrips_passed':len(models),
         'animations_source_resources':len(expected),'animation_libraries':len(libraries),
         'animation_slots':sum(r['action_slots'] for r in libraries),'animation_fbx_files':sum(len(r['fbx']) for r in libraries),
         'animation_categories':dict(Counter(c['group'].split('/')[0] for c in plan['animations'])),
         'weapon_sounds':audio['count'],'audio_event_count':audio['event_count'],'audio_missing_dependencies':0,
         'all_model_images_packed':True,'unity_editor_import_tested':False}
(batch/'summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
(batch/'catalog.json').write_text(json.dumps({'summary':summary,'models':models,'animation_libraries':libraries},indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2),flush=True)
