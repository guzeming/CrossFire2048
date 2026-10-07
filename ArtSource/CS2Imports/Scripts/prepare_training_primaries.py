"""Stage the remaining primary guns from existing local audio exports.
Values below are authored training balance, not competitive-game statistics.
"""
import hashlib
import json
import shutil
from pathlib import Path

root = Path(__file__).resolve().parents[3]
source = root / 'ArtSource/CS2Imports/CombatAudio/WAV/sounds/weapons'
art = root / 'Assets/Art/Combat'
# category, profile, capacity, cycle, damage per pellet, reload, pellets, cone half-angle
profiles = [
    ('rif', 'aug', 30, .09, 28, 3.3, 1, 0),
    ('rif', 'famas', 25, .09, 30, 3.3, 1, 0),
    ('rif', 'galilar', 35, .09, 30, 3.0, 1, 0),
    ('rif', 'sg556', 30, .10, 30, 2.8, 1, 0),
    ('smg', 'bizon', 64, .08, 27, 2.4, 1, 0),
    ('smg', 'mac10', 30, .075, 29, 2.6, 1, 0),
    ('smg', 'mp5sd', 30, .08, 27, 3.0, 1, 0),
    ('smg', 'mp7', 30, .08, 29, 3.1, 1, 0),
    ('smg', 'mp9', 30, .07, 26, 2.1, 1, 0),
    ('smg', 'p90', 50, .07, 26, 3.4, 1, 0),
    ('smg', 'ump45', 25, .09, 35, 3.4, 1, 0),
    ('shot', 'mag7', 5, .85, 30, 2.4, 8, 3.5),
    ('shot', 'nova', 8, .88, 26, 4.5, 9, 4.0),
    ('shot', 'sawedoff', 7, .85, 32, 4.0, 8, 5.0),
    ('shot', 'xm1014', 7, .35, 20, 4.0, 6, 3.5),
    ('mach', 'm249', 100, .08, 32, 5.7, 1, 0),
    ('mach', 'negev', 150, .075, 35, 5.7, 1, 0),
]
foley = {
    'aug': ['aug_clipout', 'aug_clipin', 'aug_boltpull', 'aug_boltrelease'],
    'famas': ['famas_clipout', 'famas_clipin', 'famas_boltback', 'famas_boltforward'],
    'galilar': ['galil_clipout', 'galil_clipin', 'galil_boltback', 'galil_boltforward'],
    'sg556': ['sg556_clipout', 'sg556_clipin', 'sg556_boltback', 'sg556_boltforward'],
    'mag7': ['mag7_clipout', 'mag7_clipin', 'mag7_pump_back', 'mag7_pump_forward'],
    'nova': ['nova_insertshell_01', 'nova_insertshell_02', 'nova_insertshell_03', 'nova_pump'],
    'sawedoff': ['sawedoff_insertshell_01', 'sawedoff_insertshell_02', 'sawedoff_insertshell_03', 'sawedoff_pump'],
    'xm1014': ['xm1014_insertshell_01', 'xm1014_insertshell_02', 'xm1014_insertshell_03', 'xm1014_catch_03'],
}
records, bindings = [], []
def copy(folder, name, destination):
    src, dst = source / folder / (name + '.wav'), art / destination / (name + '.wav')
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    records.append(dict(source=src.relative_to(root).as_posix(), asset=dst.relative_to(root).as_posix(),
                        sha256=hashlib.sha256(src.read_bytes()).hexdigest()))

path = art / 'weapon_data.json'
data = json.loads(path.read_text())
ids = {'weapon_' + category + '_' + name for category, name, *_ in profiles}
data['weapons'] = [w for w in data['weapons'] if w['id'] not in ids]
for category, name, capacity, cycle, damage, duration, pellets, spread in profiles:
    weapon_id = 'weapon_' + category + '_' + name
    folder = 'mp5' if name == 'mp5sd' else name
    sound = 'galil' if name == 'galilar' else folder
    shots = [p.stem for p in sorted((source / folder).glob(sound + '_0[1-4].wav'))]
    if not shots:
        shots = [sound + '-1']
    if name in ('m249', 'negev'):
        cues = [name + suffix for suffix in ('_coverup', '_boxout', '_boxin', '_chain', '_coverdown', '_pump')]
    else:
        action = 'slide' if name in ('mp5sd', 'mp7') else 'bolt'
        cues = foley.get(name, [sound + '_clipout', sound + '_clipin', sound + '_' + action + 'back', sound + '_' + action + 'forward'])
    times = [.12, .28, .48, .64, .78, .9] if len(cues) == 6 else [.12, .53, .76, .87]
    if name in ('nova', 'sawedoff', 'xm1014'):
        times = [.2, .4, .6, .87]
    data['weapons'].append(dict(id=weapon_id, magazineSize=capacity, reserveAmmo=capacity * 3,
        cycleTime=cycle, damage=damage, reloadDuration=duration, range=80 if pellets > 1 else 200,
        rangeModifier=.7 if pellets > 1 else .85 if category == 'smg' else .97,
        automatic=category != 'shot' or name == 'xm1014', silenced=name == 'mp5sd',
        pelletCount=pellets, spreadAngle=spread))
    for shot in shots:
        copy(folder, shot, 'Audio/' + name)
    for cue in cues:
        copy(folder, cue, 'ReloadAudio')
    bindings.append(dict(id=weapon_id, profile=name, sounds=cues, times=times))
path.write_text(json.dumps(data, indent=2) + '\n', encoding='utf-8')
(art / 'primary_sources.json').write_text(json.dumps(dict(weapons=bindings, files=records), indent=2) + '\n', encoding='utf-8')
print('PRIMARY_ASSETS_READY', len(profiles), 'weapons,', len(records), 'audio references')
