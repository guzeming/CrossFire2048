"""Export combat attachment transforms, separate from the lobby's relaxed dual-pistol grips."""
import sys
from pathlib import Path
import bpy
from mathutils import Matrix

root = Path(__file__).resolve().parents[1]
out = root.parents[1] / 'Assets/Art/Training/EliteTrainingGrips.fbx'
bpy.ops.wm.open_mainfile(filepath=str(root / 'Batch/Weapons/weapon_pist_elite/weapon_pist_elite.blend'))
weapon_rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
grips = {side: weapon_rig.matrix_world @ weapon_rig.data.bones['weapon_hand_' + side].matrix_local for side in ('r', 'l')}
bpy.ops.wm.read_factory_settings(use_empty=True)
with bpy.data.libraries.load(str(root / 'CT_SAS/ctm_sas_animated.blend'), link=False) as (src, dst):
    dst.objects = ['CT_SAS_Rig']
rig = dst.objects[0]
bpy.context.scene.collection.objects.link(rig)
rig.name = 'LobbyRig'; rig.animation_data_clear(); rig.matrix_world = Matrix.Identity(4)
for bone in rig.pose.bones:
    bone.matrix_basis = Matrix.Identity(4)
before = set(bpy.data.objects)
bpy.ops.import_scene.gltf(filepath=str(root / 'Batch/RawAnimations/animation/anims/world/pistol/pistol_elite/idle_elite.vnmclip+non_additive.gltf'))
imported = set(bpy.data.objects) - before
source = next(o for o in imported if o.type == 'ARMATURE' and 'hand_R' in o.data.bones)
weapon = next(o for o in imported if o.type == 'ARMATURE' and 'weapon_hand_r' in o.data.bones)
bpy.context.scene.frame_set(round(source.animation_data.action.frame_range[0])); bpy.context.view_layer.update()
parent = bpy.data.objects.new('LoadoutModel', None); bpy.context.scene.collection.objects.link(parent); rig.parent = parent
mounts = []
for side in ('r', 'l'):
    bone_name = 'hand_' + side.upper()
    raw_hand = source.matrix_world @ source.pose.bones[bone_name].matrix
    hand = raw_hand @ (source.matrix_world @ source.data.bones[bone_name].matrix_local).inverted() @ rig.data.bones[bone_name].matrix_local
    grip = 'weapon_hand_' + side
    delta = source.matrix_world @ source.pose.bones['wpn'].matrix @ weapon.data.bones['weapon'].matrix_local.inverted() @ weapon.pose.bones[grip].matrix @ weapon.data.bones[grip].matrix_local.inverted()
    delta.translation += raw_hand.translation - (delta @ grips[side]).translation
    mount = bpy.data.objects.new('TrainingGrip_' + side.upper(), None); bpy.context.scene.collection.objects.link(mount)
    mount.parent = rig; mount.parent_type = 'BONE'; mount.parent_bone = bone_name
    bpy.context.view_layer.update()
    mount.matrix_world = rig.data.bones[bone_name].matrix_local @ hand.inverted() @ delta
    mounts.append(mount)
for obj in imported: bpy.data.objects.remove(obj, do_unlink=True)
bpy.ops.object.select_all(action='DESELECT')
for obj in [parent, rig] + mounts: obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=str(out), use_selection=True, object_types={'ARMATURE', 'EMPTY'},
    add_leaf_bones=False, use_armature_deform_only=False, bake_anim=False, axis_forward='-Z', axis_up='Y')
print('ELITE_COMBAT_GRIPS_EXPORTED', out, flush=True)
