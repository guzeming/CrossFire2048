"""Prepare decoded game audio for Unity without resampling or normalizing levels."""
import aud
import csv
import hashlib
import json
import re
import shutil
import struct
import wave
from collections import Counter
from pathlib import Path

import numpy as np

root = Path(__file__).resolve().parent.parent/'M4A1_S/Audio'
source_root = root/'Source'
events = json.loads((root/'Metadata/m4a1_events.json').read_text(encoding='utf-8'))
records = []

def category(name):
    if name == 'm4a1_silencer_01': return 'Fire/Suppressed','消音开火'
    if re.fullmatch(r'm4a1_0[1-4]',name): return 'Fire/Unsuppressed','非消音开火（当前音效事件引用）'
    if re.fullmatch(r'm4a1_us_0[1-4]',name): return 'Fire/Alternate','额外开火变体（本次事件表未引用）'
    if 'distant' in name: return 'Fire/Distant','远距离开火；具体武器见事件名'
    if name.startswith('movement'): return 'Handling/Movement','持枪动作/装备摩擦'
    if name == 'm4a1_draw': return 'Handling/Draw','拔枪/装备'
    if any(x in name for x in ['clip','bolt','addammo']): return 'Handling/Reload','弹匣/枪机/装填操作'
    if 'silencer' in name: return 'Handling/Silencer','安装/拆卸消音器'
    raise ValueError(name)

for path in sorted(source_root.rglob('*')):
    if path.suffix.lower() not in {'.wav','.mp3'}: continue
    name = path.stem
    group, use = category(name)
    destination = root/'WAV'/group/(name+'.wav')
    destination.parent.mkdir(parents=True,exist_ok=True)
    sound = aud.Sound(str(path))
    rate, channels = sound.specs
    data = sound.data()
    assert data.ndim==2 and data.shape[1]==channels and len(data)>0
    assert np.isfinite(data).all()
    peak = float(np.max(np.abs(data)))
    assert peak>0, name
    if path.suffix.lower()=='.wav':
        shutil.copy2(path,destination)
        conversion='Original WAV copied byte-for-byte'
        assert hashlib.sha256(path.read_bytes()).digest()==hashlib.sha256(destination.read_bytes()).digest()
    else:
        # MP3 decoding can legitimately overshoot +/-1. Float WAV retains those
        # samples without clipping or silently changing the source volume.
        payload=data.astype('<f4').tobytes()
        fmt=struct.pack('<HHIIHH',3,channels,round(rate),round(rate)*channels*4,channels*4,32)
        chunks=b'fmt '+struct.pack('<I',len(fmt))+fmt+b'fact'+struct.pack('<II',4,len(data))+b'data'+struct.pack('<I',len(payload))+payload
        destination.write_bytes(b'RIFF'+struct.pack('<I',len(chunks)+4)+b'WAVE'+chunks)
        conversion='Original MP3 decoded to float32 WAV; no resampling, clipping, or level normalization'
    decoded = aud.Sound(str(destination)).data()
    assert decoded.shape == data.shape
    difference = float(np.max(np.abs(decoded-data)))
    assert difference < 0.0001, (name,difference)
    if path.suffix.lower()=='.wav':
        with wave.open(str(destination),'rb') as check:
            bits = check.getsampwidth()*8
            assert check.getnframes()==len(data)
    else:
        bits=32
    resource = path.relative_to(source_root).with_suffix('.vsnd').as_posix()
    matched = [e['event'] for e in events if resource in e['sounds']]
    metadata = path.with_suffix('.vsnd')
    source_samples=None
    if metadata.exists():
        m=re.search(r'm_nSampleCount\s*=\s*(\d+)',metadata.read_text(encoding='utf-8-sig'))
        if m: source_samples=int(m[1])
    records.append({'name':name,'use_zh':use,'category':group,'wav':destination.relative_to(root).as_posix(),
                    'source_resource':resource+'_c','original_file':path.relative_to(root).as_posix(),
                    'rate_hz':round(rate),'channels':channels,'bits':bits,'frames':len(data),
                    'duration_seconds':round(len(data)/rate,6),'peak_linear':peak,'event_names':matched,
                    'conversion':conversion,'source_declared_samples':source_samples,'max_conversion_error':difference,
                    'sha256':hashlib.sha256(destination.read_bytes()).hexdigest()})

