import json,re
from pathlib import Path
root=Path(__file__).resolve().parent.parent
out=root/'Batch'
out.mkdir(exist_ok=True)
models=[]
for line in (root/'Logs/model_inventory.txt').read_text(encoding='utf-8-sig').splitlines():
    m=re.match(r'(.+\.vmdl_c) CRC:.* size:(\d+)',line)
    if m: models.append((m[1],int(m[2])))
selected_characters={'ctm_sas','ctm_fbi','ctm_st6_variante','ctm_swat_variante','ctm_gendarmerie_varianta',
                     'tm_phoenix','tm_leet_varianta','tm_balkan_variantf','tm_professional_varf','tm_jungle_raider_varianta'}
entries=[]
for path,size in models:
    category=None
    if re.match(r'agents/models/(ctm_|tm_)',path):
        if Path(path).name.removesuffix('.vmdl_c') not in selected_characters: continue
        category='Characters'
    elif path.startswith('models/weapons/w_eq_'):
        category='Weapons'
    elif path.startswith('weapons/models/'):
        if '/knife/' in path and not any('/'+k+'/' in path for k in ['knife_default_ct','knife_default_t']): continue
        if '/shared/' in path and '/shells/' not in path and not any(path.endswith('/'+n+'.vmdl_c') for n in ['grenade_pin','grenade_spoon']): continue
        category='WeaponParts' if path.endswith('_mag.vmdl_c') or '/shells/' in path or '_broken_glass' in path or '/eholster.' in path or path.endswith('/grenade_pin.vmdl_c') or path.endswith('/grenade_spoon.vmdl_c') else 'Weapons'
    if not category: continue
    name=Path(path).name.removesuffix('.vmdl_c')
    existing=None
    if path=='agents/models/ctm_sas/ctm_sas.vmdl_c': existing='CT_SAS/ctm_sas_animated.blend'
    if path=='weapons/models/m4a1_silencer/weapon_rif_m4a1_silencer.vmdl_c': existing='M4A1_S/m4a1_s.blend'
    entries.append({'id':name,'category':category,'source':path,'source_bytes':size,'existing_blend':existing,
                    'relative_dir':category+'/'+name,'gltf':'RawModels/'+path.removesuffix('_c').removesuffix('.vmdl')+'.gltf'})
animations=[]
for line in (root/'Logs/animation_inventory.txt').read_text(encoding='utf-8-sig').splitlines():
    path=line.split(' CRC:')[0]
    if not re.match(r'animation/anims/(world|viewmodel|ui_anims)/.+\.vnmclip_c$',path): continue
    bits=path.split('/')
    if bits[2]=='viewmodel' and bits[3] in ['chicken','egg']: continue
    if bits[3]=='knife' and bits[4] not in ['_default_knife','default_ct','default_t','knife_default_t']: continue
    animations.append({'source':path,'name':Path(path).name.removesuffix('.vnmclip_c'),
                       'group':'/'.join(bits[2:-1]),'gltf':'RawAnimations/'+path.removesuffix('.vnmclip_c')+'.gltf'})
plan={'scope':'All base weapons/equipment (CT/T default knives), 5 CT and 5 T player characters. No cosmetic paint variants, keychains, standalone gloves or cosmetic knife models.',
      'models':entries,'animations':animations,'existing_preserved':['CT_SAS','M4A1_S','DustII']}
(out/'plan.json').write_text(json.dumps(plan,indent=2),encoding='utf-8')
(out/'model_filters.txt').write_text(','.join(e['source'] for e in entries if not e['existing_blend']),encoding='utf-8')
animation_filters=['animation/anims/ui_anims/']
for kind in ['world','viewmodel']:
    for category in ['rifle','pistol','shared','grenade','equipment','arms']:
        animation_filters.append(f'animation/anims/{kind}/{category}/')
    for knife in ['_default_knife','default_ct','default_t','knife_default_t']:
        animation_filters.append(f'animation/anims/{kind}/knife/{knife}/')
(out/'animation_filters.txt').write_text(','.join(animation_filters),encoding='utf-8')
from collections import Counter
print(json.dumps({'models':dict(Counter(e['category'] for e in entries)),'new_models':sum(not e['existing_blend'] for e in entries),'animations':len(animations),'animation_sets':len(set(e['group'] for e in animations))}),flush=True)
