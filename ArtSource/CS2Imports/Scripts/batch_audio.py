import aud,csv,hashlib,json,re,shutil,struct
from pathlib import Path
import numpy as np
root=Path(__file__).resolve().parent.parent
folder=root/'Batch/Audio'
source=folder/'Source'
records=[]
failures=[]
for path in sorted(source.rglob('*')):
    if path.suffix.lower() not in ['.wav','.mp3','.ogg']: continue
    relative=path.relative_to(source)
    output=folder/'WAV'/relative.with_suffix('.wav')
    output.parent.mkdir(parents=True,exist_ok=True)
    try:
        sound=aud.Sound(str(path))
        rate,channels=sound.specs
        data=sound.data()
        if not len(data) or not np.isfinite(data).all(): raise ValueError('Empty/nonfinite audio')
        if path.suffix.lower()=='.wav':
            shutil.copy2(path,output)
            conversion='Native WAV copied unchanged'
        else:
            payload=data.astype('<f4').tobytes()
            fmt=struct.pack('<HHIIHH',3,channels,round(rate),round(rate)*channels*4,channels*4,32)
            chunks=b'fmt '+struct.pack('<I',len(fmt))+fmt+b'fact'+struct.pack('<II',4,len(data))+b'data'+struct.pack('<I',len(payload))+payload
            output.write_bytes(b'RIFF'+struct.pack('<I',len(chunks)+4)+b'WAVE'+chunks)
            conversion='Decoded to float32 WAV; no clipping, resampling or normalization'
        decoded=aud.Sound(str(output)).data()
        assert data.shape==decoded.shape and np.max(np.abs(data-decoded))<0.0001
        records.append({'name':path.stem,'source_resource':relative.with_suffix('.vsnd_c').as_posix(),
                        'wav':output.relative_to(folder).as_posix(),'sample_rate':round(rate),'channels':channels,
                        'duration_seconds':round(len(data)/rate,6),'peak_linear':float(np.max(np.abs(data))),
                        'conversion':conversion,'sha256':hashlib.sha256(output.read_bytes()).hexdigest()})
    except Exception as error:
        failures.append({'source':relative.as_posix(),'error':str(error)})
    if (len(records)+len(failures))%100==0: print('AUDIO '+str(len(records))+' validated',flush=True)
(folder/'manifest.json').write_text(json.dumps({'count':len(records),'sounds':records,'failures':failures},indent=2),encoding='utf-8')
with (folder/'sound_list.csv').open('w',encoding='utf-8-sig',newline='') as f:
    writer=csv.writer(f)
    writer.writerow(['名称','WAV路径','时长秒','采样率','声道','原始资源'])
    for r in records: writer.writerow([r['name'],r['wav'],r['duration_seconds'],r['sample_rate'],r['channels'],r['source_resource']])
events_text=(root/'M4A1_S/Audio/Metadata/game_sounds_weapons.vsndevts').read_text(encoding='utf-8-sig')
resource_map={r['source_resource'].removesuffix('_c'):r['wav'] for r in records}
events=[]
for match in re.finditer(r'^\t([^\s=]+)\s*=\s*\n\t\{',events_text,re.M):
    start=match.end()-1
    # Exported KV3 has one-tab closing braces only at the event level.
    end=events_text.find('\n\t}',start)
    if end<0: continue
    body=events_text[start:end+3]
    paths=sorted(set(re.findall(r'"(sounds/[^"\n]+\.vsnd)"',body)))
    if not any(p in resource_map for p in paths): continue
    values={}
    for key in ['volume','pitch','volume_random_min','volume_random_max','pitch_random_min','pitch_random_max','mixgroup']:
        value=re.search(r'^\s*'+key+r'\s*=\s*([^\r\n]+)',body,re.M)
        if value: values[key]=value[1].strip().strip('"')
    events.append({'event':match[1],'source_parameters':values,'sounds':paths,'wav_files':[resource_map[p] for p in paths if p in resource_map],
                   'missing_resources':[p+'_c' for p in paths if p not in resource_map]})
(folder/'sound_events.json').write_text(json.dumps(events,indent=2),encoding='utf-8')
missing=sorted({p for e in events for p in e['missing_resources']})
(folder/'missing_event_dependencies.txt').write_text('\n'.join(missing),encoding='utf-8')
summary={'count':len(records),'failures':failures,'event_count':len(events),'missing_event_resources':len(missing),'no_resampling_or_level_normalization':True,'animation_trigger_frames_assigned':False}
(folder/'validation.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print('AUDIO_COMPLETE '+json.dumps(summary),flush=True)
if failures: raise RuntimeError('Audio conversion failures: '+str(len(failures)))
