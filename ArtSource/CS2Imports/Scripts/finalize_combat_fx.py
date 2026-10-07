"""Validate extraction and create a small entry catalog above the dependency archive."""
import collections
import concurrent.futures
import html
import json
import re
import shlex
import subprocess
from pathlib import Path
from PIL import Image

ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'CombatFX'
DECODED=OUT/'Decompiled'
manifest=json.loads((OUT/'manifest.json').read_text(encoding='utf-8'))
CLI='D:/MyProject/BlenderTools/Source2Viewer-20.0/Source2Viewer-CLI.exe'

def relative(p): return p.relative_to(OUT).as_posix()
def material_data(path):
    output=OUT/'Metadata/MaterialData'/path.removesuffix('_c')
    output=output.with_suffix('.kv3');output.parent.mkdir(parents=True,exist_ok=True)
    result=subprocess.run([CLI,'-i',str(OUT/'Raw'/path),'-b','DATA'],capture_output=True,text=True,encoding='utf-8',errors='replace',check=True)
    text=result.stdout[result.stdout.index('<!-- kv3'):]
    output.write_text(text,encoding='utf-8')
    return {'source':path,'data':relative(output),'decompiled':relative(DECODED/path.removesuffix('_c')),
            'compiledTextureReferences':manifest['resources'][path]['dependencies']}

materials=[p for p in manifest['resources'] if p.endswith('.vmat_c')]
with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
    material_records=list(pool.map(material_data,materials))
(OUT/'Metadata/materials.json').write_text(json.dumps(material_records,indent=2),encoding='utf-8')

images=[]
for i,p in enumerate(sorted(DECODED.rglob('*.png'))):
    with Image.open(p) as im:
        width,height=im.size;mode=im.mode;im.verify()
    assert width>0 and height>0,p
    images.append({'file':relative(p),'width':width,'height':height,'mode':mode})
    if i and i%2000==0: print('PNG_VERIFIED '+str(i),flush=True)
image_lookup={e['file']:e for e in images}

sheets=[]
for p in sorted(DECODED.rglob('*.mks')):
    sheet={'file':relative(p),'sequences':[],'otherDirectives':[]}
    current=None
    for line in p.read_text(encoding='utf-8-sig').splitlines():
        line=line.strip()
        if not line or line.startswith('//'): continue
        tokens=shlex.split(line)
        if tokens[0].startswith('sequence'):
            current={'id':int(tokens[1]),'channelMode':tokens[0],'loop':False,'frames':[]}
            sheet['sequences'].append(current)
        elif tokens[0]=='LOOP': current['loop']=True
        elif tokens[0]=='frame':
            image=p.parent/tokens[1];assert image.is_file(),str(image)
            current['frames'].append({'file':relative(image),'durationSourceUnits':float(tokens[2])})
        else: sheet['otherDirectives'].append(line)
    sheets.append(sheet)
(OUT/'Metadata/sprite_sequences.json').write_text(json.dumps(sheets,indent=2),encoding='utf-8')
(OUT/'Metadata/images.json').write_text(json.dumps(images,indent=2),encoding='utf-8')

texture_records=[]
for path in manifest['resources']:
    if not path.endswith('.vtex_c'): continue
    base=DECODED/path.removesuffix('.vtex_c')
    outputs=[p for p in base.parent.glob(base.name+'*') if p.is_file() and p.suffix in ('.png','.mks','.vtex','.exr')]
    assert any(p.suffix in ('.png','.mks','.exr') for p in outputs),path
    texture_records.append({'source':path,'outputs':[relative(p) for p in sorted(outputs)]})
(OUT/'Metadata/textures.json').write_text(json.dumps(texture_records,indent=2),encoding='utf-8')

def closure(path):
    found=set();pending=[path]
    while pending:
        p=pending.pop()
        if p in found: continue
        found.add(p);pending.extend(manifest['resources'][p]['dependencies'])
    return found

particles=[]
for path,info in manifest['resources'].items():
    if not path.endswith('.vpcf_c'): continue
    p=DECODED/path.removesuffix('_c');text=p.read_text(encoding='utf-8-sig')
    assert 'CParticleSystemDefinition' in text,path
    deps=closure(path)
    particles.append({'source':path,'file':relative(p),'category':info['rootCategory'] or 'Dependency',
        'operators':sorted(set(re.findall(r'_class\s*=\s*"([^"]+)"',text))),
        'dependencyCount':len(deps)-1,
        'missingReferences':[e for e in manifest['missingReferences'] if e['parent'] in deps]})
(OUT/'Metadata/particles.json').write_text(json.dumps(particles,indent=2),encoding='utf-8')

