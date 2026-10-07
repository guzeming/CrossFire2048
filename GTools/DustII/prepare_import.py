"""Stage Dust II and preserve source texture resolution for Unity import."""
import hashlib
import json
import shutil
from pathlib import Path
from urllib.parse import unquote
from PIL import Image, ImageOps

project = Path(__file__).resolve().parents[2]
source = project / 'ArtSource/CS2Imports/DustII'
stage = project / '.build-check/dustii-import'
art = stage / 'Assets/Art/Maps/DustII'
for folder in ['Models','Textures','Materials','Prefabs','Runtime']:
    (art / folder).mkdir(parents=True, exist_ok=True)
(stage/'Assets/Editor').mkdir(parents=True, exist_ok=True)
(stage/'Assets/Scenes').mkdir(parents=True, exist_ok=True)
shutil.copytree(project/'ProjectSettings', stage/'ProjectSettings', dirs_exist_ok=True)
shutil.copytree(project/'Assets/Settings', stage/'Assets/Settings', dirs_exist_ok=True)
for p in (project/'Assets').glob('*GlobalSettings*'): shutil.copy2(p,stage/'Assets'/p.name)
(stage/'Packages').mkdir(exist_ok=True)
(stage/'Packages/manifest.json').write_text(json.dumps({'dependencies': {
    'com.unity.render-pipelines.universal': '14.0.11',
    'com.unity.modules.physics':'1.0.0',
    'com.unity.modules.audio':'1.0.0',
    'com.unity.modules.imgui':'1.0.0',
    'com.unity.modules.imageconversion':'1.0.0',
    'com.unity.modules.jsonserialize':'1.0.0'
}},indent=2))
manifest = json.loads((source/'UnityExport/source_manifest.json').read_text(encoding='utf-8'))
result = {k:manifest[k] for k in ['meshCount','triangleCount','hiddenToolMeshes','chunks','boundsBlender','nonCollidableMeshes']}
result['materials'], result['textures'] = [], []
texture_roles = {}
def texture(info, role):
    if not info: return ''
    name = unquote(manifest['images'][manifest['textures'][info['index']]['source']]['uri'])
    texture_roles[name] = role
    return name
def packed(info):
    orm = texture(info, 'linear')
    if not orm: return ''
    destination = 'MS_' + orm
    if not (art/'Textures'/destination).exists():
        with Image.open(source/orm) as raw:
            r,g,b,a = raw.convert('RGBA').split()
            Image.merge('RGBA', (b,r,Image.new('L',raw.size),ImageOps.invert(g))).save(art/'Textures'/destination)
    texture_roles[destination] = 'linear'
    return destination
for m in manifest['materials']:
    if '/tools/' in m.get('extras',{}).get('vmat',{}).get('Name',''): continue
    pbr = m.get('pbrMetallicRoughness',{})
    result['materials'].append({
        'name':m['name'], 'color':texture(pbr.get('baseColorTexture'),'color'),
        'normal':texture(m.get('normalTexture'),'normal'),
        'metalSmoothness':packed(pbr.get('metallicRoughnessTexture')),
        'emission':texture(m.get('emissiveTexture'),'color'),
        'factor':pbr.get('baseColorFactor',[1,1,1,1]),
        'metallic':pbr.get('metallicFactor',1), 'roughness':pbr.get('roughnessFactor',1),
        'normalScale':m.get('normalTexture',{}).get('scale',1),
        'alphaMode':m.get('alphaMode','OPAQUE'), 'cutoff':m.get('alphaCutoff',0.5),
        'doubleSided':m.get('doubleSided',False),
        'sourceMaterial':m.get('extras',{}).get('vmat',{}).get('Name','')})
for name, role in texture_roles.items():
    if (source/name).exists(): shutil.copy2(source/name,art/'Textures'/name)
    with Image.open(art/'Textures'/name) as im: width,height=im.size
    result['textures'].append({'name':name,'role':role,'width':width,'height':height})
for chunk in manifest['chunks']: shutil.copy2(source/'UnityExport'/chunk['file'],art/'Models'/chunk['file'])
(art/'import_manifest.json').write_text(json.dumps(result,indent=2),encoding='utf-8')
for src,dst in [('BuildDustII.cs',stage/'Assets/Editor/BuildDustII.cs'),
                ('DustIIEffectCleanup.cs',stage/'Assets/Editor/DustIIEffectCleanup.cs'),
                ('DustIIViewer.cs',art/'Runtime/DustIIViewer.cs')]:
    shutil.copy2(project/'GTools/DustII'/src,dst)
print(json.dumps({'stage':str(stage),'models':len(result['chunks']),'materials':len(result['materials']),'textures':len(result['textures'])}))
