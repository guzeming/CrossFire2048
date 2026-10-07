"""Build lobby-only models and shared animation clips, preserving all source blends.

Blender --background --python export_lobby_loadout.py -- --output <Unity Assets/Art/Loadout>
Use --only <id> for a single asset; completed records are resumable.
"""
import argparse
import hashlib
import json
import math
import sys
from urllib.parse import unquote
from pathlib import Path
import bpy
import bmesh
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from batch_common import bake_action, assign
from lobby_loadout_definitions import AGENTS, WEAPONS, PROFILES

parser = argparse.ArgumentParser()
parser.add_argument('--output', required=True)
parser.add_argument('--only')
parser.add_argument('--force', action='store_true', help='Rebuild selected assets even when a cached record exists')
parser.add_argument('--weapons-only', action='store_true')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
ROOT = Path(__file__).resolve().parents[1]
OUT = Path(args.output).resolve()
for directory in ('Characters', 'Weapons', 'Animations', 'Textures', 'Records'):
    (OUT / directory).mkdir(parents=True, exist_ok=True)
plan = {e['id']: e for e in json.loads((ROOT / 'Batch/plan.json').read_text())['models']}

def canonical():
    with bpy.data.libraries.load(str(ROOT / 'CT_SAS/ctm_sas_animated.blend'), link=False) as (src, dst):
        dst.objects = ['CT_SAS_Rig']
    rig = dst.objects[0]
    bpy.context.scene.collection.objects.link(rig)
    rig.name = 'LobbyRig'
    rig.animation_data_clear()
    rig.matrix_world = Matrix.Identity(4)
    for b in rig.pose.bones:
        b.matrix_basis = Matrix.Identity(4)
        b.rotation_mode = 'QUATERNION'
    return rig

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.context.scene.render.fps = 30

def wrap(rig, meshes):
    root = bpy.data.objects.new('LoadoutModel', None)
    bpy.context.scene.collection.objects.link(root)
    for o in [rig] + meshes:
        matrix = o.matrix_world.copy()
        o.parent = root
        o.matrix_world = matrix
    return root

def export(path, objects, animated=False):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:
        o.hide_set(False)
        o.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.export_scene.fbx(filepath=str(path), use_selection=True,
        object_types={'ARMATURE', 'MESH', 'EMPTY'}, add_leaf_bones=False,
        use_armature_deform_only=False, bake_anim=animated,
        bake_anim_use_nla_strips=False, bake_anim_use_all_actions=False,
        bake_anim_simplify_factor=0.0, path_mode='STRIP', axis_forward='-Z', axis_up='Y')

def visible_meshes():
    return [o for o in bpy.context.scene.objects if o.type == 'MESH'
            and not any(c.hide_render or c.name in ('FirstPerson_Alternatives', 'Legacy_Alternative') for c in o.users_collection)
            and 'firstperson_' not in o.name and not o.hide_render]

def image_nodes(tree):
    if not tree: return
    for node in tree.nodes:
        if node.type == 'TEX_IMAGE' and node.image: yield node
        elif node.type == 'GROUP': yield from image_nodes(node.node_tree)

def materials(meshes):
    result = []
    for mat in sorted({m for o in meshes for m in o.data.materials if m}, key=lambda m: m.name):
        item = {'name': mat.name, 'color': '', 'normal': '', 'orm': ''}
        for node in image_nodes(mat.node_tree):
            image = node.image
            data = image.packed_file.data if image.packed_file else Path(bpy.path.abspath(image.filepath)).read_bytes()
            name = hashlib.sha256(data).hexdigest()[:20] + '.png'
            path = OUT / 'Textures' / name
            if not path.exists(): path.write_bytes(data)
            lowered = image.name.lower()
            channel = 'orm' if 'orm' in lowered else 'normal' if 'normal' in lowered else 'color'
            item[channel] = name
        result.append(item)
    return result

def import_source(profile, inspect=False):
    team, idle, look = PROFILES[profile]
    before = set(bpy.data.objects)
    name = look if inspect else idle
    path = ROOT / f'Batch/RawAnimations/animation/anims/ui_anims/main_menu/{team}/{name}.gltf'
    bpy.ops.import_scene.gltf(filepath=str(path))
    added = set(bpy.data.objects) - before
    rigs = [o for o in added if o.type == 'ARMATURE']
    character = max(rigs, key=lambda o: len(o.data.bones))
    weapons = [o for o in rigs if o != character]
    return character, weapons, added