resource_map={r['source_resource'].removesuffix('_c'):r['wav'] for r in records}
for event in events:
    event['wav_files']=[resource_map[p] for p in event['sounds']]
assert len(records)==34, len(records)
event_names={e['event'] for e in events}
associations=[
    {'animations':['draw_m4a1s','draw_crouch_m4a1s'],'events':['Weapon_M4A1.Draw','Weapon_M4A1S.BoltBack','Weapon_M4A1S.BoltForward','Weapon_M4A1.WeaponMove1','Weapon_M4A1.WeaponMove2','Weapon_M4A1.WeaponMove3'],
     'note_zh':'拔枪及拉机柄候选音效，按实际骨骼动作选择触发点，并非要求全部播放。'},
    {'animations':['reload_m4a1s','reload_crouch_m4a1s'],'events':['Weapon_M4A1.Clipout','Weapon_M4A1.Clipin','Weapon_M4A1.ClipHit','Weapon_M4A1.AddAmmo','Weapon_M4A1S.BoltBack','Weapon_M4A1S.BoltForward'],
     'note_zh':'弹匣移出、插入、拍击和装填阶段的候选音效；枪机音效仅在对应枪机动作出现时使用。'},
    {'animations':['shoot_m4a1s'],'events':['Weapon_M4A1.Silenced','Weapon_M4A1.Single','Weapon_M4A1.SingleDistant'],
     'note_zh':'按是否安装消音器选择开火事件，二者不同时播放；远距离层由距离混音控制。'},
    {'animations':['silencer_attach_m4a1s','silencer_detach_m4a1s'],'events':['Weapon_M4A1.Silencer_On','Weapon_M4A1.Silencer_Off','Weapon_M4A1.SilencerScrewOnStart','Weapon_M4A1.SilencerScrewOffEnd']+[f'Weapon_M4A1.SilencerScrew{i}' for i in range(1,6)],
     'note_zh':'补充消音器操作音效；这两个动画未包含在此前的 5 动作 Blender 文件中。'}]
for item in associations:
    assert set(item['events'])<=event_names
    item['association_basis']='Semantic association by source event names; event-to-file mapping is extracted from the game.'
    item['timing_status']='No sound event timing found in exported third-person clips. Trigger frames are not assigned.'
    item['trigger_frames']=None
(root/'sound_manifest.json').write_text(json.dumps({'count':len(records),'sounds':records},indent=2,ensure_ascii=False),encoding='utf-8')
(root/'sound_events.json').write_text(json.dumps(events,indent=2,ensure_ascii=False),encoding='utf-8')
(root/'animation_sound_map.json').write_text(json.dumps(associations,indent=2,ensure_ascii=False),encoding='utf-8')
with (root/'sound_list.csv').open('w',encoding='utf-8-sig',newline='') as output:
    writer=csv.writer(output)
    writer.writerow(['名称','用途','WAV路径','时长秒','采样率Hz','声道','位深','游戏事件名','原始资源'])
    for r in records:
        writer.writerow([r['name'],r['use_zh'],r['wav'],r['duration_seconds'],r['rate_hz'],r['channels'],r['bits'],'; '.join(r['event_names']),r['source_resource']])
summary={'count':len(records),'categories':dict(Counter(r['category'] for r in records)),
         'rate_hz':sorted({r['rate_hz'] for r in records}),'channels':sorted({r['channels'] for r in records}),
         'copied_wav':sum('copied' in r['conversion'] for r in records),'decoded_mp3':sum('decoded' in r['conversion'] for r in records),
         'all_decoded_nonempty_nonzero_finite':True,'all_event_dependencies_resolved':True,
         'no_resampling_or_normalization':True,'event_count':len(events),'animation_sound_binding_performed':False,
         'max_decode_roundtrip_error':max(r['max_conversion_error'] for r in records)}
(root/'validation.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print('AUDIO_READY '+json.dumps(summary),flush=True)
