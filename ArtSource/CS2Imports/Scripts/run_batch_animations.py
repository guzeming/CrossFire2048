import json,subprocess,time
from collections import defaultdict
from pathlib import Path
root=Path(__file__).resolve().parent.parent
batch=root/'Batch'
plan=json.loads((batch/'plan.json').read_text())
groups=defaultdict(list)
for clip in plan['animations']: groups[clip['group']].append(clip)
jobs=[]
for group,clips in sorted(groups.items()):
    for start in range(0,len(clips),30):
        jobs.append({'id':group.replace('/','__')+f'__{start//30+1:02d}','clips':clips[start:start+30]})
(batch/'animation_jobs.json').write_text(json.dumps(jobs,indent=2))
logs=batch/'Logs'
logs.mkdir(exist_ok=True)
results=[]
for i,job in enumerate(jobs,1):
    report=batch/'Animations'/job['id']/'report.json'
    (batch/'animation_progress.json').write_text(json.dumps({'phase':'animations','index':i,'total':len(jobs),'current':job['id'],'results':results},indent=2))
    if report.exists() and json.loads(report.read_text()).get('status')=='complete':
        results.append({'id':job['id'],'status':'complete','reused':True})
        continue
    with (logs/('anim_'+job['id']+'.log')).open('w',encoding='utf-8') as log:
        start=time.monotonic()
        result=subprocess.run(['D:/SteamLibrary/steamapps/common/Blender/blender.exe','--background','--python-exit-code','1','--python',str(root/'Scripts/batch_animation_library.py'),'--','--job',job['id']],stdout=log,stderr=subprocess.STDOUT)
    status='complete' if result.returncode==0 and report.exists() else 'failed'
    results.append({'id':job['id'],'status':status,'exit_code':result.returncode,'seconds':round(time.monotonic()-start,1)})
    print(f'{i}/{len(jobs)} {status}: {job["id"]}',flush=True)
(batch/'animation_progress.json').write_text(json.dumps({'phase':'finished','total':len(jobs),'results':results},indent=2))