def build_agent(id, name, team):
    entry = plan[id]
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'Batch' / entry['relative_dir'] / (id + '.blend')))
    scene = bpy.context.scene
    meshes = visible_meshes()
    old = max((o for o in scene.objects if o.type == 'ARMATURE'), key=lambda o: len(o.data.bones))
    old.animation_data_clear()
    for b in old.pose.bones: b.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    rig = canonical()
    remaps = {}
    for mesh in meshes:
        for group in list(mesh.vertex_groups):
            if group.name in rig.data.bones: continue
            bone = old.data.bones.get(group.name)
            while bone and bone.name not in rig.data.bones: bone = bone.parent
            if not bone: continue
            remaps[group.name] = bone.name
            destination = mesh.vertex_groups.get(bone.name) or mesh.vertex_groups.new(name=bone.name)
            for vertex in mesh.data.vertices:
                for weight in vertex.groups:
                    if weight.group == group.index:
                        destination.add([vertex.index], weight.weight, 'ADD')
                        break
            mesh.vertex_groups.remove(group)
        for modifier in mesh.modifiers:
            if modifier.type == 'ARMATURE': modifier.object = rig
    root = wrap(rig, meshes)
    material_list = materials(meshes)
    export(OUT / 'Characters' / (id + '.fbx'), [root, rig] + meshes)
    return {'id': id, 'name': name, 'team': team, 'materials': material_list, 'boneRemaps': remaps}

def build_profile(id):
    reset()
    rig = canonical()
    root = wrap(rig, [])
    action = bpy.data.actions.new('LobbySequence')
    slot = action.slots.new('OBJECT', rig.name)
    bag = action.layers.new('Menu actions').strips.new(type='KEYFRAME').channelbag(slot, ensure=True)
    curves = {}
    start = 1
    clips = []
    for inspecting in (False, True):
        source, _, imported = import_source(id, inspecting)
        baked = bake_action(rig, source, 'Baked', *source.animation_data.action.frame_range)
        for layer in baked.layers:
            for strip in layer.strips:
                for source_bag in strip.channelbags:
                    for curve in source_bag.fcurves:
                        key = (curve.data_path, curve.array_index)
                        if key not in curves: curves[key] = bag.fcurves.new(data_path=key[0], index=key[1])
                        for point in curve.keyframe_points:
                            k = curves[key].keyframe_points.insert(point.co.x + start - 1, point.co.y, options={'FAST'})
                            k.interpolation = 'LINEAR'
        count = int(baked.frame_end)
        clips.append({'name': 'Inspect' if inspecting else 'Idle', 'first': start, 'last': start + count - 1})
        start += count
        for obj in imported: bpy.data.objects.remove(obj, do_unlink=True)
    for curve in curves.values(): curve.update()
    assign(rig, action)
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, start - 1
    bpy.context.scene.frame_set(1)
    export(OUT / 'Animations' / (id + '.fbx'), [root, rig], True)
    return {'id': id, 'clips': clips}

