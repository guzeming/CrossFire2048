"""Extract selected combat resources and their actual compiled RERL dependencies.

This exports Source 2 particle data, not Unity ParticleSystem prefabs.
"""
import collections
import json
import re
import struct
import subprocess
import zlib
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'CombatFX'
CLI = Path('D:/MyProject/BlenderTools/Source2Viewer-20.0/Source2Viewer-CLI.exe')
GAME = Path('D:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game')
inventory = {}
for package in ['csgo', 'core']:
    for line in (OUT / 'Metadata' / (package+'_inventory.txt')).read_text(encoding='utf-8-sig').splitlines():
        match = re.match(r'(.+) CRC:([0-9a-fA-F]+) size:(\d+)', line)
        if match:
            name, crc, size = match.groups()
            inventory.setdefault(name.lower(), {'path':name,'package':package,'crc':int(crc,16),'size':int(size)})

def category(path):
    if not path.endswith('.vpcf_c'): return None
    if path.startswith('particles/ui/') or '/mvp_' in path: return None
    if 'smokegrenade' in path: return 'SmokeGrenade'
    if 'flashbang' in path: return 'Flashbang'
    if 'hegrenade' in path: return 'HEGrenade'
    if 'decoy' in path: return 'Decoy'
    if path.startswith('particles/inferno_fx/') or 'molotov' in path or 'incgrenade' in path: return 'Molotov_Incendiary'
    if path.startswith(('particles/impact_fx/','particles/blood_impact/','particles/water_impact/')): return 'BulletImpacts'
    if path.startswith('particles/unified_weapon_fx/'): return 'WeaponMuzzle'
    if path.startswith('particles/weapons/'):
        if 'grenade_pin' in path or 'grenade_spoon' in path: return 'GrenadeParts'
        if 'tracer' in path or 'wallbang' in path: return 'BulletTracers'
        if 'shell' in path or 'casing' in path: return 'ShellEjection'
        if any(s in path for s in ['muzzle','heat','smoke','spark']): return 'WeaponMuzzle'
    return None

roots = {path:category(path) for path in inventory if category(path)}
model_roots = [p for p in inventory if p.endswith('.vmdl_c') and
    (p.startswith('weapons/models/shared/shells/') or
     p in ['models/weapons/w_bullet.vmdl_c','models/models/weapons/shared/shell_50cal_hr.vmdl_c',
           'models/models/weapons/shared/shell_762_hr.vmdl_c','models/models/weapons/shared/shell_9mm_hr.vmdl_c'])]
roots.update({p:'AmmoModels' for p in model_roots})
roots.update({p:'GrenadeModels' for p in inventory if p.startswith('weapons/models/grenade/') and p.endswith('.vmdl_c')})

def references(data):
    if len(data)<16: return []
    _,version,_,table_offset,count = struct.unpack_from('<IHHII',data)
    if version != 12: return []
    table=8+table_offset
    result=[]
    for i in range(count):
        pos=table+i*12
        kind,offset,size=struct.unpack_from('<4sII',data,pos)
        start=pos+4+offset
        if kind != b'RERL': continue
        off,n=struct.unpack_from('<II',data,start)
        for k in range(n):
            rec=start+off+k*16
            _,delta=struct.unpack_from('<Qq',data,rec)
            string_pos=rec+8+delta
            result.append(data[string_pos:data.index(b'\0',string_pos)].decode('utf-8').replace('\\','/'))
    return result

def batches(paths, limit=16000):
    current=[];length=0
    for p in sorted(paths):
        if current and length+len(p)+1>limit:
            yield current;current=[];length=0
        current.append(p);length+=len(p)+1
    if current: yield current

def fetch(paths, generation):
    for package in ['csgo','core']:
        group=[p for p in paths if inventory[p]['package']==package and not (OUT/'Raw'/p).exists()]
        for i,batch in enumerate(batches(group)):
            log=OUT/'Logs'/f'raw_{generation}_{package}_{i}.log'
            with log.open('w',encoding='utf-8') as stream:
                subprocess.run([str(CLI),'-i',str(GAME/package/'pak01_dir.vpk'),'-f',','.join(batch),
                                '-o',str(OUT/'Raw'),'--threads','4'],stdout=stream,stderr=subprocess.STDOUT,check=True)

processed={};edges={};missing=[];queue=set(roots);generation=0
while queue:
    fetch(queue,generation)
    next_queue=set()
    for path in sorted(queue):
        data=(OUT/'Raw'/path).read_bytes();entry=inventory[path]
        assert len(data)==entry['size'],path
        assert zlib.crc32(data)&0xffffffff==entry['crc'],path
        deps=[]
        for ref in references(data):
            key=ref.lower()
            resolved=key if key in inventory else key+'_c' if key+'_c' in inventory else None
            if resolved:
                deps.append(resolved)
                if resolved not in processed and resolved not in queue: next_queue.add(resolved)
            else: missing.append({'parent':path,'reference':ref})
        edges[path]=deps
        processed[path]={**entry,'rootCategory':roots.get(path),'dependencies':deps}
    print(f'EXTRACT_LEVEL {generation}: {len(queue)} resources, {len(next_queue)} new dependencies',flush=True)
    queue=next_queue;generation+=1

manifest={'sourceGame':'Counter-Strike 2','sourceRoot':str(GAME), 'roots':roots,
          'resources':processed,'missingReferences':missing,
          'crcVerified':len(processed),'categories':dict(collections.Counter(roots.values()))}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False),encoding='utf-8')
with (OUT/'Logs/decompile.log').open('w',encoding='utf-8') as log:
    result=subprocess.run([str(CLI),'-i',str(OUT/'Raw'),'-o',str(OUT/'Decompiled'),'-d','--recursive',
        '--threads','4','--game',str(GAME/'csgo/gameinfo.gi')],stdout=log,stderr=subprocess.STDOUT)
manifest['decompileExitCode']=result.returncode
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2,ensure_ascii=False),encoding='utf-8')
print('COMBAT_EXTRACTION_FINISHED '+json.dumps({'resources':len(processed),'missing':len(missing),
    'types':dict(collections.Counter(Path(p).suffix for p in processed)), 'decompileExitCode':result.returncode}),flush=True)
