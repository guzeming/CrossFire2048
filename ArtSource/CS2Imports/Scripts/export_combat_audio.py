"""Export the selected actors' voices plus shared character/weapon sounds.

Run plan/extract with normal Python; run convert inside Blender (aud decoder).
All files are staged in ArtSource; no Unity scene or animation is modified.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re
import shutil
import struct
import subprocess
import zlib

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / 'CombatAudio'
CLI = Path('D:/MyProject/BlenderTools/Source2Viewer-20.0/Source2Viewer-CLI.exe')
GAME = Path('D:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game')


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf-8')


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def kv3(path):
    """Parse exported text KV3, retaining nested curves, tracks and rule arrays."""
    text = path.read_text(encoding='utf-8-sig')
    text = re.sub(r'<!--.*?-->', '', text, flags=re.S)
    pattern = re.compile(r'\s+|//[^\n]*|/\*.*?\*/|""".*?"""|"(?:\\.|[^"\\])*"|[{}\[\]=,:]|[^\s{}\[\]=,:]+', re.S)
    tokens = [m.group() for m in pattern.finditer(text)
              if not m.group().isspace() and not m.group().startswith(('//', '/*'))]
    i = 0

    def atom(t):
        if t.startswith('"""'):
            return t[3:-3]
        if t.startswith('"'):
            return json.loads(t)
        if t == 'true': return True
        if t == 'false': return False
        if t == 'null': return None
        try: return int(t)
        except ValueError: pass
        try: return float(t)
        except ValueError: return t

    def value():
        nonlocal i
        t = tokens[i]; i += 1
        if t == '{':
            result = {}
            while tokens[i] != '}':
                if tokens[i] == ',': i += 1; continue
                key = str(atom(tokens[i])); i += 1
                assert tokens[i] == '=', (path, key, tokens[i:i+4])
                i += 1
                assert key not in result, (path, 'duplicate key', key)
                result[key] = value()
            i += 1
            return result
        if t == '[':
            result = []
            while tokens[i] != ']':
                if tokens[i] == ',': i += 1; continue
                result.append(value())
            i += 1
            return result
        result = atom(t)
        if i < len(tokens) and tokens[i] == ':':
            i += 1
            return {'kv3_type': result, 'value': value()}
        return result

    result = value()
    assert i == len(tokens), (path, tokens[i:i+10])
    return result


def strings(value):
    if isinstance(value, str): yield value
    elif isinstance(value, dict):
        for v in value.values(): yield from strings(v)
    elif isinstance(value, list):
        for v in value: yield from strings(v)


def inventory():
    result = {}
    for game in ['core', 'csgo']:
        for line in (ROOT / f'CombatFX/Metadata/{game}_inventory.txt').read_text().splitlines():
            m = re.fullmatch(r'(.+) CRC:([0-9A-Fa-f]+) size:(\d+)', line)
            if m: result[m[1]] = {'archive': game, 'crc32': f'{int(m[2], 16):08x}', 'bytes': int(m[3])}
    return result