def build_weapon(id, name, category, slot, team, profile):
    entry = plan[id]
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'Batch' / entry['relative_dir'] / (id + '.blend')))
    target_weapon_rest = None
    target_hand_rest = None
    for o in list(bpy.context.scene.objects):
        if o.type == 'ARMATURE':
            if 'weapon' in o.data.bones:
                target_weapon_rest = o.matrix_world @ o.data.bones['weapon'].matrix_local
            if 'weapon_hand_r' in o.data.bones:
                target_hand_rest = o.matrix_world @ o.data.bones['weapon_hand_r'].matrix_local
            o.animation_data_clear()
            for b in o.pose.bones: b.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    original = visible_meshes()
    material_list = materials(original)
    meshes = []
    for o in original:
        evaluated = o.evaluated_get(bpy.context.evaluated_depsgraph_get())
        mesh = bpy.data.meshes.new_from_object(evaluated)
        if id == 'weapon_molotov':
            # The exported world model also includes a detached lighter; the lobby holds the bottle.
            discard = {v.index for v in o.data.vertices if any(o.vertex_groups[w.group].name.startswith('lighter') and w.weight > .5 for w in v.groups)}
            editable = bmesh.new(); editable.from_mesh(mesh); editable.verts.ensure_lookup_table()
            bmesh.ops.delete(editable, geom=[v for v in editable.verts if v.index in discard], context='VERTS')
            editable.to_mesh(mesh); editable.free()
            if not mesh.vertices: continue
        copy = bpy.data.objects.new('WeaponMesh' + str(len(meshes)), mesh)
        bpy.context.scene.collection.objects.link(copy)
        copy.matrix_world = o.matrix_world.copy()
        meshes.append(copy)
    rig = canonical()
    root = wrap(rig, [])
    mount = bpy.data.objects.new('WeaponMount', None)
    bpy.context.scene.collection.objects.link(mount)
    # Equipment without a held pose is available in the inspect viewer.
    held = bool(profile)
    if held:
        source, source_weapons, imported = import_source(profile)
        bpy.context.scene.frame_set(0)
        bpy.context.view_layer.update()
        source_hand = source.matrix_world @ source.pose.bones['hand_R'].matrix
        rest_hand = source.matrix_world @ source.data.bones['hand_R'].matrix_local
        hand = source_hand @ rest_hand.inverted() @ rig.data.bones['hand_R'].matrix_local
        if id == 'weapon_molotov':
            # This menu clip leaves wpn at the world origin; use its authored grip bone instead.
            delta = source_hand @ target_hand_rest.inverted()
        elif source_weapons:
            source_weapon = source_weapons[0]
            bone = source_weapon.pose.bones.get('weapon') or next(iter(source_weapon.pose.bones))
            # Weapon animation skeletons are local to the character's wpn attachment.
            delta = source.matrix_world @ source.pose.bones['wpn'].matrix @ source_weapon.matrix_world @ bone.matrix @ (
                source_weapon.matrix_world @ bone.bone.matrix_local).inverted() @ (target_weapon_rest or Matrix.Identity(4)).inverted()
        else:
            delta = source.matrix_world @ source.pose.bones['wpn'].matrix @ (target_weapon_rest or Matrix.Identity(4)).inverted()
        offset = hand.inverted() @ delta
        mount.parent = rig
        mount.parent_type = 'BONE'
        mount.parent_bone = 'hand_R'
        bpy.context.view_layer.update()
        mount.matrix_world = rig.data.bones['hand_R'].matrix_local @ offset
        for obj in imported: bpy.data.objects.remove(obj, do_unlink=True)
    else:
        mount.parent = root
    for mesh in meshes:
        original_world = mesh.matrix_world.copy()
        mesh.parent = mount
        mesh.matrix_parent_inverse = Matrix.Identity(4)
        mesh.matrix_basis = original_world
    bpy.context.view_layer.update()
    export(OUT / 'Weapons' / (id + '.fbx'), [root, rig, mount] + meshes)
    return {'id': id, 'name': name, 'category': category, 'slot': slot, 'team': team,
            'profile': profile, 'held': held, 'materials': material_list}

def build_dual_pistols(id, name, category, slot, team, profile):
    entry = plan[id]
    bpy.ops.wm.open_mainfile(filepath=str(ROOT / 'Batch' / entry['relative_dir'] / (id + '.blend')))
    target = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    target.animation_data_clear()
    for bone in target.pose.bones: bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    originals = [o for o in visible_meshes() if 'eholster' not in o.name]
    material_list = materials(originals)
    grips = {side: target.matrix_world @ target.data.bones['weapon_hand_' + side].matrix_local for side in ('r', 'l')}
    halves = {'r': [], 'l': []}
    for original in originals:
        for side in ('r', 'l'):
            keep = {v.index for v in original.data.vertices if any(
                original.vertex_groups[w.group].name.endswith('_' + side) and w.weight > .5 for w in v.groups)}
            mesh = bpy.data.meshes.new_from_object(original.evaluated_get(bpy.context.evaluated_depsgraph_get()))
            editable = bmesh.new(); editable.from_mesh(mesh); editable.verts.ensure_lookup_table()
            bmesh.ops.delete(editable, geom=[v for v in editable.verts if v.index not in keep], context='VERTS')
            editable.to_mesh(mesh); editable.free()
            if not mesh.vertices: continue
            obj = bpy.data.objects.new('WeaponMesh_' + side, mesh)
            bpy.context.scene.collection.objects.link(obj)
            obj.matrix_world = original.matrix_world.copy()
            halves[side].append(obj)
    rig = canonical(); root = wrap(rig, [])
    source, weapons, imported = import_source(profile)
    bpy.context.scene.frame_set(0); bpy.context.view_layer.update()
    source_weapon = weapons[0]
    mounts = []; markers = []
    for side in ('r', 'l'):
        bone_name = 'hand_' + side.upper()
        hand = source.matrix_world @ source.pose.bones[bone_name].matrix @ (
            source.matrix_world @ source.data.bones[bone_name].matrix_local).inverted() @ rig.data.bones[bone_name].matrix_local
        # Apply the source grip's deformation to neutral geometry; imported model bone axes can differ.
        grip_name = 'weapon_hand_' + side
        delta = source.matrix_world @ source.pose.bones['wpn'].matrix @ source_weapon.data.bones['weapon'].matrix_local.inverted() @ source_weapon.pose.bones[grip_name].matrix @ source_weapon.data.bones[grip_name].matrix_local.inverted()
        delta.translation += (source.matrix_world @ source.pose.bones[bone_name].matrix).translation - (delta @ grips[side]).translation
        # Neutral Elite variants differ in handedness. Keep relaxed pistols pointing below the wrist.
        if (delta.to_3x3() @ Vector((0, -1, 0))).z > 0:
            pivot = (source.matrix_world @ source.pose.bones[bone_name].matrix).translation
            delta = Matrix.Translation(pivot) @ Matrix.Rotation(math.pi, 4, 'X') @ Matrix.Translation(-pivot) @ delta
        mount = bpy.data.objects.new('WeaponMount' if side == 'r' else 'WeaponMountLeft', None)
        bpy.context.scene.collection.objects.link(mount)
        mount.parent = rig; mount.parent_type = 'BONE'; mount.parent_bone = bone_name
        bpy.context.view_layer.update()
        mount.matrix_world = rig.data.bones[bone_name].matrix_local @ hand.inverted() @ delta
        for mesh in halves[side]:
            neutral = mesh.matrix_world.copy(); mesh.parent = mount
            mesh.matrix_parent_inverse = Matrix.Identity(4); mesh.matrix_basis = neutral
        marker = bpy.data.objects.new('Grip_' + side.upper(), None)
        bpy.context.scene.collection.objects.link(marker)
        marker.parent = mount; marker.matrix_basis = grips[side]
        markers.append(marker)
        mounts.append(mount)
    for obj in imported: bpy.data.objects.remove(obj, do_unlink=True)
    bpy.context.view_layer.update()
    export(OUT / 'Weapons' / (id + '.fbx'), [root, rig] + mounts + markers + halves['r'] + halves['l'])
    return {'id': id, 'name': name, 'category': category, 'slot': slot, 'team': team,
            'profile': profile, 'held': True, 'materials': material_list}

