import json
import shutil
import subprocess
from pathlib import Path
from urllib.parse import unquote

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'CombatFX'
CLI='D:/MyProject/BlenderTools/Source2Viewer-20.0/Source2Viewer-CLI.exe'
GAME=Path('D:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo')
BLENDER='D:/SteamLibrary/steamapps/common/Blender/blender.exe'
batch={e['source']:e for e in json.loads((ROOT/'Batch/plan.json').read_text())['models']}
resources=json.loads((OUT/'manifest.json').read_text(encoding='utf-8'))['resources']
plan=[]
for source, resource in resources.items():
    if not source.endswith('.vmdl_c'): continue
    name=Path(source).name.removesuffix('.vmdl_c')
    category={'AmmoModels':'Ammo','GrenadeModels':'Grenades'}.get(resource['rootCategory'],'EffectMeshes')
    destination=OUT/'Models'/category/name
    destination.mkdir(parents=True,exist_ok=True)
    entry={'id':name,'source':source,'category':category,'folder':str(destination.relative_to(OUT))}
    if source in batch:
        previous=batch[source]
        previous_folder=ROOT/'Batch'/previous['relative_dir']
        report=json.loads((previous_folder/'report.json').read_text())
        assert report['status']=='complete' and report['fbx_validation']['passed'],source
        for ext in ['.blend','.fbx']: shutil.copy2(previous_folder/(previous['id']+ext),destination/(name+ext))
        gltf=ROOT/'Batch'/previous['gltf']
        doc=json.loads(gltf.read_text())
        shutil.copy2(gltf,destination/(name+'.gltf'))
        for kind in ['images','buffers']:
            for item in doc.get(kind,[]):
                uri=unquote(item['uri'])
                if uri.startswith('data:'): continue
                target=destination/uri;target.parent.mkdir(parents=True,exist_ok=True)
                shutil.copy2(gltf.parent/uri,target)
        report['reusedFrom']=str(previous_folder)
        report['blend']=str(destination/(name+'.blend'));report['fbx']=str(destination/(name+'.fbx'))
        (destination/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
        entry['reused']=True
    else:
        gltf=destination/(name+'.gltf')
        with (OUT/'Logs'/('model_'+name+'.log')).open('w',encoding='utf-8') as log:
            result=subprocess.run([CLI,'-i',str(GAME/'pak01_dir.vpk'),'-f',source,'-o',str(gltf),'-d',
                '--gltf_export_format','gltf','--gltf_export_materials','--gltf_textures_adapt',
                '--gltf_export_animations','--gltf_animation_list','__rig_only__','--game',str(GAME/'gameinfo.gi')],
                stdout=log,stderr=subprocess.STDOUT)
            entry['exportExitCode']=result.returncode
        if not gltf.exists(): entry['error']='Exporter produced no glTF; raw compiled resource is preserved.'
    plan.append(entry)
    print('MODEL_PREPARED '+name,flush=True)
(OUT/'model_plan.json').write_text(json.dumps(plan,indent=2),encoding='utf-8')
with (OUT/'Logs/models_blender.log').open('w',encoding='utf-8') as log:
    result=subprocess.run([BLENDER,'--background','--factory-startup','--python-exit-code','1',
        '--python',str(ROOT/'Scripts/build_combat_models.py')],stdout=log,stderr=subprocess.STDOUT)
print('COMBAT_MODELS_FINISHED '+str(result.returncode),flush=True)
