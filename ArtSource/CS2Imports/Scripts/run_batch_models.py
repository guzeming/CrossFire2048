import json,subprocess,time
from pathlib import Path
root=Path(__file__).resolve().parent.parent
batch=root/'Batch'
plan=json.loads((batch/'plan.json').read_text())
logdir=batch/'Logs'
logdir.mkdir(exist_ok=True)
blender='D:/SteamLibrary/steamapps/common/Blender/blender.exe'
results=[]
for index,entry in enumerate(plan['models'],1):
    report=batch/entry['relative_dir']/'report.json'
    progress={'phase':'models','index':index,'total':len(plan['models']),'current':entry['id'],'results':results}
    (batch/'model_progress.json').write_text(json.dumps(progress,indent=2))
    if report.exists() and json.loads(report.read_text()).get('status')=='complete':
        results.append({'id':entry['id'],'status':'complete','reused':True})
        continue
    with (logdir/(entry['id']+'.log')).open('w',encoding='utf-8') as log:
        start=time.monotonic()
        result=subprocess.run([blender,'--background','--python-exit-code','1','--python',str(root/'Scripts/batch_model_worker.py'),'--','--id',entry['id']],stdout=log,stderr=subprocess.STDOUT)
    state='complete' if result.returncode==0 and report.exists() else 'failed'
    results.append({'id':entry['id'],'status':state,'exit_code':result.returncode,'seconds':round(time.monotonic()-start,1)})
    print(f'{index}/{len(plan["models"])} {state}: {entry["id"]}',flush=True)
(batch/'model_progress.json').write_text(json.dumps({'phase':'finished','total':len(plan['models']),'results':results},indent=2))