def plan():
    source = OUT / 'Source'
    inv = inventory()
    assert inv
    models = read(ROOT / 'Batch/plan.json')['models']
    shared = kv3(source / 'scripts/talker/shared.vrr')
    criteria = {v['m_name']: v for v in shared.get('m_Criteria', [])}
    if not criteria:
        for v in shared.values():
            if isinstance(v, list):
                criteria.update({x['m_name']: x for x in v if isinstance(x, dict) and 'm_matchKey' in x})
    items_text = (source / 'scripts/items/items_game.txt').read_text(encoding='utf-8-sig')
    item_blocks = re.findall(r'^\t\t"[^"\n]+"\s*\n\t\t\{.*?^\t\t\}', items_text, re.M | re.S)
    characters = []
    rules = {}
    for model in models:
        if model['category'] != 'Characters': continue
        mid = model['id']
        blocks = [b for b in item_blocks if re.search(r'^\t\t\t"name"\s+"customplayer_' + re.escape(mid) + '"', b, re.M)]
        assert len(blocks) == 1, (mid, len(blocks))
        item = blocks[0]
        explicit = re.search(r'"vo_prefix"\s+"([^"\n]+)"', item)
        if explicit:
            pack = explicit[1]
            evidence = {'kind': 'items_game.vo_prefix', 'value': pack}
        else:
            matches = [(name[2:], c) for name, c in criteria.items()
                       if name.startswith('Is') and c.get('m_matchKey') == 'model'
                       and mid.lower().startswith(c.get('m_matchExpr', '\0').lower())]
            assert len(matches) == 1, (mid, matches)
            pack, criterion = matches[0]
            evidence = {'kind': 'shared_response_rule_model_criterion', 'criterion': criterion}
        event_path = f'soundevents/vo/agents/game_sounds_{pack}.vsndevts'
        assert (source / event_path).is_file()
        rp = f'scripts/talker/{pack}.vrr'
        rules[pack] = kv3(source / rp)
        assert event_path in rules[pack]['m_SoundEventScripts']
        characters.append({'id': mid, 'team': 'CT' if mid.startswith('ctm_') else 'T',
                           'model_resource': model['source'], 'voice_pack': pack,
                           'mapping_evidence': evidence, 'sound_events_source': event_path,
                           'response_rules_source': rp, 'item_definition': item})

    all_events = {}
    for path in sorted((source / 'soundevents').rglob('*.vsndevts')):
        for name, parameters in kv3(path).items():
            if not isinstance(parameters, dict): continue
            assert name not in all_events, ('duplicate event', name)
            all_events[name] = {'event': name, 'source': path.relative_to(source).as_posix(), 'parameters': parameters}
    chosen_sources = {c['sound_events_source'] for c in characters}
    chosen_sources.update(f'soundevents/game_sounds_{n}.vsndevts' for n in ['weapons', 'player', 'footsteps'])
    selected = {n for n, e in all_events.items() if e['source'] in chosen_sources}
    # Bullet surface hits, physical weapon/equipment drops and body impacts.
    selected.update(n for n, e in all_events.items() if e['source'].endswith('game_sounds_physics.vsndevts')
                    and (any(k in n.lower() for k in ['bulletimpact', 'weapon', 'grenade', 'defuser', 'gloves'])
                         or n.startswith(('Flesh.', 'Flesh_', 'CT.', 'T.', 'Water.Player', 'Bounce.Shell', 'Bounce.ShotgunShell'))
                         or n == 'Default.Land'))
    unresolved_events = []
    queue = sorted(selected)
    while queue:
        event = all_events[queue.pop()]
        refs = []
        for key, value in event['parameters'].items():
            if key == 'base' or re.fullmatch(r'soundevent_\d+', key):
                refs += [v for v in strings(value) if v]
        event['referenced_events'] = refs
        for ref in refs:
            if ref not in all_events:
                unresolved_events.append({'event': event['event'], 'reference': ref})
            elif ref not in selected:
                selected.add(ref); queue.append(ref)
    events = [all_events[n] for n in sorted(selected)]
    resources = set()
    for event in events:
        event['direct_sound_resources'] = sorted({v + '_c' for v in strings(event['parameters']) if v.endswith('.vsnd')})
        resources.update(event['direct_sound_resources'])
    # Keep the already delivered weapon collection intact, including optional knife variations.
    old = read(ROOT / 'Batch/Audio/manifest.json')
    resources.update(r['source_resource'] for r in old['sounds'])
    # Full selected voice folders include clips unused by the current event scripts.
    voice_prefixes = tuple(f"sounds/vo/agents/{c['voice_pack']}/" for c in characters)
    resources.update(p for p in inv if p.endswith('.vsnd_c') and p.startswith(voice_prefixes))
    resources.update(p for p in inv if p.endswith('.vsnd_c') and (
        p.startswith('sounds/player/') and not p.startswith(('sounds/player/halloween/', 'sounds/player/winter/'))
        or p.startswith('sounds/items/') and not any(t in p for t in ['rush_', 'spraycan'])
        or p.startswith(('sounds/physics/body/', 'sounds/physics/weapons/', 'sounds/physics/flesh/'))))
    missing = sorted(p for p in resources if p not in inv)
    sounds = [{'resource': p, **inv[p]} for p in sorted(resources) if p in inv]
    metadata = {e['source'] + '_c' for e in events}
    metadata.update(f'scripts/talker/{pack}.vrr_c' for pack in rules)
    metadata.update(['scripts/talker/shared.vrr_c', 'scripts/talker/response_rules.vrr_c', 'scripts/items/items_game.txt',
                     'scripts/surfaceproperties_footsteps.txt', 'scripts/surfaceproperties_game.txt',
                     'scripts/surfaceproperties_impact_effects.txt', 'scripts/soundmixers.txt'])
    save(OUT / 'Metadata/characters.json', characters)
    save(OUT / 'Metadata/sound_events.json', events)
    for pack, data in rules.items(): save(OUT / f'Metadata/ResponseRules/{pack}.json', data)
    save(OUT / 'Metadata/ResponseRules/shared.json', shared)
    save(OUT / 'Metadata/surface_footsteps.json', kv3(source / 'scripts/surfaceproperties_footsteps.txt'))
    result = {'sounds': sounds, 'metadata': [{'resource': p, **inv[p]} for p in sorted(metadata)],
              'missing_sound_resources': missing, 'unresolved_event_references': unresolved_events,
              'characters': len(characters), 'events': len(events), 'existing_weapon_sounds': old['count']}
    save(OUT / 'export_plan.json', result)
    print(json.dumps({k: len(v) if isinstance(v, list) else v for k, v in result.items() if k not in ['sounds', 'metadata']}, ensure_ascii=False), flush=True)
    print('planned sounds', len(sounds), 'compiled MB', round(sum(r['bytes'] for r in sounds) / 1e6), flush=True)
    print('packs', {c['id']: c['voice_pack'] for c in characters}, flush=True)