models=[]
for entry in json.loads((OUT/'model_plan.json').read_text(encoding='utf-8')):
    folder=OUT/entry['folder']
    report=json.loads((folder/'report.json').read_text(encoding='utf-8'))
    record={**entry,'status':report['status']}
    dependencies=closure(entry['source'])
    record['missingSourceReferences']=[e for e in manifest['missingReferences'] if e['parent'] in dependencies]
    record['materialFidelity']='source_material_unavailable' if record['missingSourceReferences'] else 'approximate_PBR'
    if report['status']=='complete':
        assert report['fbx_validation']['passed'],entry['id']
        for ext in ('.fbx','.blend'):
            file=folder/(entry['id']+ext);assert file.is_file(),file
            record[ext[1:]]=relative(file)
        gltf=folder/(entry['id']+'.gltf');doc=json.loads(gltf.read_text())
        from urllib.parse import unquote
        for kind in ('images','buffers'):
            for item in doc.get(kind,[]):
                if not item['uri'].startswith('data:'): assert (gltf.parent/unquote(item['uri'])).exists(),item['uri']
    models.append(record)
(OUT/'Metadata/models.json').write_text(json.dumps(models,indent=2),encoding='utf-8')

primary=[
 ('步枪弹道','particles/weapons/cs_weapon_fx/weapon_tracers_assrifle.vpcf_c'),
 ('通用弹道','particles/weapons/cs_weapon_fx/weapon_tracers.vpcf_c'),
 ('高爆手雷','particles/explosions_fx/explosion_hegrenade.vpcf_c'),
 ('闪光弹','particles/explosions_fx/explosion_flashbang.vpcf_c'),
 ('烟雾弹：粒子入口','particles/explosions_fx/explosion_smokegrenade.vpcf_c'),
 ('烟雾弹：体积辅助','particles/explosions_fx/explosion_smokegrenade_voxel.vpcf_c'),
 ('燃烧瓶：投掷拖尾','particles/weapons/cs_weapon_fx/weapon_molotov_thrown.vpcf_c'),
 ('燃烧瓶：爆燃','particles/inferno_fx/molotov_explosion.vpcf_c'),
 ('地面燃烧','particles/inferno_fx/molotov_groundfire_main.vpcf_c'),
 ('燃烧弹：投掷拖尾','particles/inferno_fx/incgrenade_thrown_trail.vpcf_c'),
 ('诱饵弹','particles/weapons/cs_weapon_fx/weapon_decoy_ground_effect.vpcf_c')]
lookup={p['source']:p for p in particles}
entries=[{'label':label,**lookup[path]} for label,path in primary]
(OUT/'START_HERE.json').write_text(json.dumps(entries,indent=2,ensure_ascii=False),encoding='utf-8')

counts={'particleConfigurations':len(particles),'compiledResourcesCRCVerified':manifest['crcVerified'],
        'textureResources':len(texture_records),'pngImages':len(images),'spriteSheets':len(sheets),
        'spriteSequences':sum(len(s['sequences']) for s in sheets),
        'spriteFrameReferences':sum(len(seq['frames']) for s in sheets for seq in s['sequences']),
        'materialConfigurations':len(material_records),'completeModels':sum(m['status']=='complete' for m in models),
        'ammoModels':sum(m['category']=='Ammo' and m['status']=='complete' for m in models),
        'grenadeModelsAndParts':sum(m['category']=='Grenades' and m['status']=='complete' for m in models),
        'missingSourceMaterialReferences':len(manifest['missingReferences']),
        'emptySourceModels':[m['id'] for m in models if m['status']!='complete']}
summary={'exportValidationPassed':True,'dependencyClosureComplete':not manifest['missingReferences'],**counts,
    'unityParticlePrefabsCreated':False,
    'limits':['Source 2 VPCF and snapshot behavior must be rebuilt in Unity.',
              'The original package has 9 unavailable legacy material references; 65 additional local archives were checked.',
              'w_bullet is an empty resource; use the 16 actual ammo/casing models.',
              'VMAT text uses fallback shader mapping because exporter 20 does not understand shader version 72. Metadata/MaterialData keeps the original decoded parameters.',
              'Raw VSnap resources are preserved; decompiled snapshots may omit skinning streams.',
              'MKS duration values are preserved without assuming a Unity playback frame rate.']}
(OUT/'summary.json').write_text(json.dumps(summary,indent=2,ensure_ascii=False),encoding='utf-8')
(OUT/'Metadata/missing_source_references.json').write_text(json.dumps(manifest['missingReferences'],indent=2),encoding='utf-8')

