import json,re
from pathlib import Path
root=Path(__file__).resolve().parent.parent/'M4A1_S/Audio'
text=(root/'Metadata/game_sounds_weapons.vsndevts').read_text(encoding='utf-8-sig')
events=[]
for match in re.finditer(r'^\t([^\s=]+)\s*=\s*\n\t\{',text,re.M):
    start=match.end()-1
    depth=1
    end=start+1
    quoted=False
    escaped=False
    while depth:
        c=text[end]
        if quoted:
            if escaped: escaped=False
            elif c=='\\': escaped=True
            elif c=='"': quoted=False
        elif c=='"': quoted=True
        elif c=='{': depth+=1
        elif c=='}': depth-=1
        end+=1
    body=text[start:end]
    paths=sorted(set(re.findall(r'"(sounds/[^"\n]+\.vsnd)"',body)))
    if 'm4a1' not in match[1].lower() and not any('/m4a1/' in p for p in paths):
        continue
    values={}
    for key in ['volume','pitch','mixgroup','volume_random_min','volume_random_max','pitch_random_min','pitch_random_max']:
        v=re.search(r'^\s*'+key+r'\s*=\s*([^\r\n]+)',body,re.M)
        if v: values[key]=v[1].strip().strip('"')
    events.append({'event':match[1],'sounds':paths,'source_parameters':values})
    print(match[1]+': '+', '.join(paths),flush=True)
(root/'Metadata/m4a1_events.json').write_text(json.dumps(events,indent=2),encoding='utf-8')
dependencies=sorted(set(p+'_c' for event in events for p in event['sounds'] if '/m4a1/' not in p))
(root/'Metadata/additional_sound_resources.txt').write_text('\n'.join(dependencies),encoding='utf-8')
print('ADDITIONAL '+str(len(dependencies)))
