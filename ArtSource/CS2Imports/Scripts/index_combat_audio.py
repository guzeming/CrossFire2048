"""Build a compact audition page and documented model/event/audio associations."""
from collections import Counter, defaultdict
import csv
import hashlib
import json
from pathlib import Path
import re

from export_combat_audio import ROOT, OUT, read, save, strings


def main():
    manifest = read(OUT / 'manifest.json')
    assert not manifest['failures']
    plan = read(OUT / 'export_plan.json')
    assert manifest['count'] == len(plan['sounds'])
    sounds = {r['source_resource']: r for r in manifest['sounds']}
    events = {e['event']: e for e in read(OUT / 'Metadata/sound_events.json')}
    characters = read(OUT / 'Metadata/characters.json')
    reverse = defaultdict(set)
    missing_records = []
    dependency_cache = {}

    def deps(name, visiting=()):
        if name in dependency_cache: return dependency_cache[name]
        assert name not in visiting, ('event dependency cycle', visiting, name)
        result = set(events[name]['direct_sound_resources'])
        for child in events[name].get('referenced_events', []):
            if child in events: result.update(deps(child, (*visiting, name)))
        dependency_cache[name] = result
        return result

    for name, event in events.items():
        resources = sorted(deps(name))
        event['potential_sound_resources'] = resources
        event['wav_files'] = [sounds[r]['wav'] for r in resources if r in sounds]
        event['missing_resources'] = [r for r in resources if r not in sounds]
        for r in resources:
            reverse[r].add(name)
        if event['missing_resources']:
            missing_records.append({'event': name, 'source': event['source'], 'resources': event['missing_resources']})
    save(OUT / 'Metadata/sound_events.json', list(events.values()))
    save(OUT / 'Metadata/missing_resources.json', {
        'reason': 'Referenced by original event definitions but absent from the installed csgo/core archives and 65 inspected map/addon archives; no substitutes assigned.',
        'missing_resources': plan['missing_sound_resources'], 'affected_events': missing_records})

    response_index = {}
    for c in characters:
        pack = c['voice_pack']
        voice = [r for r in sounds.values() if r['source_resource'].startswith(f'sounds/vo/agents/{pack}/')]
        voice_events = [e for e in events.values() if e['source'] == c['sound_events_source']]
        c.update({'voice_clip_count': len(voice), 'event_count': len(voice_events),
                  'events_with_missing_audio': [e['event'] for e in voice_events if e['missing_resources']],
                  'wav_folder': f'WAV/sounds/vo/agents/{pack}',
                  'common_effects': ['Metadata/surface_footsteps.json', 'Metadata/sound_events.json'],
                  'animation_trigger_frames_assigned': False})
        groups = read(OUT / f'Metadata/ResponseRules/{pack}.json')['m_ResponseGroups']
        grouped = []
        for group in groups:
            names = [v['m_value'] for v in group.get('m_responses', []) if v.get('m_type') == 'SPEAK' and 'm_value' in v]
            if not names: continue
            grouped.append({'response_group': group['m_name'], 'notes': group.get('m_notes', ''), 'events': names,
                            'wav_files': sorted({p for n in names if n in events for p in events[n]['wav_files']}),
                            'events_without_definition': [n for n in names if n not in events],
                            'missing_audio_events': [n for n in names if n in events and events[n]['missing_resources']]})
        response_index[pack] = grouped
    save(OUT / 'Metadata/characters.json', characters)
    save(OUT / 'Metadata/voice_responses.json', response_index)

    # Semantic candidates for the 51 exported equipment/weapon meshes. Original
    # event names/parameters remain authoritative; this does not assign timing.
    weapons = []
    models = read(ROOT / 'Batch/plan.json')['models']
    aliases = {'m4a1_silencer': ['m4a1', 'm4a1s'], 'm4a4': ['m4a4', 'm4a1'],
               'usp_silencer': ['usp'], 'mp5sd': ['mp5'], 'cz75a': ['cz', 'cz75a'],
               'incendiarygrenade': ['incgrenade', 'incendiary', 'inferno'], 'molotov': ['molotov', 'inferno'],
               'knife_default_ct': ['knife'], 'knife_default_t': ['knife'],
               'glock18': ['glock', 'glock18'], 'healthshot': ['healthshot'], 'defuse_multimeter': ['defuser', 'c4'],
               'defuser': ['defuser', 'c4']}
    for model in models:
        if model['category'] != 'Weapons': continue
        mid = model['id']
        suffix = re.sub(r'^weapon_(?:rif_|snip_|smg_|pist_|mach_|shot_)?', '', mid)
        names = aliases.get(suffix, [suffix])
        matches = []
        for name, event in events.items():
            if '/vo/' in event['source']: continue
            prefix = name.split('.')[0].lower().removeprefix('weapon_')
            direct = prefix in names
            if mid.startswith('w_eq_'):
                direct = name in ['Player.EquipArmor_CT', 'Player.EquipArmor_T', 'Player.DamageKevlar', 'Player.BurnDamageKevlar']
            if suffix == 'm4a4' and prefix == 'm4a1':
                direct = not any(k in name.lower() for k in ['single', 'silenc'])
            if suffix == 'defuse_multimeter' or suffix == 'defuser':
                direct = 'defus' in name.lower() or name.lower().startswith('c4.disarm')
            if direct: matches.append(name)
        files = sorted({p for n in matches for p in events[n]['wav_files']})
        weapons.append({'id': mid, 'model_resource': model['source'], 'candidate_events': sorted(matches),
                        'wav_files': files, 'association': 'semantic candidates based on original event names; review mode/silencer and shared foley at integration',
                        'animation_trigger_frames_assigned': False})
    save(OUT / 'Metadata/weapons.json', weapons)

    clips = []
    for r in sounds.values():
        path = r['source_resource']; parts = path.split('/')
        if path.startswith('sounds/vo/agents/'):
            group = 'voice:' + parts[3]; family = '角色语音'; label = parts[3]
        elif path.startswith('sounds/weapons/'):
            key = parts[2] if len(parts) > 3 else 'shared'
            group = 'weapon:' + key; family = '武器音效'; label = key
        elif path.startswith('sounds/player/footsteps/'):
            group = 'shared:steps'; family = '角色通用'; label = '脚步、跳跃与落地'
        elif path.startswith('sounds/player/'):
            group = 'shared:body'; family = '角色通用'; label = '受伤、死亡与身体动作'
        elif path.startswith('sounds/items/'):
            group = 'shared:items'; family = '装备音效'; label = '拾取、护具与装备操作'
        elif path.startswith('sounds/physics/'):
            group = 'shared:impacts'; family = '物理音效'; label = '弹着、弹壳、武器与身体碰撞'
        else:
            group = 'shared:dependencies'; family = '相关依赖'; label = '水声、电击与其他依赖'
        names = sorted(reverse[path])
        probe = (' '.join(names) + ' ' + r['name']).lower()
        tags = []
        for tag, patterns in [
            ('开火', ['.single', '.silenced', 'fire', 'shoot', 'shot_']),
            ('换弹', ['reload', 'clipin', 'clipout', 'cliphit', 'addammo', 'magazine', 'insertshell']),
            ('枪机', ['bolt', 'slideback', 'sliderelease', 'slideforward', 'handle']),
            ('切枪', ['draw', 'deploy', 'holster', 'equip', 'pickup']),
            ('投掷', ['throw', 'grenade', 'pullpin']),
            ('爆炸', ['explode', 'explosion', 'detonate']),
            ('脚步', ['step', 'walk', 'run_', 'sprint']),
            ('落地', ['land', 'jump', 'thud']),
            ('受伤', ['damage', 'hurt', 'pain', 'takingfire', 'hit']),
            ('死亡', ['death', 'die', 'dead']),
            ('无线电', ['radio', 'affirmative', 'negative', 'roger', 'agree', 'report']),
            ('战术语音', ['request', 'enemy', 'enemies', 'bomb', 'cover', 'round', 'hostage', 'position']),
        ]:
            if any(p in probe for p in patterns): tags.append(tag)
        clips.append({'name': r['name'], 'wav': r['wav'], 'native': r['native'], 'resource': path,
                      'seconds': r['duration_seconds'], 'rate': r['sample_rate'], 'channels': r['channels'],
                      'group': group, 'family': family, 'group_label': label, 'events': names, 'tags': tags})
    counts = Counter(c['group'] for c in clips)
    family_counts = Counter(c['family'] for c in clips)
    raw = read(OUT / 'Metadata/raw_validation.json')
    assert raw['checked'] == len(plan['sounds']) + len(plan['metadata']) and not raw['failed']
    file_errors = []
    for r in sounds.values():
        p = OUT / r['wav']
        if not p.is_file() or hashlib.sha256(p.read_bytes()).hexdigest() != r['sha256']:
            file_errors.append(r['wav'])
    assert not file_errors, file_errors
    summary = {'wav_files': len(clips), 'new_wav_files': len(clips) - plan['existing_weapon_sounds'],
               'reused_verified_weapon_wav_files': plan['existing_weapon_sounds'], 'selected_characters': len(characters),
               'weapon_equipment_models_indexed': len(weapons), 'voice_clips': family_counts['角色语音'],
               'nonvoice_clips': len(clips) - family_counts['角色语音'], 'event_definitions': len(events),
               'family_counts': family_counts, 'sample_rates': Counter(r['sample_rate'] for r in sounds.values()),
               'channels': Counter(r['channels'] for r in sounds.values()),
               'wav_bytes': sum(r['bytes'] for r in sounds.values()),
               'raw_resources_crc_verified': raw['checked'], 'wav_hashes_verified': len(sounds),
               'audio_decode_roundtrips_passed': len(sounds), 'conversion_failures': [],
               'missing_original_resources': len(plan['missing_sound_resources']), 'affected_events': len(missing_records),
               'unresolved_event_references': plan['unresolved_event_references'],
               'no_resampling_or_volume_normalization': True, 'animation_trigger_frames_assigned': False,
               'unity_assets_imported': False, 'weapon_models_without_candidate_events': [w['id'] for w in weapons if not w['candidate_events']]}
    save(OUT / 'summary.json', summary)
    save(OUT / 'Metadata/clip_index.json', clips)
    with (OUT / 'sound_list.csv').open('w', newline='', encoding='utf-8-sig') as f:
        writer = csv.writer(f)
        writer.writerow(['分类', '分组', '名称', 'WAV', '秒', '采样率', '声道', '原始事件'])
        for c in clips: writer.writerow([c['family'], c['group_label'], c['name'], c['wav'], c['seconds'], c['rate'], c['channels'], ' | '.join(c['events'])])
    payload = {'clips': clips, 'characters': characters, 'weapons': weapons, 'summary': summary}
    template = (Path(__file__).parent / 'combat_audio_browser.html').read_text(encoding='utf-8')
    html = template.replace('/*__AUDIO_DATA__*/', 'const data = ' + json.dumps(payload, ensure_ascii=False, separators=(',', ':')).replace('<', '\\u003c') + ';')
    (OUT / 'START_HERE.html').write_text(html, encoding='utf-8')
    (OUT / 'README.txt').write_text(f'''角色、武器与装备音效导出

入口：START_HERE.html（双击，用浏览器打开；全部数据内嵌，离线可用）。
按角色 / 武器 / 类别筛选后点击试听。每次只播放一条音频。

本次共 {len(clips):,} 个 WAV：{family_counts['角色语音']:,} 条所选 10 个角色的语音、
{len(clips) - family_counts['角色语音']:,} 条武器/角色通用/装备/物理依赖声音。
原有 1,237 条武器 WAV 已校验并归入本目录；新增 {len(clips) - 1237:,} 条。

WAV/：可导入 Unity 的 WAV；保留原采样率和声道，不调整响度。
Source/sounds/：从 VPK 解出的原始 WAV/MP3 等音频及 .vsnd 参数。
Raw/：原始编译资源，{raw['checked']:,} 个资源通过 VPK CRC 校验。
manifest.json：每条音频的路径、时长、SHA256、采样率、转换方式。
Metadata/characters.json：10 个角色与语音包的对应，以及原始配置证据。
Metadata/weapons.json：51 个武器/装备模型与音效事件的语义候选对应。
Metadata/sound_events.json：{len(events):,} 个原始事件及完整参数、曲线、分轨和依赖。
Metadata/ResponseRules/：原始语音响应规则的结构化导出。
Metadata/voice_responses.json：无线电/战术响应组到事件及声音文件的索引。
Metadata/surface_footsteps.json：原游戏地表材质到 CT/T 脚步事件的对应。
Metadata/missing_resources.json：缺失源文件与受影响事件。

已核实的限制：
1. {len(plan['missing_sound_resources'])} 条事件引用的源音频不在本地安装包中（61 条法国宪兵，
   1 条 professional_epic）；额外检索 65 个地图/附加包仍未找到。缺项明确保留，不替换、不伪造。
2. WAV 是单个声音样本。原游戏的分层、随机选择、空间衰减、混响和触发条件需要在 Unity 接入；
   参数已保存，但未自动变成 AudioSource/AudioMixer 或动画事件。事件依赖列表包含条件性子事件，
   不代表所有文件应同时播放。
3. 未指定动画音效帧号；未把这批文件复制到 Unity Assets，也未修改场景。
4. 原有武器库的可选刀具变体也保留，试听页可按基础武器模型筛选。
5. 压缩源解码为 float32 WAV，保留可能超过 1.0 的峰值，不削波、不再次有损编码。
   试听用于选样；浏览器不能还原游戏的完整混音环境。

角色语音对应：
''' + '\n'.join(f"{c['team']}  {c['id']} -> {c['voice_pack']} ({c['voice_clip_count']} 条)" for c in characters) + '\n', encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