links=''.join('<tr><td>'+html.escape(e['label'])+'</td><td><a href="'+e['file']+'">'+html.escape(Path(e['file']).name)+'</a></td><td>'+str(e['dependencyCount'])+'</td></tr>' for e in entries)
ammo=''.join('<li>'+html.escape(m['id'])+' · <a href="'+m['fbx']+'">FBX</a> · <a href="'+m['blend']+'">Blender</a></li>' for m in models if m['category']=='Ammo' and m['status']=='complete')
page='''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><title>战斗特效资源入口</title>
<style>body{font:16px/1.7 system-ui,sans-serif;max-width:1100px;margin:40px auto;padding:0 24px;background:#121923;color:#d8e2f0}a{color:#82c4ff}h1,h2{color:white}table{width:100%;border-collapse:collapse}td,th{text-align:left;border-bottom:1px solid #334155;padding:10px}ul{columns:2}aside{padding:16px;background:#1d2a3c;border-radius:8px}small{color:#adbacd}</style>
<h1>子弹与投掷物特效 · 常用入口</h1><p>先从这里选效果，再按依赖清单取素材。完整库的 1123 个粒子文件包含子效果、地面变体、质量档位和旧版备选。</p>
<aside>模型 FBX 可导入 Unity。粒子导出为 Source 2 VPCF 配置、PNG 图像和 MKS 序列信息，需要在 Unity Particle System / VFX Graph 中重建。体积烟雾、交互和闪光致盲逻辑没有转换。</aside>
<h2>16 个子弹与弹壳模型</h2><ul>'''+ammo+'''</ul><h2>11 个常用效果入口</h2><small>这些是从文件名与依赖关系整理的候选入口，未声称均为当前游戏版本实际调用的效果。</small><table><thead><tr><th>用途</th><th>配置</th><th>依赖资源数</th></tr></thead><tbody>'''+links+'''</tbody></table>
<h2>完整索引</h2><p><a href="Metadata/models.json">模型索引</a> · <a href="Metadata/particles.json">粒子与操作器</a> · <a href="Metadata/textures.json">贴图来源</a> · <a href="Metadata/sprite_sequences.json">序列帧顺序/通道/时长值</a> · <a href="Metadata/materials.json">材质参数</a> · <a href="summary.json">验证结果</a></p>
<p>9 个旧版碎片材质在本机安装包中缺失，受影响配置已标注：<a href="Metadata/missing_source_references.json">缺失清单</a>。w_bullet 本身没有网格，其原始文件另存保留。</p>
<p>原始编译文件在 Raw；可读配置与 PNG 序列在 Decompiled；FBX/Blender 模型在 Models。</p>
<small>提取工具：<a href="https://s2v.app/ValveResourceFormat/guides/format-support.html">Source 2 Viewer / ValveResourceFormat</a>。源着色器、游戏逻辑和 Unity 特效重建不包含在本次资源导出内。</small></html>'''
(OUT/'START_HERE.html').write_text(page,encoding='utf-8')
readme=f'''子弹与投掷物特效资源

打开 START_HERE.html，从 16 个子弹/弹壳模型和 11 个常用特效候选入口开始。
全部 {len(particles)} 个粒子配置包含大量子效果、不同表面/武器、远近/质量及旧版备选，
无需在 Unity 中创建同等数量的独立 Prefab，也不会因为存放在 ArtSource 而自动进入游戏。

Models：FBX + Blender + glTF，保持现有角色/武器导出的单位与贴图。
Decompiled：VPCF、PNG、MKS、材质和快照等解码资源。
Metadata/sprite_sequences.json：按原始顺序保留序列帧、LOOP、RGB/Alpha 分组和时长数值。
Metadata/MaterialData：编译材质的原始参数 KV3；优先据此重建 Unity 材质。
Raw：从安装包提取且经过 CRC 验证的原始文件。

粒子配置不能直接作为 Unity ParticleSystem 使用。本次没有创建 Unity 特效 Prefab。
体积烟雾模拟、子弹打散烟雾、闪光致盲、燃烧范围和伤害属于后续重建的渲染/游戏逻辑。
贴图里包含颜色、法线、运动矢量和分离 Alpha，不能全部当作普通颜色贴图。
材质解码器对游戏着色器版本 72 有兼容限制；已另存原始材质参数与编译文件。
9 个旧版模型材质在主资源包和额外 65 个本机资源包中均未找到，详见缺失清单。
models/weapons/w_bullet.vmdl_c 是空资源，未伪造它的网格；提供其他 16 个实际弹药模型。

统计及验证结果：summary.json。
来源工具与格式说明：https://s2v.app/ValveResourceFormat/guides/format-support.html
'''
(OUT/'README.txt').write_text(readme,encoding='utf-8')
print('COMBAT_FX_VALIDATION '+json.dumps(counts),flush=True)
