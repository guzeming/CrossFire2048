"""Export the existing equipped SAS as a small, independently rebuildable Unity lobby asset."""
import json
import sys
from pathlib import Path
import bpy
from mathutils import Matrix

sys.path.insert(0, str(Path(__file__).resolve().parent))
from batch_common import assign, bake_action

root = Path(__file__).resolve().parents[1]
output = root / 'CT_SAS' / 'Lobby'
textures = output / 'Textures'
textures.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(root / 'CT_SAS' / 'ctm_sas_m4a1_s.blend'))
scene = bpy.context.scene
character = bpy.data.objects['CT_SAS_Rig']
weapon = next(o for o in scene.objects if o.type == 'ARMATURE' and o != character)
character.animation_data.action = bpy.data.actions['idle_rifle']
character.animation_data.action_slot = character.animation_data.action.slots[0]
weapon.animation_data_clear()
for bone in weapon.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
scene.render.fps = 30
scene.render.fps_base = 1
scene.frame_set(1)
bpy.context.view_layer.update()

# Keep the M4A1-S grip from the equipped source. The menu clips use an M4A4
# weapon skeleton with a different attachment origin; retargeting that origin
# directly would put the M4A1-S outside the hands.
weapon_from_hand = character.pose.bones['hand_R'].matrix.inverted() @ character.pose.bones['wpn'].matrix
source_folder = root / 'Batch/RawAnimations/animation/anims/ui_anims/main_menu/ct'
clips = []
sequence = bpy.data.actions.new('LobbyRifleSequence')
slot = sequence.slots.new('OBJECT', character.name)
bag = sequence.layers.new('Lobby idle and inspect').strips.new(type='KEYFRAME').channelbag(slot, ensure=True)
sequence_curves = {}
first_frame = 1
for name, source_name, looping in (
        ('RifleIdle', 'ct_main_menu_rifle_idle_handrepo_m4', True),
        ('RifleInspect', 'ct_main_menu_rifle_lookat_handrepo_m4', False)):
    before_objects, before_actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.gltf(filepath=str(source_folder / (source_name + '.gltf')))
    imported = set(bpy.data.objects) - before_objects
    imported_actions = set(bpy.data.actions) - before_actions
    source = max((o for o in imported if o.type == 'ARMATURE'),
                 key=lambda o: len(set(o.data.bones.keys()) & set(character.data.bones.keys())))
    action = bake_action(character, source, name, *source.animation_data.action.frame_range)
    for layer in action.layers:
        for strip in layer.strips:
            for source_bag in strip.channelbags:
                for curve in source_bag.fcurves:
                    key = (curve.data_path, curve.array_index)
                    if key not in sequence_curves:
                        sequence_curves[key] = bag.fcurves.new(data_path=key[0], index=key[1])
                    destination = sequence_curves[key]
                    for point in curve.keyframe_points:
                        destination.keyframe_points.insert(point.co.x + first_frame - 1, point.co.y, options={'FAST'})
    count = int(action.frame_end)
    clips.append({'name': name, 'source': str((source_folder / (source_name + '.gltf')).relative_to(root)),
                  'first_frame': first_frame, 'last_frame': first_frame + count - 1,
                  'duration_seconds': (count - 1) / scene.render.fps, 'loop': looping})
    first_frame += count
    for obj in imported:
        bpy.data.objects.remove(obj, do_unlink=True)
    for imported_action in imported_actions | {action}:
        bpy.data.actions.remove(imported_action)

for curve in sequence_curves.values():
    curve.update()
sequence.use_frame_range = True
scene.frame_start, scene.frame_end = 1, first_frame - 1
sequence.frame_start, sequence.frame_end = scene.frame_start, scene.frame_end
assign(character, sequence)
previous_rotation = None
for frame in range(scene.frame_start, scene.frame_end + 1):
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    bone = character.pose.bones['wpn']
    bone.matrix = character.pose.bones['hand_R'].matrix @ weapon_from_hand
    if previous_rotation and previous_rotation.dot(bone.rotation_quaternion) < 0:
        bone.rotation_quaternion.negate()
    previous_rotation = bone.rotation_quaternion.copy()
    for channel in ('location', 'rotation_quaternion', 'scale'):
        bone.keyframe_insert(data_path=channel, frame=frame, group='wpn')