def extract():
    data = read(OUT / 'export_plan.json')
    logs = OUT / 'Logs'; logs.mkdir(exist_ok=True)
    rows = data['sounds'] + data['metadata']
    raw = OUT / 'Raw'
    for archive in ['csgo', 'core']:
        pending = []
        for row in rows:
            if row['archive'] != archive: continue
            path = raw / row['resource']
            if path.is_file() and path.stat().st_size == row['bytes'] and f'{zlib.crc32(path.read_bytes()) & 0xffffffff:08x}' == row['crc32']: continue
            pending.append(row['resource'])
        batch = []; size = 0; batches = []
        for path in pending:
            if size + len(path) > 16000:
                batches.append(batch); batch = []; size = 0
            batch.append(path); size += len(path) + 1
        if batch: batches.append(batch)
        for idx, batch in enumerate(batches):
            with (logs / f'extract_{archive}_{idx:03}.log').open('w', encoding='utf-8') as log:
                subprocess.run([str(CLI), '-i', str(GAME / archive / 'pak01_dir.vpk'), '-f', ','.join(batch), '-o', str(raw), '--threads', '4'], stdout=log, stderr=subprocess.STDOUT, check=True)
            print('EXTRACT', archive, idx + 1, '/', len(batches), flush=True)
    failures = []
    for row in rows:
        p = raw / row['resource']
        if not p.exists() or p.stat().st_size != row['bytes'] or f'{zlib.crc32(p.read_bytes()) & 0xffffffff:08x}' != row['crc32']:
            failures.append(row['resource'])
    save(OUT / 'Metadata/raw_validation.json', {'checked': len(rows), 'failed': failures})
    assert not failures, f'{len(failures)} raw validation failures; see Metadata/raw_validation.json'
    # Decode from each archive as a batch: folder-mode --game would remount the
    # huge VPK for each individual input file in this CLI version.
    for archive in ['csgo', 'core']:
        batch = []; size = 0; batches = []
        for row in data['sounds']:
            if row['archive'] != archive: continue
            if size + len(row['resource']) > 16000:
                batches.append(batch); batch = []; size = 0
            batch.append(row['resource']); size += len(row['resource']) + 1
        if batch: batches.append(batch)
        for idx, batch in enumerate(batches):
            with (logs / f'decode_{archive}_{idx:03}.log').open('w', encoding='utf-8') as log:
                subprocess.run([str(CLI), '-i', str(GAME / archive / 'pak01_dir.vpk'), '-f', ','.join(batch),
                                '-o', str(OUT / 'Source'), '-d', '--threads', '4'],
                               stdout=log, stderr=subprocess.STDOUT, check=True)
            print('DECODE', archive, idx + 1, '/', len(batches), flush=True)
    print('EXTRACT_COMPLETE', len(data['sounds']), flush=True)


