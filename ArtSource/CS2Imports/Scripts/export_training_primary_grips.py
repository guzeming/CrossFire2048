"""Calibrate world-animation mounts independently of the lobby's relaxed poses."""
import json
from pathlib import Path
import bpy
from mathutils import Matrix

root = Path(__file__).resolve().parents[1]
project = root.parents[1]
profiles = json.loads((project / 'Assets/Art/Combat/primary_sources.json').read_text())['weapons']
bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(str(root / 'CT_SAS/ctm_sas_animated.blend'), link=False) as (src, dst):
    dst.objects = ['CT_SAS_Rig']
rig = dst.objects[0]
bpy.context.scene.collection.objects.link(rig)
rig.name = 'LobbyRig'; rig.animation_data_clear(); rig.matrix_world = Matrix.Identity(4)
for bone in rig.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
parent = bpy.data.objects.new('LoadoutModel', None)
bpy.context.scene.collection.objects.link(parent); rig.parent = parent
mounts = []
for entry in profiles:
    weapon_id, profile = entry['id'], entry['profile']
    with bpy.data.libraries.load(str(root / f'Batch/Weapons/{weapon_id}/{weapon_id}.blend'), link=False) as (src, dst):
        dst.objects = src.objects
    objects = list(dst.objects)
    weapon = next(o for o in objects if o.type == 'ARMATURE' and 'weapon' in o.data.bones)
    weapon_rest = weapon.matrix_world @ weapon.data.bones['weapon'].matrix_local
    for obj in objects: bpy.data.objects.remove(obj, do_unlink=True)
    before = set(bpy.data.objects)
    bpy.ops.import_scene.gltf(filepath=str(root / f'Batch/RawAnimations/animation/anims/world/rifle/rifle_{profile}/idle_{profile}.vnmclip+non_additive.gltf'))
    imported = set(bpy.data.objects) - before
    source = next(o for o in imported if o.type == 'ARMATURE' and 'hand_R' in o.data.bones)
    bpy.context.scene.frame_set(round(source.animation_data.action.frame_range[0])); bpy.context.view_layer.update()
    hand = source.matrix_world @ source.pose.bones['hand_R'].matrix @ (
        source.matrix_world @ source.data.bones['hand_R'].matrix_local).inverted() @ rig.data.bones['hand_R'].matrix_local
    delta = source.matrix_world @ source.pose.bones['wpn'].matrix @ weapon_rest.inverted()
    mount = bpy.data.objects.new('TrainingGrip_' + profile, None)
    bpy.context.scene.collection.objects.link(mount)
    mount.parent = rig; mount.parent_type = 'BONE'; mount.parent_bone = 'hand_R'
    bpy.context.view_layer.update()
    mount.matrix_world = rig.data.bones['hand_R'].matrix_local @ hand.inverted() @ delta
    mounts.append(mount)
    for obj in imported: bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in [parent, rig] + mounts: obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(project / 'Assets/Art/Training/PrimaryWeaponGrips.fbx'),
    use_selection=True, object_types={'ARMATURE', 'EMPTY'}, add_leaf_bones=False,
    use_armature_deform_only=False, bake_anim=False, axis_forward='-Z', axis_up='Y')
print('PRIMARY_COMBAT_GRIPS_EXPORTED', len(mounts), flush=True)