for kind, entries, builder in (('agents', AGENTS, build_agent), ('profiles', [(p,) for p in PROFILES], build_profile),
                                ('weapons', WEAPONS, build_weapon)):
    if args.weapons_only and kind != 'weapons': continue
    for entry in entries:
        id = entry[0]
        if args.only and args.only != id: continue
        record = OUT / 'Records' / (id + '.json')
        if record.exists() and not args.force: continue
        value = build_dual_pistols(*entry) if id == 'weapon_pist_elite' else builder(*entry)
        value['kind'] = kind
        record.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')
        print('LOADOUT_EXPORTED', kind, id, flush=True)

manifest = {'agents': [], 'weapons': [], 'profiles': []}
for record in sorted((OUT / 'Records').glob('*.json')):
    item = json.loads(record.read_text(encoding='utf-8'))
    # Packed preview node groups sometimes omit the normal map. The exported
    # glTF material table is the authoritative texture mapping for Unity.
    if item['kind'] != 'profiles':
        source_path = ROOT / 'Batch' / plan[item['id']]['gltf']
        if not source_path.exists():
            source_path = ROOT / {'ctm_sas': 'CT_SAS/ctm_sas.gltf',
                                  'weapon_rif_m4a1_silencer': 'M4A1_S/m4a1_s.gltf'}[item['id']]
        document = json.loads(source_path.read_text(encoding='utf-8'))
        source_materials = {m['name']: m for m in document.get('materials', [])}
        for material in item['materials']:
            source = source_materials.get(material['name'])
            if not source: continue
            pbr = source.get('pbrMetallicRoughness', {})
            for channel, binding in (('color', pbr.get('baseColorTexture')), ('normal', source.get('normalTexture')),
                                     ('orm', pbr.get('metallicRoughnessTexture'))):
                if not binding: continue
                texture = document['textures'][binding['index']]
                path = source_path.parent / unquote(document['images'][texture['source']]['uri'])
                data = path.read_bytes()
                filename = hashlib.sha256(data).hexdigest()[:20] + '.png'
                destination = OUT / 'Textures' / filename
                if not destination.exists(): destination.write_bytes(data)
                material[channel] = filename
        record.write_text(json.dumps(item, indent=2, ensure_ascii=False), encoding='utf-8')
    manifest[item.pop('kind')].append(item)
(OUT / 'manifest.json').write_text(json.dumps(manifest, indent=2, ensure_ascii=False), encoding='utf-8')
print('LOADOUT_EXPORT_COMPLETE', {k: len(v) for k, v in manifest.items()}, flush=True)
