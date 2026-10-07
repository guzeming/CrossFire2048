"""Select existing CS exports for Unity. No game installation or external download needed."""
import hashlib
import json
import shutil
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
SOURCE = ROOT / 'ArtSource/CS2Imports'
OUT = ROOT / 'Assets/Art/Throwables'
OUT.mkdir(parents=True, exist_ok=True)
records = []

def record(src, dst):
    records.append({'source': src.relative_to(ROOT).as_posix(), 'asset': dst.relative_to(ROOT).as_posix(),
                    'sha256': hashlib.sha256(src.read_bytes()).hexdigest()})

# Preserve alpha and the sequence order, letterbox frames to avoid distorting cropped exports.
# Each atlas is a 4x4 resampling of sequence 0; playback timing is authored for this game.
sequences = json.loads((SOURCE / 'CombatFX/Metadata/sprite_sequences.json').read_text())
for label, suffix in [
    ('Fire', 'particle/fire_gas/fire_gas_batch_b_top_v2.mks'),
    ('Explosion', 'particle/fire/fire_burst/fire_burst.mks'),
    ('Smoke', 'particle/smoke/smokeburst/smokeloop_i_1.mks'),
]:
    seq = next(s for s in sequences if s['file'].endswith(suffix))['sequences'][0]['frames']
    atlas = Image.new('RGBA', (1024, 1024))
    dst = OUT / 'Textures' / (label + '.png')
    dst.parent.mkdir(parents=True, exist_ok=True)
    for i in range(16):
        src = SOURCE / 'CombatFX' / seq[round(i * (len(seq)-1)/15)]['file']
        frame = Image.open(src).convert('RGBA')
        frame.thumbnail((248, 248), Image.Resampling.LANCZOS)
        atlas.paste(frame, ((i % 4)*256+(256-frame.width)//2, (i//4)*256+(256-frame.height)//2))
        record(src, dst)
    atlas.save(dst)

audio = {
    'hegrenade': ['pinpull.wav', 'grenade_throw.wav', 'he_bounce-1.wav', 'hegrenade_detonate_02.wav', 'hegrenade_detonate_03.wav'],
    'flashbang': ['flashbang_explode1.wav', 'flashbang_explode2.wav', 'explosion_ring_loop.wav'],
    'smokegrenade': ['smoke_emit.wav', 'smoke_clear.wav', 'grenade_hit1.wav'],
    'molotov': ['molotov_detonate_1.wav', 'molotov_smash_01.wav', 'molotov_throw_01.wav', 'molotov_throw_fire_01.wav', 'fire_loop_1.wav', 'molotov_extinguish.wav'],
    'incgrenade': ['inc_grenade_detonate_1.wav', 'inc_grenade_bounce_m.wav'],
}
for folder, names in audio.items():
    for name in names:
        src = SOURCE / 'CombatAudio/WAV/sounds/weapons' / folder / name
        dst = OUT / 'Audio' / folder / name
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        record(src, dst)
(OUT / 'sources.json').write_text(json.dumps({'notes': 'CS exported textures/audio; Unity behavior and particle timings are authored locally.', 'files': records}, indent=2), encoding='utf-8')
print(f'THROWABLE_ASSETS_READY: {len(records)} source references')