def convert():
    import aud
    import numpy as np
    data = read(OUT / 'export_plan.json')
    old = {r['source_resource']: r for r in read(ROOT / 'Batch/Audio/manifest.json')['sounds']}
    records = []; failures = []
    for row in data['sounds']:
        resource = row['resource']
        try:
            stem = resource.removesuffix('.vsnd_c')
            candidates = [OUT / 'Source' / (stem + ext) for ext in ['.wav', '.mp3', '.ogg', '.flac']]
            native = [p for p in candidates if p.is_file()]
            assert len(native) == 1, (resource, native)
            source = native[0]
            sound = aud.Sound(str(source)); rate, channels = sound.specs
            samples = sound.data()
            assert len(samples) and np.isfinite(samples).all(), 'Empty or invalid samples'
            dest = OUT / 'WAV' / (stem + '.wav')
            dest.parent.mkdir(parents=True, exist_ok=True)
            if resource in old:
                previous = ROOT / 'Batch/Audio' / old[resource]['wav']
                assert hashlib.sha256(previous.read_bytes()).hexdigest() == old[resource]['sha256']
                shutil.copy2(previous, dest)
                mode = 'Reused verified previous weapon WAV; validated against current archive decode'
            elif source.suffix == '.wav':
                shutil.copy2(source, dest)
                mode = 'Native WAV copied unchanged'
            else:
                payload = samples.astype('<f4').tobytes()
                fmt = struct.pack('<HHIIHH', 3, channels, round(rate), round(rate) * channels * 4, channels * 4, 32)
                chunks = b'fmt ' + struct.pack('<I', len(fmt)) + fmt + b'fact' + struct.pack('<II', 4, len(samples)) + b'data' + struct.pack('<I', len(payload)) + payload
                dest.write_bytes(b'RIFF' + struct.pack('<I', len(chunks) + 4) + b'WAVE' + chunks)
                mode = 'Decoded to float32 WAV; no clipping, resampling or normalization'
            decoded = aud.Sound(str(dest))
            assert decoded.specs == sound.specs
            check = decoded.data()
            assert samples.shape == check.shape
            error = float(np.max(np.abs(samples - check)))
            assert error < 0.0001, error
            records.append({'name': Path(stem).name, 'source_resource': resource, 'source_crc32': row['crc32'],
                            'native': source.relative_to(OUT).as_posix(), 'wav': dest.relative_to(OUT).as_posix(),
                            'sample_rate': round(rate), 'channels': channels, 'frames': len(samples),
                            'duration_seconds': round(len(samples) / rate, 6), 'peak_linear': float(np.max(np.abs(samples))),
                            'sha256': hashlib.sha256(dest.read_bytes()).hexdigest(), 'bytes': dest.stat().st_size,
                            'conversion': mode, 'roundtrip_max_error': error})
        except Exception as error:
            failures.append({'resource': resource, 'error': str(error)})
        if (len(records) + len(failures)) % 250 == 0:
            print('AUDIO', len(records), 'validated;', len(failures), 'failed', flush=True)
    save(OUT / 'manifest.json', {'count': len(records), 'sounds': records, 'failures': failures})
    print('CONVERT_COMPLETE', len(records), 'failed', len(failures), flush=True)
    assert not failures, failures


if __name__ == '__main__':
    import sys
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    parser = argparse.ArgumentParser()
    parser.add_argument('stage', choices=['plan', 'extract', 'convert'])
    globals()[parser.parse_args(args).stage]()
