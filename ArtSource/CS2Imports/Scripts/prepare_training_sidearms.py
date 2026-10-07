"""Stage existing pistol/knife audio and authored training combat profiles."""
import hashlib
import json
import shutil
from pathlib import Path

root = Path(__file__).resolve().parents[3]
source = root / 'ArtSource/CS2Imports/CombatAudio/WAV/sounds/weapons'
art = root / 'Assets/Art/Combat'
records = []

def copy(folder, name, destination):
    src = source / folder / (name + '.wav')
    dst = art / destination / (name + '.wav')
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    records.append({'source': src.relative_to(root).as_posix(), 'asset': dst.relative_to(root).as_posix(),
                    'sha256': hashlib.sha256(src.read_bytes()).hexdigest()})

# Values are training balance, independently authored from the animation lengths.
profiles = [
    ('glock18', 20, .15, 30, 2.3, 'glock18', ['glock_01', 'glock_02'], ['glock_clipout', 'glock_clipin', 'glock_sliderelease']),
    ('hkp2000', 13, .17, 35, 2.2, 'hkp2000', ['hkp2000_01', 'hkp2000_02'], ['hkp2000_clipout', 'hkp2000_clipin', 'hkp2000_sliderelease']),
    ('usp_silencer', 12, .17, 35, 2.2, 'usp', ['usp_01', 'usp_02'], ['usp_clipout', 'usp_clipin', 'usp_sliderelease']),
    ('p250', 13, .15, 38, 2.2, 'p250', ['p250_01'], ['p250_clipout', 'p250_clipin', 'p250_sliderelease']),
    ('fiveseven', 20, .15, 32, 2.2, 'fiveseven', ['fiveseven_01'], ['fiveseven_clipout', 'fiveseven_clipin', 'fiveseven_sliderelease']),
    ('tec9', 18, .12, 33, 2.5, 'tec9', ['tec9_02'], ['tec9_clipout', 'tec9_clipin', 'tec9_boltrelease']),
    ('cz75a', 12, .10, 31, 2.7, 'cz75a', ['cz75_01', 'cz75_02'], ['cz75_clipin_01', 'cz75_addammo_01']),
    ('deagle', 7, .25, 53, 2.3, 'deagle', ['deagle_01', 'deagle_02'], ['de_clipout', 'de_clipin', 'de_slideforward']),
    ('revolver', 8, .4, 86, 2.3, 'revolver', ['revolver-1_01'], ['revolver_clipout', 'revolver_clipin', 'revolver_siderelease']),
    ('elite', 30, .12, 38, 3.8, 'elite', ['elites_01', 'elites_02'], ['elite_clipout', 'elite_leftclipin', 'elite_rightclipin', 'elite_sliderelease']),
    ('taser', 1, 1.0, 120, 5, 'taser', ['taser_shoot'], ['taser_charging', 'taser_charge_ready']),
]
path = art / 'weapon_data.json'
data = json.loads(path.read_text())
data['weapons'] = [w for w in data['weapons'] if not w['id'].startswith(('weapon_pist_', 'weapon_knife_'))]
for name, capacity, cycle, damage, duration, folder, shots, cues in profiles:
    weapon_id = 'weapon_pist_' + name
    data['weapons'].append(dict(id=weapon_id, automatic=name == 'cz75a', silenced=name == 'usp_silencer',
        magazineSize=capacity, reserveAmmo=capacity * 4, reloadDuration=duration, range=3 if name == 'taser' else 150,
        cycleTime=cycle, damage=damage, rangeModifier=1 if name == 'taser' else .9))
    for sound in shots:
        copy(folder, sound, 'Audio/' + name)
    for sound in cues:
        copy(folder, sound, 'ReloadAudio')
for team in ('ct', 't'):
    data['weapons'].append(dict(id='weapon_knife_default_' + team, cycleTime=.65, range=1.65, damage=35, rangeModifier=1))
for sound in ['knife_slash1', 'knife_slash2', 'knife_stab', 'knife_hit1', 'knife_hit2']:
    copy('knife', sound, 'MeleeAudio')
path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
(art / 'sidearm_sources.json').write_text(json.dumps({'notes': 'Existing third-person audio; authored training balance and foley timing.', 'files': records}, indent=2), encoding='utf-8')
print('SIDEARM_ASSETS_READY', len(profiles), 'pistols, 2 knives,', len(records), 'audio references')