for layer in sequence.layers:
    for strip in layer.strips:
        for bag in strip.channelbags:
            for curve in bag.fcurves:
                for key in curve.keyframe_points:
                    key.interpolation = 'LINEAR'
scene.frame_set(scene.frame_start)
bpy.context.view_layer.update()

meshes = [o for o in scene.objects if o.type == 'MESH' and
          any(m.type == 'ARMATURE' and m.object in (character, weapon) for m in o.modifiers) and
          not any(c.name in ('FirstPerson_Alternatives', 'Legacy_Alternative') for c in o.users_collection)]
materials = set(m for o in meshes for m in o.data.materials if m)
records = []
source_normals = {}
for source_dir, gltf_name in [('CT_SAS', 'ctm_sas.gltf'), ('M4A1_S', 'm4a1_s.gltf')]:
    document = json.loads((root / source_dir / gltf_name).read_text(encoding='utf-8'))
    for source_material in document['materials']:
        normal_path = source_material.get('extras', {}).get('vmat', {}).get('TextureParams', {}).get('g_tNormal')
        if normal_path:
            source_normals[source_material['name']] = root / source_dir / (Path(normal_path).stem + '.png')
def image_nodes(tree):
    for node in tree.nodes:
        if node.type == 'TEX_IMAGE':
            yield node
        elif node.type == 'GROUP' and node.node_tree:
            yield from image_nodes(node.node_tree)

for material in sorted(materials, key=lambda m: m.name):
    record = {'name': material.name, 'color': '', 'normal': '', 'orm': ''}
    for node in image_nodes(material.node_tree):
        if node.type != 'TEX_IMAGE' or not node.image:
            continue
        image = node.image
        filename = Path(image.name).stem + '.png'
        destination = textures / filename
        if image.packed_file:
            destination.write_bytes(image.packed_file.data)
        else:
            destination.write_bytes(Path(bpy.path.abspath(image.filepath)).read_bytes())
        lowered = image.name.lower()
        kind = 'orm' if 'orm' in lowered else 'normal' if 'normal' in lowered else 'color'
        record[kind] = filename
    normal = source_normals.get(material.name)
    if normal and normal.exists():
        (textures / normal.name).write_bytes(normal.read_bytes())
        record['normal'] = normal.name
    records.append(record)

bpy.ops.object.select_all(action='DESELECT')
selected = set(meshes + [character, weapon])
for obj in list(selected):
    parent = obj.parent
    while parent:
        selected.add(parent)
        parent = parent.parent
for obj in selected:
    obj.hide_set(False)
    obj.select_set(True)
bpy.context.view_layer.objects.active = character
bpy.ops.export_scene.fbx(filepath=str(output / 'CT_SAS_Lobby.fbx'), use_selection=True,
    object_types={'ARMATURE', 'MESH', 'EMPTY'}, add_leaf_bones=False,
    use_armature_deform_only=False, bake_anim=True, bake_anim_use_nla_strips=False,
    bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.0, path_mode='STRIP', embed_textures=False,
    axis_forward='-Z', axis_up='Y')
(output / 'materials.json').write_text(json.dumps({'materials': records}, indent=2), encoding='utf-8')
(output / 'export-report.json').write_text(json.dumps({
    'source': 'CT_SAS/ctm_sas_m4a1_s.blend', 'pose': 'idle_rifle',
    'animation': sequence.name, 'fps': scene.render.fps, 'clips': clips,
    'first_frame': scene.frame_start, 'last_frame': scene.frame_end,
    'duration_seconds': (scene.frame_end - scene.frame_start) / scene.render.fps,
    'meshes': [o.name for o in meshes], 'materials': len(records),
    'vertices': sum(len(o.data.vertices) for o in meshes),
    'note': 'Paired original CT main-menu M4 idle and weapon inspection, retargeted to SAS. M4A1-S attachment preserves the equipped right-hand grip. Unity imports separate clips and crossfades between them. No source blend modified.'
}, indent=2), encoding='utf-8')
print('LOBBY_CT_EXPORT_OK', flush=True)
